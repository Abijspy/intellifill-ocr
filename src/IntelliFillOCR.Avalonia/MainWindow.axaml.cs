using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Docnet.Core;
using Docnet.Core.Models;
using IntelliFillOCR.Core;
using SkiaSharp;

namespace IntelliFillOCR.Avalonia;

public sealed partial class MainWindow : Window
{
    private const string AppVersion = "3.7.3";
    private const double PreviewBaseWidth = 1120;
    private const double PreviewBaseHeight = 760;
    private const double PreviewMinZoom = 0.5;
    private const double PreviewMaxZoom = 3.0;
    private const double PreviewZoomStep = 0.25;

    private readonly DocumentLoader _loader = new();
    private readonly ExportService _exportService = new();
    private readonly DatabaseService _databaseService = new();
    private readonly List<DocumentTable> _templateTables = new();
    private readonly List<DocumentItem> _uploadedDocuments = new();
    private readonly List<DocumentPreview> _sourcePreviews = new();
    private readonly List<ExtractedField> _extractedFields = new();
    private readonly List<List<List<string>>> _outputTables = new();
    private readonly List<MappingSnapshot> _mappings = new();
    private readonly Dictionary<TextBox, CellAddress> _outputCellBindings = new();
    private readonly string _appDataPath;
    private readonly string _settingsPath;
    private readonly string _logPath;

    private DocumentPreview? _templatePreview;
    private DocumentPreview? _selectedDocumentPreview;
    private CellAddress? _selectedDestination;
    private ExtractedField? _selectedField;
    private bool _regionSelectionMode;
    private Point? _regionStart;
    private Rect? _selectedRegion;
    private double _previewZoom = 1.0;
    private int _previewRotation;
    private string? _previewBaseRasterPath;
    private string? _previewDisplayRasterPath;
    private AppSettings _settings = new();
    private string _traceabilityCode = CreateTraceabilityCode();

    public MainWindow()
    {
        InitializeComponent();
        _appDataPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelliFillOCR");
        _settingsPath = System.IO.Path.Combine(_appDataPath, "settings.json");
        _logPath = System.IO.Path.Combine(_appDataPath, "logs", "intellifill-avalonia.log");
        Directory.CreateDirectory(_appDataPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_logPath)!);

        _settings = LoadSettings();
        AutoDetectTesseractPath();
        ApplySettingsToUi();
        VersionBadgeText.Text = $"v{AppVersion}";
        StatusText.Text = PackageStatus();
        TraceabilityText.Text = $"Traceability ID: {_traceabilityCode}";
        Log("Avalonia application started.");
        Opened += async (_, _) => await NotifyIfUpdateAvailableAsync();
    }

    private async void UploadTemplate_Click(object? sender, RoutedEventArgs e)
    {
        string? path = await PickSingleDocumentAsync("Upload template");
        if (path is null)
        {
            return;
        }

        try
        {
            DocumentPreview preview = _loader.Load(path);
            _templatePreview = preview;
            _traceabilityCode = CreateTraceabilityCode();
            TraceabilityText.Text = $"Traceability ID: {_traceabilityCode}";
            TemplatePathBox.Text = path;
            LoadTemplatePreview(preview);
            ResetOutputFromTemplate(preview);
            RefreshUploadedFilesList();
            SetStatus($"Template loaded: {System.IO.Path.GetFileName(path)} with {_templateTables.Count} table(s).");
            Log($"Template loaded: {path}");
        }
        catch (Exception ex)
        {
            SetStatus("Template upload failed: " + ex.Message);
            Log("Template upload failed: " + ex);
        }
    }

    private async void UploadSources_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<string> paths = await PickManyDocumentsAsync("Upload source files");
        if (paths.Count == 0)
        {
            return;
        }

        try
        {
            _sourcePreviews.Clear();
            foreach (string path in paths.Take(5))
            {
                _sourcePreviews.Add(_loader.LoadManyText(path));
                Log($"Source loaded: {path}");
            }

            RefreshUploadedFilesList();
            RefreshExtractedFields();
            RefreshParsedText();
            SetStatus($"{_sourcePreviews.Count} source file(s) parsed.");
        }
        catch (Exception ex)
        {
            SetStatus("Source upload failed: " + ex.Message);
            Log("Source upload failed: " + ex);
        }
    }

    private void EnableRegionSelection_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedDocumentPreview is null)
        {
            SetStatus("Upload and select an image/PDF before drawing a region.");
            return;
        }

        if (IsTableOnlyPreviewFormat(_selectedDocumentPreview.Path))
        {
            SetStatus("Region selection is disabled for CSV/Excel table files. Use detected tables for those formats.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_previewBaseRasterPath) || !File.Exists(_previewBaseRasterPath))
        {
            RenderDocumentPreview(_selectedDocumentPreview);
        }

        if (string.IsNullOrWhiteSpace(_previewBaseRasterPath) || !File.Exists(_previewBaseRasterPath))
        {
            SetStatus("A visual preview is not available for this file.");
            return;
        }

        _regionSelectionMode = true;
        _regionStart = null;
        _selectedRegion = null;
        PreviewRegionRectangle.IsVisible = false;
        RegionSelectionText.Text = "Draw a rectangle on the preview.";
        SetStatus("Region selection enabled.");
    }

    private void AutoFill_Click(object? sender, RoutedEventArgs e)
    {
        if (_outputTables.Count == 0)
        {
            SetStatus("Upload a template before auto filling.");
            return;
        }
        if (_extractedFields.Count == 0)
        {
            SetStatus("Upload source files before auto filling.");
            return;
        }

        int filled = 0;
        var details = new List<string>();
        for (int tableIndex = 0; tableIndex < _outputTables.Count; tableIndex++)
        {
            List<List<string>> table = _outputTables[tableIndex];
            for (int row = 0; row < table.Count; row++)
            {
                for (int column = 0; column < table[row].Count; column++)
                {
                    if (!IsFillablePlaceholder(table[row][column]))
                    {
                        continue;
                    }

                    string destinationLabel = DestinationLabel(table, row, column);
                    MatchCandidate? best = _extractedFields
                        .Select(field => new MatchCandidate(field, Similarity(destinationLabel, field.Label)))
                        .OrderByDescending(match => match.Score)
                        .FirstOrDefault();

                    if (best is not null && best.Score >= 0.42)
                    {
                        string value = string.IsNullOrWhiteSpace(best.Field.Value) ? best.Field.Label : best.Field.Value;
                        table[row][column] = value;
                        AddMapping(best.Field, new CellAddress(tableIndex, row, column), value);
                        filled++;
                        details.Add($"{destinationLabel} <- {best.Field.Label} ({best.Score:P0})");
                    }
                }
            }
        }

        RenderOutputTable();
        ValidationBox.Text = filled == 0
            ? "No confident automatic matches were found."
            : "Auto-fill matches:" + Environment.NewLine + string.Join(Environment.NewLine, details.Take(60));
        SetStatus($"{filled} cell(s) auto-filled.");
    }

    private void MapSelected_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedField is null || _selectedDestination is null)
        {
            SetStatus("Select one extracted field and one output cell first.");
            return;
        }

        CellAddress address = _selectedDestination;
        if (!IsValidAddress(address))
        {
            SetStatus("Selected output cell is no longer available.");
            return;
        }

        string value = string.IsNullOrWhiteSpace(_selectedField.Value) ? _selectedField.Label : _selectedField.Value;
        _outputTables[address.TableIndex][address.RowIndex][address.ColumnIndex] = value;
        AddMapping(_selectedField, address, value);
        RenderOutputTable();
        OutputSelectionText.Text = $"Mapped {_selectedField.Label} to table {address.TableIndex + 1}, row {address.RowIndex + 1}, column {address.ColumnIndex + 1}.";
        SetStatus("Mapped selected field.");
    }

    private async void RunValidation_Click(object? sender, RoutedEventArgs e)
    {
        List<string> issues = ValidateOutput();
        ValidationBox.Text = issues.Count == 0 ? "Validation passed. No warnings found." : string.Join(Environment.NewLine, issues);
        await ShowMessageAsync("Validation Results", ValidationBox.Text);
        SetStatus($"Validation complete. {issues.Count} issue(s) found.");
    }

    private void SaveToDatabase_Click(object? sender, RoutedEventArgs e)
    {
        if (_outputTables.Count == 0)
        {
            SetStatus("Upload and fill a template before saving.");
            return;
        }

        try
        {
            _databaseService.SaveRun(
                _settings.DatabasePath,
                _traceabilityCode,
                _templatePreview?.Path ?? string.Empty,
                _sourcePreviews.Select(source => source.Path).ToList(),
                BuildRunValues(),
                _mappings.Select(mapping => $"{mapping.DestinationLabel} <- {mapping.SourceLabel}: {mapping.Value}").ToList());
            DatabasePreviewBox.Text = _databaseService.Preview(_settings.DatabasePath);
            SetStatus("Saved to SQLite: " + _settings.DatabasePath);
        }
        catch (Exception ex)
        {
            SetStatus("SQLite save failed: " + ex.Message);
            Log("SQLite save failed: " + ex);
        }
    }

    private async void ExportCsv_Click(object? sender, RoutedEventArgs e) =>
        await ExportAsync("CSV", ".csv", path => _exportService.ExportCsv(BuildOutputTables(), path, _traceabilityCode));

    private async void ExportExcel_Click(object? sender, RoutedEventArgs e) =>
        await ExportAsync("Excel workbook", ".xlsx", path => _exportService.ExportXlsx(BuildOutputTables(), path, _traceabilityCode));

    private async void ExportWord_Click(object? sender, RoutedEventArgs e) =>
        await ExportAsync("Word document", ".docx", path => _exportService.ExportDocx(BuildOutputTables(), path, _traceabilityCode));

    private async void ExportPdf_Click(object? sender, RoutedEventArgs e) =>
        await ExportAsync("PDF with traceability barcode", ".pdf", path => _exportService.ExportPdf(BuildOutputTables(), path, _traceabilityCode));

    private void RefreshExportPdfPreview_Click(object? sender, RoutedEventArgs e) =>
        RefreshExportPdfPreview();

    private async void DetectSignatures_Click(object? sender, RoutedEventArgs e)
    {
        string text = _sourcePreviews.Count == 0
            ? "Upload source files first. Signature and stamp detection keeps the original documents untouched for export review."
            : "Signature/stamp review candidates:" + Environment.NewLine + string.Join(Environment.NewLine, _sourcePreviews.Select(source => "- " + source.Name));
        await ShowMessageAsync("Signature and Stamp Detection", text);
    }

    private void PreviewDatabase_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            DatabasePreviewBox.Text = _databaseService.Preview(_settings.DatabasePath);
            SetStatus("Database preview refreshed.");
        }
        catch (Exception ex)
        {
            DatabasePreviewBox.Text = ex.ToString();
            SetStatus("Database preview failed: " + ex.Message);
        }
    }

    private async void ViewLogs_Click(object? sender, RoutedEventArgs e)
    {
        string text = File.Exists(_logPath) ? File.ReadAllText(_logPath) : "No log file has been created yet.";
        await ShowMessageAsync("Application Logs", text);
    }

    private async void OpenHelp_Click(object? sender, RoutedEventArgs e)
    {
        await ShowMessageAsync("User Guide and Feature Help", HelpText());
    }

    private async void OpenChangelog_Click(object? sender, RoutedEventArgs e)
    {
        await ShowMessageAsync("IntelliFill OCR Changelog", ChangelogText());
    }

    private async void OpenAbout_Click(object? sender, RoutedEventArgs e)
    {
        await ShowAboutAsync();
    }

    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e)
    {
        Window? progressDialog = null;
        try
        {
            progressDialog = CreateProgressDialog("Check for Updates", "Checking GitHub releases for a newer installer...");
            progressDialog.Show(this);
            SetStatus("Checking for updates...");
            await Task.Delay(250);
            ReleaseUpdate latest = await GetLatestReleaseAsync();
            progressDialog.Close();
            progressDialog = null;
            if (!IsNewerVersion(latest.Version, AppVersion))
            {
                await ShowMessageAsync("Check for Updates", $"You are on the latest version ({AppVersion}).");
                return;
            }

            await PromptForUpdateAsync(latest, isStartupNotice: false);
        }
        catch (Exception ex)
        {
            progressDialog?.Close();
            await ShowMessageAsync("Check for Updates", $"Could not check GitHub releases. Offline use is still supported.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
    }

    private void ApplyTheme_Click(object? sender, RoutedEventArgs e)
    {
        _settings.TesseractPath = string.IsNullOrWhiteSpace(TesseractPathBox.Text) ? DetectTesseractPath() ?? string.Empty : TesseractPathBox.Text;
        _settings.DatabasePath = string.IsNullOrWhiteSpace(DatabasePathBox.Text) ? DefaultDatabasePath() : Environment.ExpandEnvironmentVariables(DatabasePathBox.Text);
        _settings.Theme = ThemeComboBox.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Default"
        };
        SaveSettings();
        ApplyTheme();
        StatusText.Text = PackageStatus();
        SetStatus("Settings saved.");
    }

    private async void BrowseTesseract_Click(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Tesseract executable",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Tesseract executable") { Patterns = OperatingSystem.IsWindows() ? new[] { "*.exe" } : new[] { "*" } },
                FilePickerFileTypes.All
            }
        });

        string? path = files.FirstOrDefault()?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            TesseractPathBox.Text = path;
            SetStatus("Tesseract path selected. Save Settings to keep it.");
        }
    }

    private void AutoDetectTesseract_Click(object? sender, RoutedEventArgs e)
    {
        string? detected = DetectTesseractPath();
        if (string.IsNullOrWhiteSpace(detected))
        {
            SetStatus("Tesseract OCR was not auto-detected. Install Tesseract or choose tesseract.exe manually.");
            return;
        }

        TesseractPathBox.Text = detected;
        _settings.TesseractPath = detected;
        SaveSettings();
        StatusText.Text = PackageStatus();
        SetStatus("Tesseract OCR auto-detected and saved: " + detected);
        Log("Tesseract auto-detected by Settings button: " + detected);
    }

    private async void BrowseDatabase_Click(object? sender, RoutedEventArgs e)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Select SQLite database",
            SuggestedFileName = "intellifill.sqlite3",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SQLite database") { Patterns = new[] { "*.sqlite3", "*.sqlite", "*.db" } },
                FilePickerFileTypes.All
            }
        });

        string? path = file?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            DatabasePathBox.Text = path;
            SetStatus("SQLite database path selected. Save Settings to keep it.");
        }
    }

    private void ShowTemplatePage_Click(object? sender, RoutedEventArgs e) =>
        ShowPage(TemplatePage, TemplatePageButton);

    private void ShowSourcesPage_Click(object? sender, RoutedEventArgs e) =>
        ShowPage(SourcesPage, SourcesPageButton);

    private void ShowMappingPage_Click(object? sender, RoutedEventArgs e) =>
        ShowPage(MappingPage, MappingPageButton);

    private void ShowReviewPage_Click(object? sender, RoutedEventArgs e)
    {
        RenderReviewOutputTable();
        RefreshExportPdfPreview();
        ShowPage(ReviewPage, ReviewPageButton);
    }

    private void ShowSettingsPage_Click(object? sender, RoutedEventArgs e) =>
        ShowPage(SettingsPage, SettingsPageButton);

    private void ShowPage(Control page, Button selectedButton)
    {
        Control[] pages = { TemplatePage, SourcesPage, MappingPage, ReviewPage, SettingsPage };
        Button[] buttons = { TemplatePageButton, SourcesPageButton, MappingPageButton, ReviewPageButton, SettingsPageButton };

        foreach (Control candidate in pages)
        {
            candidate.IsVisible = ReferenceEquals(candidate, page);
        }

        foreach (Button button in buttons)
        {
            button.Classes.Remove("primary");
        }
        selectedButton.Classes.Add("primary");
    }

    private void DocumentsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = DocumentsListBox.SelectedIndex;
        SelectDocumentPreview(index >= 0 && index < _uploadedDocuments.Count ? _uploadedDocuments[index] : null);
    }

    private void TemplateTableSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = TemplateTableSelector.SelectedIndex;
        if (index >= 0 && index < _templateTables.Count)
        {
            RenderReadOnlyGrid(TemplatePreviewGrid, _templateTables[index].Rows, _templateTables[index].Label);
        }
    }

    private void SourceTableSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RenderSelectedSourceTable();
    }

    private void OutputTableSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (OutputTableSelector.SelectedIndex >= 0 && OutputTableSelector.SelectedIndex < ReviewOutputTableSelector.Items.Count)
        {
            ReviewOutputTableSelector.SelectedIndex = OutputTableSelector.SelectedIndex;
        }
        RenderOutputTable();
    }

    private void ReviewOutputTableSelector_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        RenderReviewOutputTable();
    }

    private void ExtractedFieldsListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        int index = ExtractedFieldsListBox.SelectedIndex;
        _selectedField = index >= 0 && index < _extractedFields.Count ? _extractedFields[index] : null;
    }

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => SetPreviewZoom(_previewZoom - PreviewZoomStep);

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) => SetPreviewZoom(_previewZoom + PreviewZoomStep);

    private void ResetPreview_Click(object? sender, RoutedEventArgs e) => ResetPreviewView();

    private void RotateLeft_Click(object? sender, RoutedEventArgs e)
    {
        _previewRotation = NormalizeRotation(_previewRotation - 90);
        ApplyPreviewView(resetSelection: true);
    }

    private void RotateRight_Click(object? sender, RoutedEventArgs e)
    {
        _previewRotation = NormalizeRotation(_previewRotation + 90);
        ApplyPreviewView(resetSelection: true);
    }

    private void PreviewCanvas_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!_regionSelectionMode)
        {
            return;
        }

        _regionStart = e.GetPosition(PreviewCanvas);
        Canvas.SetLeft(PreviewRegionRectangle, _regionStart.Value.X);
        Canvas.SetTop(PreviewRegionRectangle, _regionStart.Value.Y);
        PreviewRegionRectangle.Width = 0;
        PreviewRegionRectangle.Height = 0;
        PreviewRegionRectangle.IsVisible = true;
        e.Handled = true;
    }

    private void PreviewCanvas_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_regionSelectionMode || _regionStart is null)
        {
            return;
        }

        Point current = e.GetPosition(PreviewCanvas);
        UpdateSelectionRectangle(_regionStart.Value, current);
        e.Handled = true;
    }

    private async void PreviewCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_regionSelectionMode || _regionStart is null)
        {
            return;
        }

        Point end = e.GetPosition(PreviewCanvas);
        _selectedRegion = UpdateSelectionRectangle(_regionStart.Value, end);
        _regionSelectionMode = false;
        _regionStart = null;
        e.Handled = true;

        if (_selectedRegion.Value.Width < 8 || _selectedRegion.Value.Height < 8)
        {
            PreviewRegionRectangle.IsVisible = false;
            RegionSelectionText.Text = "Selection too small.";
            SetStatus("Draw a larger rectangle on the preview.");
            return;
        }

        RegionSelectionText.Text = FormatRegion(_selectedRegion.Value);
        await AddSelectedRegionFieldAsync(_selectedRegion.Value);
    }

    private void OpenOriginal_Click(object? sender, RoutedEventArgs e)
    {
        if (_selectedDocumentPreview is null || !File.Exists(_selectedDocumentPreview.Path))
        {
            SetStatus("Select an uploaded document first.");
            return;
        }

        Process.Start(new ProcessStartInfo(_selectedDocumentPreview.Path) { UseShellExecute = true });
    }

    private async Task NotifyIfUpdateAvailableAsync()
    {
        await Task.Delay(1200);
        try
        {
            ReleaseUpdate latest = await GetLatestReleaseAsync();
            if (IsNewerVersion(latest.Version, AppVersion))
            {
                await PromptForUpdateAsync(latest, isStartupNotice: true);
            }
        }
        catch (Exception ex)
        {
            Log("Startup update check skipped: " + ex.Message);
        }
    }

    private static async Task<ReleaseUpdate> GetLatestReleaseAsync()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IntelliFillOCR-Avalonia");
        string json = await client.GetStringAsync("https://api.github.com/repos/Abijspy/intellifill-ocr/releases/latest");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "v0.0.0";
        string version = tag.TrimStart('v');
        string releaseUrl = root.TryGetProperty("html_url", out JsonElement htmlUrl) ? htmlUrl.GetString() ?? string.Empty : string.Empty;
        string notes = root.TryGetProperty("body", out JsonElement bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
        var candidates = new List<ReleaseUpdate>();

        if (root.TryGetProperty("assets", out JsonElement assets))
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out JsonElement nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                if (!IsPreferredUpdateAsset(name))
                {
                    continue;
                }

                string downloadUrl = asset.TryGetProperty("browser_download_url", out JsonElement urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
                candidates.Add(new ReleaseUpdate(version, tag, releaseUrl, name, downloadUrl, notes));
            }
        }

        ReleaseUpdate? versionMatched = candidates.FirstOrDefault(candidate => AssetMatchesVersion(candidate.AssetName, version));
        return versionMatched ?? candidates.FirstOrDefault() ?? new ReleaseUpdate(version, tag, releaseUrl, string.Empty, string.Empty, notes);
    }

    private static bool AssetMatchesVersion(string assetName, string version)
    {
        if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        return Regex.IsMatch(
            assetName,
            $@"(^|[-_])v?{Regex.Escape(version)}($|[-_.])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsPreferredUpdateAsset(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                   name.Contains("setup-win-x64", StringComparison.OrdinalIgnoreCase);
        }

        if (OperatingSystem.IsLinux())
        {
            return name.EndsWith(".deb", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
        }

        return name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PromptForUpdateAsync(ReleaseUpdate latest, bool isStartupNotice)
    {
        string installText = !string.IsNullOrWhiteSpace(latest.AssetName) && OperatingSystem.IsWindows()
            ? $"Download and install {latest.AssetName} now?"
            : "Open the GitHub release page to download the package for this operating system?";
        string title = isStartupNotice ? "Update Available" : "Check for Updates";
        string body = $"IntelliFill OCR {latest.Version} is available.{Environment.NewLine}Current version: {AppVersion}{Environment.NewLine}{Environment.NewLine}{installText}{Environment.NewLine}{Environment.NewLine}What's new in this update:{Environment.NewLine}{LatestReleaseNotes(latest)}";
        string primary = !string.IsNullOrWhiteSpace(latest.AssetName) && OperatingSystem.IsWindows()
            ? "Download and Install"
            : "Open Release Page";

        string? choice = await ShowChoiceAsync(title, body, primary, "Later");
        if (choice != "primary")
        {
            SetStatus($"Update {latest.Version} is available.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(latest.AssetName) && OperatingSystem.IsWindows())
        {
            try
            {
                await DownloadAndRunUpdateAsync(latest);
            }
            catch (Exception ex)
            {
                SetStatus("Update install failed: " + ex.Message);
                Log("Update install failed: " + ex);
                await ShowMessageAsync(
                    "Update Install Failed",
                    $"The update could not be downloaded or launched automatically.{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}The GitHub release page will open so you can download the installer manually.");
                OpenUrl(latest.ReleaseUrl);
            }
            return;
        }

        OpenUrl(latest.ReleaseUrl);
    }

    private static string LatestReleaseNotes(ReleaseUpdate latest)
    {
        if (string.IsNullOrWhiteSpace(latest.Notes))
        {
            return "No release notes were published for this update.";
        }

        string notes = latest.Notes.Trim();
        return notes.Length <= 1800 ? notes : notes[..1800] + Environment.NewLine + "...";
    }

    private async Task DownloadAndRunUpdateAsync(ReleaseUpdate latest)
    {
        if (string.IsNullOrWhiteSpace(latest.DownloadUrl) || string.IsNullOrWhiteSpace(latest.AssetName))
        {
            OpenUrl(latest.ReleaseUrl);
            return;
        }

        string updateDirectory = System.IO.Path.Combine(_appDataPath, "Updates");
        Directory.CreateDirectory(updateDirectory);

        string safeAssetName = Regex.Replace(latest.AssetName, @"[^\w.\-]+", "_");
        string targetPath = System.IO.Path.Combine(updateDirectory, safeAssetName);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        SetStatus($"Downloading IntelliFill OCR {latest.Version} installer...");
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IntelliFillOCR-Avalonia");
        client.Timeout = TimeSpan.FromMinutes(15);

        using HttpResponseMessage response = await client.GetAsync(latest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        await using (Stream source = await response.Content.ReadAsStreamAsync())
        await using (FileStream destination = File.Create(targetPath))
        {
            byte[] buffer = new byte[1024 * 128];
            long downloaded = 0;
            int lastProgress = -1;
            while (true)
            {
                int read = await source.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (contentLength is long totalBytes && totalBytes > 0)
                {
                    int progress = (int)Math.Clamp(downloaded * 100 / totalBytes, 0, 100);
                    if (progress >= lastProgress + 5 || progress == 100)
                    {
                        lastProgress = progress;
                        SetStatus($"Downloading IntelliFill OCR {latest.Version} installer... {progress}%");
                    }
                }
            }

            await destination.FlushAsync();
        }

        var installer = new FileInfo(targetPath);
        if (!installer.Exists || installer.Length < 100_000)
        {
            throw new InvalidOperationException("Downloaded installer is missing or incomplete.");
        }

        SetStatus($"Download complete. Launching update installer after IntelliFill OCR closes: {targetPath}");
        LaunchInstallerAfterExit(targetPath, updateDirectory, latest.Version);
        await Task.Delay(500);
        Close();
    }

    private void LaunchInstallerAfterExit(string installerPath, string updateDirectory, string version)
    {
        string launcherPath = System.IO.Path.Combine(updateDirectory, $"launch-intellifill-update-{version}.cmd");
        string logPath = System.IO.Path.Combine(updateDirectory, "update-launch.log");
        string processId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        string script = $"""
@echo off
setlocal
set "INSTALLER={installerPath}"
set "LOG={logPath}"
echo [%date% %time%] IntelliFill OCR update handoff started.>"%LOG%"
echo Installer: %INSTALLER%>>"%LOG%"
for /L %%I in (1,1,60) do (
  tasklist /FI "PID eq {processId}" | find "{processId}" >nul
  if errorlevel 1 goto launch
  timeout /t 1 /nobreak >nul
)
echo Existing app process still running. Stopping PID {processId}.>>"%LOG%"
taskkill /PID {processId} /F >>"%LOG%" 2>&1
timeout /t 2 /nobreak >nul
:launch
if not exist "%INSTALLER%" (
  echo Installer missing: %INSTALLER%>>"%LOG%"
  exit /b 2
)
timeout /t 2 /nobreak >nul
echo Launching installer.>>"%LOG%"
start "IntelliFill OCR Setup" /wait "%INSTALLER%"
set "INSTALL_EXIT=%ERRORLEVEL%"
echo Installer finished with exit code %INSTALL_EXIT%.>>"%LOG%"
echo Cleaning downloaded installer after setup exits.>>"%LOG%"
for /L %%I in (1,1,30) do (
  del /f /q "%INSTALLER%" >>"%LOG%" 2>&1
  if not exist "%INSTALLER%" goto cleanup_complete
  echo Installer still locked. Cleanup retry %%I of 30.>>"%LOG%"
  timeout /t 2 /nobreak >nul
)
echo Installer cleanup was deferred because the file stayed locked.>>"%LOG%"
:cleanup_complete
if not exist "%INSTALLER%" echo Downloaded installer package deleted.>>"%LOG%"
del "%~f0" >nul 2>&1
exit /b %INSTALL_EXIT%
""";

        File.WriteAllText(launcherPath, script);
        string shell = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        Process? launcher = Process.Start(new ProcessStartInfo
        {
            FileName = shell,
            Arguments = $"/c start \"IntelliFill OCR Update\" /min \"{launcherPath}\"",
            WorkingDirectory = updateDirectory,
            CreateNoWindow = true,
            UseShellExecute = false
        });
        if (launcher is null)
        {
            throw new InvalidOperationException("Windows did not start the update handoff process.");
        }
    }

    private async Task<string?> ShowChoiceAsync(string title, string text, string primaryText, string closeText)
    {
        Button primaryButton = CreateDialogButton(primaryText, isPrimary: true, minWidth: 160);
        Button closeButton = CreateDialogButton(closeText, isPrimary: false, minWidth: 110);

        var body = CreateDialogTextPanel(text);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { primaryButton, closeButton }
        };

        (double width, double height) = DialogSizeFor(text);
        Window box = CreateStyledDialog(title, Math.Max(600, width), Math.Max(360, height), body, buttons);
        primaryButton.Click += (_, _) => box.Close("primary");
        closeButton.Click += (_, _) => box.Close("close");
        return await box.ShowDialog<string?>(this);
    }

    private static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void LoadTemplatePreview(DocumentPreview preview)
    {
        _templateTables.Clear();
        _templateTables.AddRange(preview.Tables);
        TemplateTableSelector.Items.Clear();
        for (int index = 0; index < _templateTables.Count; index++)
        {
            DocumentTable table = _templateTables[index];
            TemplateTableSelector.Items.Add($"{table.Label} ({table.RowCount} x {table.ColumnCount})");
        }

        TemplateTableSelector.SelectedIndex = _templateTables.Count > 0 ? 0 : -1;
        TemplateSummaryText.Text = _templateTables.Count == 0
            ? "No tables were detected."
            : $"{_templateTables.Count} table(s) detected. Export includes every output table in one document.";
        if (_templateTables.Count > 0)
        {
            RenderReadOnlyGrid(TemplatePreviewGrid, _templateTables[0].Rows, _templateTables[0].Label);
        }
    }

    private void ResetOutputFromTemplate(DocumentPreview preview)
    {
        _outputTables.Clear();
        foreach (DocumentTable table in preview.Tables)
        {
            _outputTables.Add(table.Rows.Select(row => row.Select(value => value ?? string.Empty).ToList()).ToList());
        }
        if (_outputTables.Count == 0)
        {
            _outputTables.Add(new List<List<string>> { new() { "Field", "" } });
        }

        _mappings.Clear();
        OutputTableSelector.Items.Clear();
        ReviewOutputTableSelector.Items.Clear();
        for (int index = 0; index < _outputTables.Count; index++)
        {
            string label = TableLabel(index);
            OutputTableSelector.Items.Add(label);
            ReviewOutputTableSelector.Items.Add(label);
        }
        OutputTableSelector.SelectedIndex = 0;
        ReviewOutputTableSelector.SelectedIndex = 0;
        RenderOutputTable();
        RenderReviewOutputTable();
    }

    private void RefreshUploadedFilesList()
    {
        int previousIndex = DocumentsListBox.SelectedIndex;
        _uploadedDocuments.Clear();
        DocumentsListBox.Items.Clear();
        if (_templatePreview is not null)
        {
            _uploadedDocuments.Add(new DocumentItem("Template", _templatePreview));
            DocumentsListBox.Items.Add($"Template: {System.IO.Path.GetFileName(_templatePreview.Path)} - {_templatePreview.Tables.Count} table(s)");
        }
        foreach (DocumentPreview preview in _sourcePreviews)
        {
            _uploadedDocuments.Add(new DocumentItem("Source", preview));
            DocumentsListBox.Items.Add($"Source: {System.IO.Path.GetFileName(preview.Path)} - {preview.Tables.Count} table(s)");
        }

        if (_uploadedDocuments.Count == 0)
        {
            SelectDocumentPreview(null);
            return;
        }

        DocumentsListBox.SelectedIndex = previousIndex >= 0 && previousIndex < _uploadedDocuments.Count ? previousIndex : 0;
    }

    private void SelectDocumentPreview(DocumentItem? item)
    {
        _selectedDocumentPreview = item?.Preview;
        SourceTableSelector.Items.Clear();
        ClearGrid(SourceTablePreviewGrid);
        PreviewRegionRectangle.IsVisible = false;
        RegionSelectionText.Text = "No region selected";
        _previewBaseRasterPath = null;
        _previewDisplayRasterPath = null;
        ResetPreviewView();

        if (_selectedDocumentPreview is null)
        {
            SelectedDocumentText.Text = "Upload and select a document.";
            ParsedTextBox.Text = string.Empty;
            PreviewImage.IsVisible = false;
            PreviewMessage.IsVisible = true;
            return;
        }

        SelectedDocumentText.Text = $"{item?.Kind}: {System.IO.Path.GetFileName(_selectedDocumentPreview.Path)}";
        ParsedTextBox.Text = _selectedDocumentPreview.ParsedText;
        foreach (DocumentTable table in _selectedDocumentPreview.Tables)
        {
            SourceTableSelector.Items.Add($"{table.Label} ({table.RowCount} x {table.ColumnCount})");
        }
        SourceTableSelector.SelectedIndex = _selectedDocumentPreview.Tables.Count > 0 ? 0 : -1;
        RenderDocumentPreview(_selectedDocumentPreview);
    }

    private void RenderDocumentPreview(DocumentPreview preview)
    {
        string extension = System.IO.Path.GetExtension(preview.Path).ToLowerInvariant();
        _previewBaseRasterPath = null;
        _previewDisplayRasterPath = null;
        PreviewImage.IsVisible = false;
        PreviewMessage.IsVisible = true;
        PreviewMessage.Text = "Preview will appear here for uploaded documents. CSV and Excel files use visual table preview without OCR region selection.";

        try
        {
            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                _previewBaseRasterPath = RenderImagePreviewToPng(preview.Path);
                RefreshPreviewImageSource();
                RegionSelectionText.Text = "Use Select Region to draw OCR area.";
                return;
            }

            if (extension == ".pdf")
            {
                _previewBaseRasterPath = RenderPdfPreviewToPng(preview.Path);
                RefreshPreviewImageSource();
                RegionSelectionText.Text = "Use Select Region to draw PDF area.";
                return;
            }

            _previewBaseRasterPath = RenderTextDocumentPreviewToPng(preview);
            RefreshPreviewImageSource();
            RegionSelectionText.Text = IsTableOnlyPreviewFormat(preview.Path)
                ? "CSV/Excel visual preview. Region selection disabled; use detected tables."
                : "Use Select Region to draw document text area.";
            return;
        }
        catch (Exception ex)
        {
            PreviewMessage.Text = $"Could not render preview: {ex.Message}";
            Log($"Preview render failed for {preview.Path}: {ex}");
        }

        RegionSelectionText.Text = "Preview unavailable.";
    }

    private string RenderImagePreviewToPng(string path)
    {
        using SKBitmap source = SKBitmap.Decode(path) ?? throw new InvalidDataException("The image could not be decoded.");
        return SaveCanvasPreview(source, "image");
    }

    private string RenderPdfPreviewToPng(string path, int pageIndex = 0)
    {
        byte[] pdfBytes = File.ReadAllBytes(path);
        using var docReader = DocLib.Instance.GetDocReader(pdfBytes, new PageDimensions(2600, 3600));
        int pageCount = Math.Max(1, docReader.GetPageCount());
        int safePageIndex = pageIndex < 0 ? pageCount - 1 : Math.Clamp(pageIndex, 0, pageCount - 1);
        using var pageReader = docReader.GetPageReader(safePageIndex);
        int width = pageReader.GetPageWidth();
        int height = pageReader.GetPageHeight();
        byte[] rawBytes = pageReader.GetImage();

        using var source = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        Marshal.Copy(rawBytes, 0, source.GetPixels(), Math.Min(rawBytes.Length, width * height * 4));
        return SaveCanvasPreview(source, safePageIndex == 0 ? "pdf" : $"pdf-page-{safePageIndex + 1}");
    }

    private string RenderTextDocumentPreviewToPng(DocumentPreview preview)
    {
        int width = (int)PreviewBaseWidth;
        int height = (int)PreviewBaseHeight;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not create the document preview drawing surface.");

        SKCanvas canvas = surface.Canvas;
        canvas.Clear(PreviewBackgroundColor());
        using var pagePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var borderPaint = new SKPaint { Color = new SKColor(148, 163, 184), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
        using var titlePaint = new SKPaint { Color = new SKColor(15, 23, 42), IsAntialias = true };
        using var metaPaint = new SKPaint { Color = new SKColor(71, 85, 105), IsAntialias = true };
        using var bodyPaint = new SKPaint { Color = new SKColor(17, 24, 39), IsAntialias = true };
        using var titleFont = new SKFont { Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), Size = 26 };
        using var metaFont = new SKFont { Typeface = SKTypeface.FromFamilyName("Segoe UI"), Size = 15 };
        using var bodyFont = new SKFont { Typeface = SKTypeface.FromFamilyName("Segoe UI"), Size = 18 };

        var pageRect = new SKRect(78, 38, width - 78, height - 38);
        canvas.DrawRoundRect(pageRect, 10, 10, pagePaint);
        canvas.DrawRoundRect(pageRect, 10, 10, borderPaint);

        float x = pageRect.Left + 34;
        float y = pageRect.Top + 48;
        float maxWidth = pageRect.Width - 68;
        canvas.DrawText(System.IO.Path.GetFileName(preview.Path), x, y, titleFont, titlePaint);
        y += 26;
        canvas.DrawText("Generated visual preview for region selection", x, y, metaFont, metaPaint);
        y += 34;

        foreach (string rawLine in PreviewDocumentLines(preview))
        {
            foreach (string line in WrapPreviewLine(rawLine, bodyFont, bodyPaint, maxWidth))
            {
                if (y > pageRect.Bottom - 42)
                {
                    canvas.DrawText("Preview truncated. Use parsed text or tables below for the full document.", x, pageRect.Bottom - 20, metaFont, metaPaint);
                    string truncatedPath = NewPreviewPath("document");
                    SaveSurfacePng(surface, truncatedPath);
                    return truncatedPath;
                }

                canvas.DrawText(line, x, y, bodyFont, bodyPaint);
                y += 24;
            }

            y += 4;
        }

        string outputPath = NewPreviewPath("document");
        SaveSurfacePng(surface, outputPath);
        return outputPath;
    }

    private static IEnumerable<string> PreviewDocumentLines(DocumentPreview preview)
    {
        var yielded = false;
        foreach (string line in preview.ParsedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            string normalized = Regex.Replace(line.Trim(), @"\s+", " ");
            if (normalized.Length == 0)
            {
                continue;
            }

            yielded = true;
            yield return normalized;
        }

        if (yielded)
        {
            yield break;
        }

        foreach (DocumentTable table in preview.Tables)
        {
            yield return table.Label;
            foreach (IReadOnlyList<string> row in table.Rows.Take(40))
            {
                string text = Regex.Replace(string.Join("  |  ", row).Trim(), @"\s+", " ");
                if (text.Length > 0)
                {
                    yield return text;
                }
            }
        }
    }

    private static IEnumerable<string> WrapPreviewLine(string value, SKFont font, SKPaint paint, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield return string.Empty;
            yield break;
        }

        var current = new StringBuilder();
        foreach (string word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate, paint) <= maxWidth)
            {
                current.Clear();
                current.Append(candidate);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
                current.Clear();
            }

            if (font.MeasureText(word, paint) <= maxWidth)
            {
                current.Append(word);
                continue;
            }

            foreach (string chunk in BreakLongPreviewWord(word, font, paint, maxWidth))
            {
                yield return chunk;
            }
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static IEnumerable<string> BreakLongPreviewWord(string word, SKFont font, SKPaint paint, float maxWidth)
    {
        var current = new StringBuilder();
        foreach (char character in word)
        {
            string candidate = current.ToString() + character;
            if (current.Length > 0 && font.MeasureText(candidate, paint) > maxWidth)
            {
                yield return current.ToString();
                current.Clear();
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static bool IsTableOnlyPreviewFormat(string path)
    {
        string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return extension is ".csv" or ".xlsx" or ".xls";
    }

    private string SaveCanvasPreview(SKBitmap source, string suffix)
    {
        int width = (int)PreviewBaseWidth;
        int height = (int)PreviewBaseHeight;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not create the preview drawing surface.");
        surface.Canvas.Clear(PreviewBackgroundColor());
        SKRect destination = FitWithin(source.Width, source.Height, width, height);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawBitmap(source, destination, paint);

        string outputPath = NewPreviewPath(suffix);
        SaveSurfacePng(surface, outputPath);
        return outputPath;
    }

    private bool RefreshPreviewImageSource()
    {
        if (string.IsNullOrWhiteSpace(_previewBaseRasterPath) || !File.Exists(_previewBaseRasterPath))
        {
            return false;
        }

        try
        {
            _previewDisplayRasterPath = _previewRotation == 0
                ? _previewBaseRasterPath
                : RenderRotatedPreview(_previewBaseRasterPath, _previewRotation);
            using FileStream stream = File.OpenRead(_previewDisplayRasterPath);
            PreviewImage.Source = new Bitmap(stream);
            PreviewImage.IsVisible = true;
            PreviewMessage.IsVisible = false;
            return true;
        }
        catch (Exception ex)
        {
            PreviewImage.IsVisible = false;
            PreviewMessage.IsVisible = true;
            PreviewMessage.Text = $"Could not refresh preview: {ex.Message}";
            Log("Preview refresh failed: " + ex);
            return false;
        }
    }

    private string RenderRotatedPreview(string sourcePath, int rotation)
    {
        using SKBitmap source = SKBitmap.Decode(sourcePath) ?? throw new InvalidDataException("The preview image could not be decoded.");
        int width = (int)PreviewBaseWidth;
        int height = (int)PreviewBaseHeight;
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Could not create the rotated preview drawing surface.");
        surface.Canvas.Clear(PreviewBackgroundColor());
        surface.Canvas.Translate(width / 2f, height / 2f);
        surface.Canvas.RotateDegrees(rotation);
        float rotatedWidth = rotation is 90 or 270 ? source.Height : source.Width;
        float rotatedHeight = rotation is 90 or 270 ? source.Width : source.Height;
        float scale = Math.Min(width / rotatedWidth, height / rotatedHeight);
        surface.Canvas.Scale(scale);
        surface.Canvas.DrawBitmap(source, new SKRect(-source.Width / 2f, -source.Height / 2f, source.Width / 2f, source.Height / 2f));

        string outputPath = NewPreviewPath($"rotated-{rotation}");
        SaveSurfacePng(surface, outputPath);
        return outputPath;
    }

    private string? CropSelectedRegionToPng(Rect region)
    {
        if (string.IsNullOrWhiteSpace(_previewDisplayRasterPath) || !File.Exists(_previewDisplayRasterPath))
        {
            return null;
        }

        using SKBitmap source = SKBitmap.Decode(_previewDisplayRasterPath) ?? throw new InvalidDataException("The preview image could not be decoded.");
        double zoom = Math.Max(_previewZoom, 0.01);
        int left = (int)Math.Clamp(Math.Floor(region.X / zoom), 0, Math.Max(0, source.Width - 1));
        int top = (int)Math.Clamp(Math.Floor(region.Y / zoom), 0, Math.Max(0, source.Height - 1));
        int right = (int)Math.Clamp(Math.Ceiling((region.X + region.Width) / zoom), left + 1, source.Width);
        int bottom = (int)Math.Clamp(Math.Ceiling((region.Y + region.Height) / zoom), top + 1, source.Height);
        int width = right - left;
        int height = bottom - top;
        if (width < 2 || height < 2)
        {
            return null;
        }

        using var crop = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(crop);
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(source, new SKRectI(left, top, right, bottom), new SKRect(0, 0, width, height));

        string outputPath = NewPreviewPath("region");
        SaveBitmapPng(crop, outputPath);
        return outputPath;
    }

    private async Task<string?> RunTesseractAsync(string imagePath)
    {
        string executable = ResolveTesseractExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(imagePath);
        startInfo.ArgumentList.Add("stdout");
        startInfo.ArgumentList.Add("-l");
        startInfo.ArgumentList.Add("eng");
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add("6");

        using Process process = Process.Start(startInfo) ?? throw new InvalidOperationException("Tesseract could not be started.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0)
        {
            Log($"Tesseract exited with code {process.ExitCode}: {error}");
            return null;
        }

        return Regex.Replace(output, @"\s+", " ").Trim();
    }

    private string ResolveTesseractExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_settings.TesseractPath) && File.Exists(_settings.TesseractPath))
        {
            return _settings.TesseractPath;
        }

        string? detected = DetectTesseractPath();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            _settings.TesseractPath = detected;
            TesseractPathBox.Text = detected;
            SaveSettings();
            return detected;
        }

        throw new InvalidOperationException("Tesseract OCR was not found. Install Tesseract or choose tesseract.exe in Settings.");
    }

    private void AutoDetectTesseractPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.TesseractPath) && File.Exists(_settings.TesseractPath))
        {
            return;
        }

        string? detected = DetectTesseractPath();
        if (!string.IsNullOrWhiteSpace(detected))
        {
            _settings.TesseractPath = detected;
            SaveSettings();
            Log("Tesseract auto-detected: " + detected);
        }
    }

    private static string? DetectTesseractPath()
    {
        if (OperatingSystem.IsWindows())
        {
            string[] candidates =
            {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tesseract-OCR", "tesseract.exe"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tesseract-OCR", "tesseract.exe")
            };

            string? match = candidates.FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        string executableName = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            string path = System.IO.Path.Combine(directory.Trim(), executableName);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private string NewPreviewPath(string suffix)
    {
        string directory = System.IO.Path.Combine(_appDataPath, "PreviewCache");
        Directory.CreateDirectory(directory);
        return System.IO.Path.Combine(directory, $"preview-{Guid.NewGuid():N}-{suffix}.png");
    }

    private string NewPreviewCachePath(string suffix, string extension)
    {
        string directory = System.IO.Path.Combine(_appDataPath, "PreviewCache");
        Directory.CreateDirectory(directory);
        string normalizedExtension = extension.StartsWith('.') ? extension : "." + extension;
        return System.IO.Path.Combine(directory, $"preview-{Guid.NewGuid():N}-{suffix}{normalizedExtension}");
    }

    private SKColor PreviewBackgroundColor()
    {
        try
        {
            if (Resources["PreviewCanvasBrush"] is SolidColorBrush brush)
            {
                Color color = brush.Color;
                return new SKColor(color.R, color.G, color.B, color.A);
            }
        }
        catch
        {
            // Use the light preview color if the dynamic resource is not available yet.
        }

        return new SKColor(238, 243, 255);
    }

    private static SKRect FitWithin(float sourceWidth, float sourceHeight, float targetWidth, float targetHeight)
    {
        float scale = Math.Min(targetWidth / sourceWidth, targetHeight / sourceHeight);
        float width = sourceWidth * scale;
        float height = sourceHeight * scale;
        float left = (targetWidth - width) / 2f;
        float top = (targetHeight - height) / 2f;
        return new SKRect(left, top, left + width, top + height);
    }

    private static void SaveSurfacePng(SKSurface surface, string outputPath)
    {
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 95)
            ?? throw new InvalidOperationException("Preview image encoding failed.");
        using FileStream stream = File.Create(outputPath);
        data.SaveTo(stream);
    }

    private static void SaveBitmapPng(SKBitmap bitmap, string outputPath)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 95)
            ?? throw new InvalidOperationException("Region image encoding failed.");
        using FileStream stream = File.Create(outputPath);
        data.SaveTo(stream);
    }

    private static void TryDeleteFile(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Cache cleanup should never interrupt the user workflow.
        }
    }

    private void RenderSelectedSourceTable()
    {
        ClearGrid(SourceTablePreviewGrid);
        if (_selectedDocumentPreview is null)
        {
            return;
        }

        int index = SourceTableSelector.SelectedIndex;
        if (index < 0 || index >= _selectedDocumentPreview.Tables.Count)
        {
            return;
        }

        RenderReadOnlyGrid(SourceTablePreviewGrid, _selectedDocumentPreview.Tables[index].Rows, _selectedDocumentPreview.Tables[index].Label);
    }

    private void RefreshParsedText()
    {
        ParsedTextBox.Text = _selectedDocumentPreview is not null
            ? _selectedDocumentPreview.ParsedText
            : string.Join(Environment.NewLine + Environment.NewLine, _sourcePreviews.Select(preview => $"[{preview.Name}]{Environment.NewLine}{preview.ParsedText}"));
    }

    private void RefreshExtractedFields()
    {
        _extractedFields.Clear();
        ExtractedFieldsListBox.Items.Clear();
        foreach (DocumentPreview preview in _sourcePreviews)
        {
            foreach (ExtractedField field in ExtractFields(preview))
            {
                if (_extractedFields.Any(existing => existing.Label == field.Label && existing.Value == field.Value))
                {
                    continue;
                }
                AddExtractedField(field);
            }
        }
    }

    private async Task AddSelectedRegionFieldAsync(Rect region)
    {
        if (_selectedDocumentPreview is null)
        {
            return;
        }

        string label = $"Region from {System.IO.Path.GetFileNameWithoutExtension(_selectedDocumentPreview.Path)}";
        string value = FormatRegion(region);
        double confidence = 0.80;
        string status = "Region field added. Select an output cell and choose Map Selected.";
        string? cropPath = null;

        try
        {
            cropPath = CropSelectedRegionToPng(region);
            if (!string.IsNullOrWhiteSpace(cropPath))
            {
                string? ocrText = await RunTesseractAsync(cropPath);
                if (!string.IsNullOrWhiteSpace(ocrText))
                {
                    value = ocrText.Trim();
                    confidence = 0.88;
                    status = "OCR text extracted from selected region. Select an output cell and choose Map Selected.";
                }
                else
                {
                    status = "Region selected, but OCR returned no readable text. Coordinates were added as the field value.";
                }
            }
        }
        catch (Exception ex)
        {
            status = "Region selected, but OCR extraction failed: " + ex.Message;
            Log("Region OCR failed: " + ex);
        }
        finally
        {
            TryDeleteFile(cropPath);
        }

        AddExtractedField(new ExtractedField(_extractedFields.Count + 1, _selectedDocumentPreview.Name, label, value, confidence));
        SetStatus(status);
    }

    private void AddExtractedField(ExtractedField field)
    {
        _extractedFields.Add(field);
        ExtractedFieldsListBox.Items.Add($"{field.Label}: {field.Value}  ({field.SourceName}, {field.Confidence:P0})");
        ExtractedFieldsListBox.SelectedIndex = _extractedFields.Count - 1;
    }

    private static IEnumerable<ExtractedField> ExtractFields(DocumentPreview preview)
    {
        int id = 1;
        foreach (DocumentTable table in preview.Tables)
        {
            foreach (IReadOnlyList<string> row in table.Rows)
            {
                for (int column = 0; column < row.Count; column++)
                {
                    string current = row[column].Trim();
                    if (string.IsNullOrWhiteSpace(current))
                    {
                        continue;
                    }

                    if (column + 1 < row.Count && !string.IsNullOrWhiteSpace(row[column + 1]))
                    {
                        yield return new ExtractedField(id++, preview.Name, current, row[column + 1].Trim(), 0.86);
                    }
                    else if (current.Contains(':'))
                    {
                        string[] parts = current.Split(':', 2);
                        if (!string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                        {
                            yield return new ExtractedField(id++, preview.Name, parts[0].Trim(), parts[1].Trim(), 0.82);
                        }
                    }
                }
            }
        }

        foreach (Match match in Regex.Matches(preview.ParsedText, @"(?m)^\s*([A-Za-z][A-Za-z0-9\s\/\.\-]{2,40})\s*[:\-]\s*(.{1,120})$").Cast<Match>().Take(80))
        {
            yield return new ExtractedField(id++, preview.Name, match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim(), 0.74);
        }
    }

    private void RenderReadOnlyGrid(Grid grid, IReadOnlyList<IReadOnlyList<string>> rows, string label)
    {
        ClearGrid(grid);
        int rowCount = Math.Min(rows.Count, 120);
        int columns = Math.Min(rows.Select(row => row.Count).DefaultIfEmpty(1).Max(), 30);
        for (int column = 0; column < columns; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        }
        for (int row = 0; row < rowCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int column = 0; column < columns; column++)
            {
                string value = column < rows[row].Count ? rows[row][column] : string.Empty;
                var border = new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Child = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, MinWidth = 120 }
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, column);
                grid.Children.Add(border);
            }
        }
    }

    private void RenderOutputTable()
    {
        ClearGrid(OutputPreviewGrid);
        _outputCellBindings.Clear();
        int tableIndex = OutputTableSelector.SelectedIndex;
        if (tableIndex < 0 || tableIndex >= _outputTables.Count)
        {
            RenderReviewOutputTable();
            return;
        }

        List<List<string>> table = _outputTables[tableIndex];
        int columns = Math.Min(table.Select(row => row.Count).DefaultIfEmpty(1).Max(), 40);
        for (int column = 0; column < columns; column++)
        {
            OutputPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(150)));
        }
        for (int row = 0; row < table.Count; row++)
        {
            OutputPreviewGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            for (int column = 0; column < columns; column++)
            {
                while (table[row].Count <= column)
                {
                    table[row].Add(string.Empty);
                }
                var address = new CellAddress(tableIndex, row, column);
                var textBox = new TextBox
                {
                    Text = table[row][column],
                    MinWidth = 130,
                    MinHeight = 34,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Background = IsFillablePlaceholder(table[row][column])
                        ? new SolidColorBrush(Color.FromArgb(42, 37, 99, 235))
                        : Brushes.Transparent
                };
                _outputCellBindings[textBox] = address;
                textBox.GotFocus += (_, _) =>
                {
                    _selectedDestination = address;
                    OutputSelectionText.Text = $"Destination selected: table {address.TableIndex + 1}, row {address.RowIndex + 1}, column {address.ColumnIndex + 1}.";
                };
                textBox.TextChanged += (_, _) =>
                {
                    if (IsValidAddress(address))
                    {
                        _outputTables[address.TableIndex][address.RowIndex][address.ColumnIndex] = textBox.Text ?? string.Empty;
                    }
                };

                var border = new Border
                {
                    BorderBrush = Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(2),
                    Child = textBox
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, column);
                OutputPreviewGrid.Children.Add(border);
            }
        }
        RenderReviewOutputTable();
    }

    private void RenderReviewOutputTable()
    {
        ClearGrid(ReviewOutputPreviewGrid);
        int tableIndex = ReviewOutputTableSelector.SelectedIndex;
        if (tableIndex < 0 || tableIndex >= _outputTables.Count)
        {
            tableIndex = OutputTableSelector.SelectedIndex;
        }
        if (tableIndex < 0 || tableIndex >= _outputTables.Count)
        {
            return;
        }

        RenderReadOnlyGrid(ReviewOutputPreviewGrid, _outputTables[tableIndex], TableLabel(tableIndex));
    }

    private void RefreshExportPdfPreview()
    {
        if (_outputTables.Count == 0)
        {
            ExportPdfPreviewImage.Source = null;
            ExportPdfPreviewImage.IsVisible = false;
            ExportPdfPreviewMessage.Text = "Upload and fill a template to preview the generated PDF.";
            ExportPdfPreviewMessage.IsVisible = true;
            return;
        }

        string? pdfPath = null;
        try
        {
            pdfPath = NewPreviewCachePath("export-preview", ".pdf");
            _exportService.ExportPdf(BuildOutputTables(), pdfPath, _traceabilityCode);
            string imagePath = RenderPdfPreviewToPng(pdfPath, pageIndex: -1);
            using FileStream stream = File.OpenRead(imagePath);
            ExportPdfPreviewImage.Source = new Bitmap(stream);
            ExportPdfPreviewImage.IsVisible = true;
            ExportPdfPreviewMessage.IsVisible = false;
            SetStatus("PDF export preview refreshed.");
        }
        catch (Exception ex)
        {
            ExportPdfPreviewImage.Source = null;
            ExportPdfPreviewImage.IsVisible = false;
            ExportPdfPreviewMessage.Text = "Could not render the PDF export preview: " + ex.Message;
            ExportPdfPreviewMessage.IsVisible = true;
            Log("PDF export preview failed: " + ex);
        }
        finally
        {
            TryDeleteFile(pdfPath);
        }
    }

    private static void ClearGrid(Grid grid)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
    }

    private Rect UpdateSelectionRectangle(Point start, Point end)
    {
        double left = Math.Min(start.X, end.X);
        double top = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        Canvas.SetLeft(PreviewRegionRectangle, left);
        Canvas.SetTop(PreviewRegionRectangle, top);
        PreviewRegionRectangle.Width = width;
        PreviewRegionRectangle.Height = height;
        return new Rect(left, top, width, height);
    }

    private void SetPreviewZoom(double zoom)
    {
        _previewZoom = Math.Clamp(zoom, PreviewMinZoom, PreviewMaxZoom);
        ApplyPreviewView(resetSelection: true);
    }

    private void ResetPreviewView()
    {
        _previewZoom = 1.0;
        _previewRotation = 0;
        ApplyPreviewView(resetSelection: true);
    }

    private void ApplyPreviewView(bool resetSelection)
    {
        PreviewCanvas.Width = PreviewBaseWidth * _previewZoom;
        PreviewCanvas.Height = PreviewBaseHeight * _previewZoom;
        PreviewImage.Width = PreviewCanvas.Width;
        PreviewImage.Height = PreviewCanvas.Height;
        PreviewMessage.Width = Math.Max(280, PreviewCanvas.Width - 40);
        PreviewImage.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        PreviewImage.RenderTransform = null;
        RefreshPreviewImageSource();
        ZoomText.Text = $"{_previewZoom:P0} / {_previewRotation}°";

        if (resetSelection)
        {
            _regionSelectionMode = false;
            _regionStart = null;
            _selectedRegion = null;
            PreviewRegionRectangle.IsVisible = false;
            RegionSelectionText.Text = "No region selected";
        }
        else if (_selectedRegion is Rect region)
        {
            RegionSelectionText.Text = FormatRegion(region);
        }
    }

    private string FormatRegion(Rect region)
    {
        double zoom = Math.Max(_previewZoom, 0.01);
        Rect baseRegion = new(region.X / zoom, region.Y / zoom, region.Width / zoom, region.Height / zoom);
        string rotation = _previewRotation == 0 ? "" : $", rotated {_previewRotation}°";
        return $"Region X={baseRegion.X:0}, Y={baseRegion.Y:0}, W={baseRegion.Width:0}, H={baseRegion.Height:0} ({_previewZoom:P0}{rotation})";
    }

    private async Task ExportAsync(string label, string extension, Action<string> export)
    {
        if (_outputTables.Count == 0)
        {
            SetStatus("Upload and fill a template before exporting.");
            return;
        }

        string? path = await PickSavePathAsync($"intellifill-output-{_traceabilityCode}{extension}", label, extension);
        if (path is null)
        {
            return;
        }

        try
        {
            export(path);
            SetStatus($"Exported {label}: {path}");
        }
        catch (Exception ex)
        {
            SetStatus($"Export {label} failed: {ex.Message}");
            Log($"Export {label} failed: {ex}");
        }
    }

    private IReadOnlyList<OutputTable> BuildOutputTables()
    {
        return _outputTables
            .Select((rows, index) => new OutputTable(TableLabel(index), rows.Select(row => (IReadOnlyList<string>)row.ToList()).ToList()))
            .ToList();
    }

    private IReadOnlyList<RunValue> BuildRunValues()
    {
        var values = new List<RunValue>();
        for (int tableIndex = 0; tableIndex < _outputTables.Count; tableIndex++)
        {
            List<List<string>> table = _outputTables[tableIndex];
            for (int row = 0; row < table.Count; row++)
            {
                for (int column = 0; column < table[row].Count; column++)
                {
                    values.Add(new RunValue(_traceabilityCode, TableLabel(tableIndex), tableIndex, row, column, table[row][column]));
                }
            }
        }
        return values;
    }

    private void AddMapping(ExtractedField field, CellAddress address, string value)
    {
        string destinationLabel = DestinationLabel(_outputTables[address.TableIndex], address.RowIndex, address.ColumnIndex);
        _mappings.RemoveAll(mapping => mapping.TableIndex == address.TableIndex && mapping.RowIndex == address.RowIndex && mapping.ColumnIndex == address.ColumnIndex);
        _mappings.Add(new MappingSnapshot(field.Label, field.Value, address.TableIndex, address.RowIndex, address.ColumnIndex, value, destinationLabel));
    }

    private List<string> ValidateOutput()
    {
        var issues = new List<string>();
        var seenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int tableIndex = 0; tableIndex < _outputTables.Count; tableIndex++)
        {
            List<List<string>> table = _outputTables[tableIndex];
            for (int row = 0; row < table.Count; row++)
            {
                for (int column = 0; column < table[row].Count; column++)
                {
                    string value = table[row][column].Trim();
                    string label = DestinationLabel(table, row, column);
                    if (IsFillablePlaceholder(value))
                    {
                        issues.Add($"Required/blank warning: {TableLabel(tableIndex)} row {row + 1}, column {column + 1} ({label}) is empty.");
                    }
                    if (label.Contains("gst", StringComparison.OrdinalIgnoreCase) && value.Length > 0 && !Regex.IsMatch(value, @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.IgnoreCase))
                    {
                        issues.Add($"GST/GSTIN warning: {label} value '{value}' does not look valid.");
                    }
                    if (label.Contains("date", StringComparison.OrdinalIgnoreCase) && value.Length > 0 && !DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                    {
                        issues.Add($"Date warning: {label} value '{value}' could not be parsed as a date.");
                    }
                    if ((label.Contains("amount", StringComparison.OrdinalIgnoreCase) || label.Contains("total", StringComparison.OrdinalIgnoreCase)) && value.Length > 0 && !TryParseAmount(value, out _))
                    {
                        issues.Add($"Amount warning: {label} value '{value}' is not a recognizable number.");
                    }
                    if (value.Length > 3 && seenValues.TryGetValue(value, out string? firstLabel))
                    {
                        issues.Add($"Duplicate warning: '{value}' appears in both {firstLabel} and {label}.");
                    }
                    else if (value.Length > 3)
                    {
                        seenValues[value] = label;
                    }
                }
            }
        }
        return issues;
    }

    private async Task<string?> PickSingleDocumentAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = DocumentFileTypes()
        });
        return files.FirstOrDefault()?.Path.LocalPath;
    }

    private async Task<IReadOnlyList<string>> PickManyDocumentsAsync(string title)
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = true,
            FileTypeFilter = DocumentFileTypes()
        });
        return files.Select(file => file.Path.LocalPath).Where(path => !string.IsNullOrWhiteSpace(path)).ToList();
    }

    private async Task<string?> PickSavePathAsync(string suggestedName, string label, string extension)
    {
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save " + label,
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { new FilePickerFileType(label) { Patterns = new[] { "*" + extension } } }
        });
        return file?.Path.LocalPath;
    }

    private static IReadOnlyList<FilePickerFileType> DocumentFileTypes()
    {
        return new[]
        {
            new FilePickerFileType("Documents")
            {
                Patterns = new[] { "*.csv", "*.txt", "*.xlsx", "*.xls", "*.docx", "*.pdf", "*.png", "*.jpg", "*.jpeg" }
            },
            FilePickerFileTypes.All
        };
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsPath), JsonOptions());
                if (settings is not null)
                {
                    settings.DatabasePath = string.IsNullOrWhiteSpace(settings.DatabasePath) ? DefaultDatabasePath() : Environment.ExpandEnvironmentVariables(settings.DatabasePath);
                    return settings;
                }
            }
        }
        catch
        {
            // Use defaults when settings are invalid.
        }
        return new AppSettings { DatabasePath = DefaultDatabasePath() };
    }

    private void SaveSettings()
    {
        Directory.CreateDirectory(_appDataPath);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, JsonOptions()));
    }

    private void ApplySettingsToUi()
    {
        TesseractPathBox.Text = _settings.TesseractPath;
        DatabasePathBox.Text = _settings.DatabasePath;
        ThemeComboBox.SelectedIndex = _settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = _settings.Theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
        bool isDark = _settings.Theme == "Dark" ||
                      (_settings.Theme == "Default" && Application.Current.ActualThemeVariant == ThemeVariant.Dark);
        ApplyThemePalette(isDark);
    }

    private void ApplyThemePalette(bool isDark)
    {
        if (isDark)
        {
            SetBrush("AppBackgroundBrush", "#000000");
            SetBrush("ShellBrush", "#070707");
            SetBrush("ShellRailBrush", "#0D0D0E");
            SetBrush("ShellCardBrush", "#121214");
            SetBrush("ShellBorderBrush", "#2A2A2D");
            SetBrush("PanelBrush", "#080808");
            SetBrush("SoftPanelBrush", "#111113");
            SetBrush("PreviewPanelBrush", "#0D0D0F");
            SetBrush("PanelBorderBrush", "#2E2E32");
            SetBrush("TitleTextBrush", "#FAFAFA");
            SetBrush("BodyTextBrush", "#EDEDED");
            SetBrush("MutedTextBrush", "#A8A8A8");
            SetBrush("ShellTitleTextBrush", "#FAFAFA");
            SetBrush("ShellBodyTextBrush", "#EDEDED");
            SetBrush("ShellMutedTextBrush", "#A8A8A8");
            SetBrush("PrimaryBrush", "#22D3EE");
            SetBrush("TealBrush", "#2DD4BF");
            SetBrush("TealTextBrush", "#031417");
            SetBrush("RailButtonBrush", "#18181B");
            SetBrush("RailButtonTextBrush", "#F4F4F5");
            SetBrush("RailButtonBorderBrush", "#333337");
            SetBrush("PreviewCanvasBrush", "#050505");
            SetBrush("SelectionStrokeBrush", "#22D3EE");
            SetBrush("SelectionFillBrush", "#3322D3EE");
            return;
        }

        SetBrush("AppBackgroundBrush", "#EAF2FF");
        SetBrush("ShellBrush", "#FBFCFF");
        SetBrush("ShellRailBrush", "#EEF4FF");
        SetBrush("ShellCardBrush", "#F2F5FE");
        SetBrush("ShellBorderBrush", "#DDE5F5");
        SetBrush("PanelBrush", "#FBFCFF");
        SetBrush("SoftPanelBrush", "#F2F5FE");
        SetBrush("PreviewPanelBrush", "#F8FAFF");
        SetBrush("PanelBorderBrush", "#DDE5F5");
        SetBrush("TitleTextBrush", "#282B36");
        SetBrush("BodyTextBrush", "#2B2E3A");
        SetBrush("MutedTextBrush", "#687083");
        SetBrush("ShellTitleTextBrush", "#282B36");
        SetBrush("ShellBodyTextBrush", "#2B2E3A");
        SetBrush("ShellMutedTextBrush", "#687083");
        SetBrush("PrimaryBrush", "#2563EB");
        SetBrush("TealBrush", "#14B8A6");
        SetBrush("TealTextBrush", "#071318");
        SetBrush("RailButtonBrush", "#E8EEF9");
        SetBrush("RailButtonTextBrush", "#263044");
        SetBrush("RailButtonBorderBrush", "#CDD8EC");
        SetBrush("PreviewCanvasBrush", "#EEF3FF");
        SetBrush("SelectionStrokeBrush", "#2563EB");
        SetBrush("SelectionFillBrush", "#332563EB");
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush(Color.Parse(color));
    }

    private string PackageStatus()
    {
        string tesseract = string.IsNullOrWhiteSpace(_settings.TesseractPath) ? "Not selected" : _settings.TesseractPath;
        return $"Version: {AppVersion}{Environment.NewLine}App folder: {AppContext.BaseDirectory}{Environment.NewLine}App data: {_appDataPath}{Environment.NewLine}Tesseract: {tesseract}{Environment.NewLine}SQLite: {_settings.DatabasePath}";
    }

    private string DefaultDatabasePath() => System.IO.Path.Combine(_appDataPath, "intellifill.sqlite3");

    private void SetStatus(string message)
    {
        StatusText.Text = message + Environment.NewLine + Environment.NewLine + PackageStatus();
        Log(message);
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not stop the UI.
        }
    }

    private async Task ShowMessageAsync(string title, string text)
    {
        Button closeButton = CreateDialogButton("Close", isPrimary: true, minWidth: 110);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { closeButton }
        };

        (double width, double height) = DialogSizeFor(text);
        Window box = CreateStyledDialog(title, width, height, CreateDialogTextPanel(text), buttons);
        closeButton.Click += (_, _) => box.Close();

        await box.ShowDialog(this);
    }

    private async Task ShowAboutAsync()
    {
        Button closeButton = CreateDialogButton("Close", isPrimary: true, minWidth: 110);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { closeButton }
        };

        Control banner = CreateAboutBanner();
        var body = new Border
        {
            Background = DialogBrush("PreviewPanelBrush"),
            BorderBrush = DialogBrush("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(18),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    banner,
                    new TextBlock
                    {
                        Text = $"IntelliFill OCR {AppVersion}",
                        FontSize = 22,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = DialogBrush("TitleTextBrush")
                    },
                    new TextBlock
                    {
                        Text = "Avalonia desktop edition",
                        FontSize = 14,
                        Foreground = DialogBrush("MutedTextBrush")
                    },
                    new TextBlock
                    {
                        Text = "Offline OCR, document extraction, table filling, SQLite storage, and traceable exports.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = DialogBrush("BodyTextBrush"),
                        LineHeight = 22
                    }
                }
            }
        };

        Window box = CreateStyledDialog("About IntelliFill OCR", 740, 540, body, buttons);
        closeButton.Click += (_, _) => box.Close();
        await box.ShowDialog(this);
    }

    private Control CreateAboutBanner()
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri("avares://IntelliFillOCR/Assets/logo.png"));
            return new Image
            {
                Source = new Bitmap(stream),
                Stretch = Stretch.Uniform,
                MaxHeight = 220,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }
        catch (Exception ex)
        {
            Log("About banner failed to load: " + ex.Message);
            return new TextBlock
            {
                Text = "OCR AUTOFILL",
                FontSize = 28,
                FontWeight = FontWeight.SemiBold,
                Foreground = DialogBrush("TitleTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }
    }

    private Window CreateProgressDialog(string title, string text)
    {
        var body = new Border
        {
            Background = DialogBrush("PreviewPanelBrush"),
            BorderBrush = DialogBrush("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16),
            Child = new StackPanel
            {
                Spacing = 14,
                Children =
                {
                    new TextBlock
                    {
                        Text = text,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = DialogBrush("BodyTextBrush")
                    },
                    new ProgressBar
                    {
                        IsIndeterminate = true,
                        Height = 8,
                        Foreground = DialogBrush("PrimaryBrush"),
                        Background = DialogBrush("RailButtonBrush")
                    }
                }
            }
        };

        return CreateStyledDialog(title, 460, 220, body, new StackPanel());
    }

    private static (double Width, double Height) DialogSizeFor(string text)
    {
        int lines = text.Count(character => character == '\n') + 1;
        if (text.Length > 1600 || lines > 24)
        {
            return (760, 560);
        }
        if (text.Length > 700 || lines > 10)
        {
            return (640, 430);
        }
        return (520, 300);
    }

    private Window CreateStyledDialog(string title, double width, double height, Control body, Control buttons)
    {
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Foreground = DialogBrush("TitleTextBrush"),
            TextWrapping = TextWrapping.Wrap
        };

        var contentGrid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 14,
            Children =
            {
                titleText,
                body,
                buttons
            }
        };
        Grid.SetRow(body, 1);
        Grid.SetRow(buttons, 2);

        return new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(520, width),
            MinHeight = Math.Min(320, height),
            Icon = Icon,
            FontFamily = FontFamily,
            Background = DialogBrush("AppBackgroundBrush"),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new Border
            {
                Background = DialogBrush("AppBackgroundBrush"),
                Padding = new Thickness(16),
                Child = new Border
                {
                    Background = DialogBrush("PanelBrush"),
                    BorderBrush = DialogBrush("PanelBorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(18),
                    Padding = new Thickness(18),
                    Child = contentGrid
                }
            }
        };
    }

    private Border CreateDialogTextPanel(string text)
    {
        return new Border
        {
            Background = DialogBrush("PreviewPanelBrush"),
            BorderBrush = DialogBrush("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(14),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = DialogBrush("BodyTextBrush"),
                    LineHeight = 22
                }
            }
        };
    }

    private Button CreateDialogButton(string text, bool isPrimary, double minWidth)
    {
        return new Button
        {
            Content = text,
            MinWidth = minWidth,
            MinHeight = 38,
            Padding = new Thickness(14, 8),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = isPrimary ? DialogBrush("PrimaryBrush") : DialogBrush("RailButtonBrush"),
            Foreground = isPrimary ? Brushes.White : DialogBrush("RailButtonTextBrush"),
            BorderBrush = isPrimary ? DialogBrush("PrimaryBrush") : DialogBrush("RailButtonBorderBrush"),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeight.SemiBold
        };
    }

    private IBrush DialogBrush(string key)
    {
        return Resources[key] is IBrush brush ? brush : Brushes.Transparent;
    }

    private string TableLabel(int index)
    {
        return index >= 0 && index < _templateTables.Count ? _templateTables[index].Label : $"Table {index + 1}";
    }

    private bool IsValidAddress(CellAddress address)
    {
        return address.TableIndex >= 0 &&
               address.TableIndex < _outputTables.Count &&
               address.RowIndex >= 0 &&
               address.RowIndex < _outputTables[address.TableIndex].Count &&
               address.ColumnIndex >= 0 &&
               address.ColumnIndex < _outputTables[address.TableIndex][address.RowIndex].Count;
    }

    private static bool IsFillablePlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string trimmed = value.Trim();
        return trimmed.Contains("___", StringComparison.Ordinal) ||
               Regex.IsMatch(trimmed, @"^\[.*\]$") ||
               Regex.IsMatch(trimmed, @"^\{\{.*\}\}$") ||
               Regex.IsMatch(trimmed, @"^<.*>$");
    }

    private static string DestinationLabel(List<List<string>> table, int row, int column)
    {
        for (int left = column - 1; left >= 0; left--)
        {
            if (!string.IsNullOrWhiteSpace(table[row][left]) && !IsFillablePlaceholder(table[row][left]))
            {
                return table[row][left].Trim();
            }
        }
        for (int up = row - 1; up >= 0; up--)
        {
            if (column < table[up].Count && !string.IsNullOrWhiteSpace(table[up][column]) && !IsFillablePlaceholder(table[up][column]))
            {
                return table[up][column].Trim();
            }
        }
        return $"Row {row + 1} Column {column + 1}";
    }

    private static double Similarity(string left, string right)
    {
        string a = Normalize(left);
        string b = Normalize(right);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }
        if (a == b)
        {
            return 1;
        }
        var leftTokens = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var rightTokens = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        int intersection = leftTokens.Intersect(rightTokens).Count();
        int union = leftTokens.Union(rightTokens).Count();
        double tokenScore = union == 0 ? 0 : (double)intersection / union;
        int distance = Levenshtein(a, b);
        double editScore = 1.0 - (double)distance / Math.Max(a.Length, b.Length);
        return Math.Max(tokenScore, editScore);
    }

    private static string Normalize(string value)
    {
        return Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
    }

    private static int Levenshtein(string a, string b)
    {
        var costs = new int[b.Length + 1];
        for (int j = 0; j < costs.Length; j++)
        {
            costs[j] = j;
        }
        for (int i = 1; i <= a.Length; i++)
        {
            costs[0] = i;
            int previous = i - 1;
            for (int j = 1; j <= b.Length; j++)
            {
                int current = costs[j];
                costs[j] = a[i - 1] == b[j - 1] ? previous : Math.Min(Math.Min(costs[j - 1], costs[j]), previous) + 1;
                previous = current;
            }
        }
        return costs[b.Length];
    }

    private static bool TryParseAmount(string value, out decimal amount)
    {
        string cleaned = Regex.Replace(value, @"[^\d\.\-]", "");
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) ||
               decimal.TryParse(value, NumberStyles.Currency, CultureInfo.CurrentCulture, out amount);
    }

    private static int NormalizeRotation(int angle)
    {
        int normalized = angle % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static bool IsNewerVersion(string candidate, string current)
    {
        int[] left = VersionParts(candidate);
        int[] right = VersionParts(current);
        for (int index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            int leftPart = index < left.Length ? left[index] : 0;
            int rightPart = index < right.Length ? right[index] : 0;
            if (leftPart != rightPart)
            {
                return leftPart > rightPart;
            }
        }
        return false;
    }

    private static int[] VersionParts(string version)
    {
        return version
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(Regex.Replace(part, @"[^\d]", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : 0)
            .ToArray();
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static string CreateTraceabilityCode() => "IF" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string ChangelogText()
    {
        return """
        IntelliFill OCR Changelog

        Version 3.7.3
        - Reworked dark mode from blue-slate to a neutral black/charcoal theme.
        - Modernized panels, buttons, tabs, text boxes, combo boxes, and list surfaces for a more consistent UI.
        - Improved dark-mode contrast with high-clarity text, gray borders, and cyan/teal accents.
        - Kept the existing document preview, OCR selection, mapping, export preview, and Settings workflows intact.

        Version 3.7.2
        - Added visual preview and zoom/rotate support across uploaded formats, including generated CSV/Excel table previews.
        - Kept OCR region selection available for PDF, image, Word, and text-style previews while disabling it for CSV/Excel table files.
        - Added a default in-app PDF export preview on Review that renders the final page so the barcode is visible.
        - Improved PDF barcode contrast with black bars, a white quiet zone, and clearer footer placement.
        - Added a Settings button to auto-detect and save the local Tesseract OCR executable path.

        Version 3.7.1
        - Fixed update installs that could appear aborted after the app was already updated.
        - Moved downloaded update-package cleanup out of the NSIS installer and into the detached updater handoff.
        - Added retry logging when antivirus or Windows temporarily locks the downloaded installer package.
        - Preserved update-launch.log so cleanup issues can be diagnosed without affecting setup.

        Version 3.7.0
        - Added the new OCR AutoFill brand logo to the app, installer icon, and GitHub README.
        - Updated README branding to use the wide OCR AutoFill banner.
        - Added the wide OCR AutoFill banner to the in-app About dialog.
        - Rebuilt the Windows application icon from the new document/OCR logo.

        Version 3.6.1
        - Improved image/PDF preview clarity with a larger high-resolution visual canvas.
        - Added a final output preview to Review so users can inspect filled tables before saving/exporting.
        - Improved PDF export with multi-page table rendering, borders, wrapped cell text, page numbers, and one bottom traceability barcode.
        - Improved Word and Excel export formatting with clearer headings, table borders, spacing, and first-row styling.
        - Added an indeterminate loading popup while Check for Updates is running.
        - Short popup messages now open in smaller styled windows instead of oversized dialogs.

        Version 3.6.0
        - Removed the duplicated left sidebar and moved the app name/version into the top header.
        - Traceability and output/export actions now live in Review.
        - Tools, logs, update checks, help, changelog, and live status now live in Settings.
        - Image and PDF files now render as real visual previews instead of placeholder text.
        - OCR region selection crops the selected preview area and runs local Tesseract OCR when available.
        - Added automatic Tesseract detection from common Windows install locations and PATH.
        - Removed legacy WinUI and PySide/Qt files from the repository.

        Version 3.5.5
        - Fixed automatic updater launch by closing the downloaded installer file before setup starts.
        - Added a detached update handoff script so the installer launches only after IntelliFill OCR exits.
        - Writes update-launch.log in the local Updates folder to diagnose download or launch failures.
        - Fixed optional Tesseract OCR download in the Windows installer by using PowerShell TLS 1.2 instead of NSISdl.

        Version 3.5.4
        - Made every main page scroll-safe: Template, Sources, Mapping, Review, and Settings.
        - Removed remaining fixed-height page layouts that could hide bottom controls on smaller windows or high display scaling.
        - Added View Changelog buttons in the sidebar Tools section and Settings Maintenance section.
        - Restyled popup windows so help, logs, changelog, about, validation, and update prompts match the main app UI.

        Version 3.5.3
        - Fixed clipped buttons and hidden elements by making Sources and Settings use safer stacked layouts.
        - Improved image/PDF preview sizing so zoom, rotate, region selection, detected tables, and parsed text stay reachable.
        - Fixed automatic update downloads to use the app update cache, launch the installer reliably, and report progress.
        - Added installer metadata, release checksums, and update-package cleanup after install.

        Version 3.5.2
        - Removed duplicate top/side workflow actions.
        - Fixed light mode sidebar palette.
        - Replaced the old tab strip with rounded page-switcher buttons.
        - Improved responsive sizing for sidebar, settings, source preview, mapping, and review layouts.

        Version 3.5.1
        - Polished light/dark mode consistency in the Avalonia UI.
        - Improved rounded panel styling and page button states.

        Version 3.5.0
        - Redesigned the app with the Avalonia showcase-inspired layout.
        - Added scroll wheel support to sidebar and pages.
        - Improved source preview organization and update notifications.
        - Continued NSIS installer distribution.

        Version 3.4.0
        - Migrated the packaged desktop app to Avalonia.
        - Added NSIS installer support and removed portable EXE packaging.
        - Added a separate Settings page and improved Windows icon registration.

        Version 3.3.2
        - Cleaned repository workflows and release packaging.
        - Reduced duplicate GitHub release assets.

        Version 3.3.1
        - Added single-package release handling and update/install support for the package.

        Version 3.3.0
        - Reworked the Windows application packaging direction after the WinUI migration attempt.
        - Improved document preview and selection workflow planning.

        Version 3.2.0
        - Added MSIX workflow support experiments for the WinUI shell.
        - Improved release packaging workflow structure.

        Version 3.1.1
        - Fixed update installs where old shortcuts could point to a missing executable after the WinUI shell became default.
        - The native WinUI shell launched as the top-level IntelliFillOCR.exe application.
        - Included IntelliFillOCR.WinUI.exe as a compatibility launcher for v3.1.0 shortcuts.
        - Bundled a Qt-free Python backend IPC process in the Windows package.

        Version 3.1.0
        - Made the Windows installer and default Windows package open the native WinUI 3 shell.
        - Ran the Python OCR engine as a local JSON IPC backend for the WinUI shell.
        - Added native WinUI template upload with detected table preview.
        - Removed the old Qt workspace launch path from the WinUI user experience.

        Version 3.0.1
        - Fixed update installers that could still show the old 2.4.2 application version after install.
        - Release builds stamp the app version from the GitHub release tag before packaging.
        - The updater downloads only the installer asset that matches the latest release version.

        Version 3.0.0
        - Added the first native WinUI 3 shell package for IntelliFill OCR.
        - Added a GitHub workflow that builds the WinUI shell, PyInstaller backend, and combined WinUI package.
        - Published Windows, WinUI, Debian, and Fedora package assets from the release pipeline.

        Version 2.4.2
        - Added a scrolling installation details output window below the Windows setup progress bar.
        - App file copying, optional Tesseract setup preparation, and final metadata steps are shown during install.
        - Repeated progress events are de-duplicated so setup output remains readable.

        Version 2.4.1
        - Fixed the Windows uninstaller runtime proc error.
        - Removed fragile custom uninstall progress label updates while keeping stable uninstall logging.

        Version 2.4.0
        - Major installer upgrade with Full, Minimal, and Custom setup types.
        - Windows setup shows the current operation during install and uninstall.
        - If Tesseract OCR is missing, setup can optionally download and launch the Tesseract OCR 5.5.0 installer.
        - Installer metadata includes registry entries and install mode details.
        - Update checks and package downloads use a 180-second network timeout.

        Version 2.3.2
        - User Guide and Feature Help workflow diagrams are readable in dark mode.
        - Screenshot-style maps, flowcharts, warning boxes, and help panels use theme-aware colors.

        Version 2.3.1
        - Dock panels use Qt6 native close and float controls.
        - Removed custom panel glyphs that could be hard to read.
        - Actions > Panels restores closed Uploaded Files, Extracted Fields, and Output Preview panels.

        Version 2.3.0
        - Template documents with two or more tables load every table into Output Preview.
        - Table selectors allow filling Table 1, Table 2, Table 3, and later tables.
        - Manual mappings, intelligent matching, learned templates, validation, SQLite storage, and exports remember destination table number.
        - CSV, Excel, Word, PDF, and preserved-layout exports include all template tables in one output document.
        - GitHub Actions can build Linux Debian and Fedora packages.
        - Linux update checks can download .deb or .rpm packages and show terminal install guidance.
        - Ubuntu, Debian, and Fedora builds automatically detect local Tesseract from PATH and common install locations.

        Version 2.2.4
        - Added smoother wheel scrolling for tables, logs, parsed text, help, database preview, and changelog pages.
        - Polished scrollbar styling in light and dark mode.
        - Expanded the in-app User Guide.

        Version 2.2.3
        - PDF traceability barcodes render as clear bottom-center barcode images instead of collapsed black strips.
        - Barcode exports keep a white quiet zone and wider modules so the ID remains scannable.

        Version 2.2.2.1
        - Dock panel close and float controls use custom high-contrast buttons in dark and light mode.
        - Removed reliance on native glyphs that could disappear against panel chrome.

        Version 2.2.2
        - Large help, database, log, validation, detection, and learned-template windows open inside the visible screen area.
        - Dock panel close/float buttons are visible in light mode.

        Version 2.2.1
        - Added a full offline User Guide under Actions > Help.
        - Made the in-app changelog scrollable and complete from v1.0.0 onward.
        - Changed GitHub release notes so release pages show only the latest version notes.

        Version 2.2.0
        - Template Learning saves reusable mappings, detects similar documents later, and applies them with confidence scores.
        - Validation warns about required blanks, GST/GSTIN format, dates, amounts, duplicate IDs, and invoice total mismatches.
        - Signature and stamp detection helps review approvals while preserved exports keep original marks intact.
        - Windows scanner import can acquire source images from local WIA scanner drivers.

        Version 2.1.0
        - Actions > Panels can show, hide, or restore Uploaded Files, Extracted Fields, and Output Preview.
        - Closing those panels is no longer a dead end.

        Version 2.0.1
        - Windows taskbar pins use the new icon when pinned from the updated shortcut.
        - Installer shortcuts use the same app identity as the running application.

        Version 2.0.0
        - New AutoFill & Export logo is used in the app, package, installer, and README.
        - The old top toolbar was replaced by one Actions button with every workflow option.
        - Fresh installs and updates show the changelog automatically on first launch.
        - Traceability IDs are shorter, scannable, and printed once at the bottom center of PDF/Word exports.
        - Preserved-layout exports fill blank/template fields only and keep headings, logos, tables, and signature areas.
        - Light mode visibility is improved for the mapping workflow.

        Version 1.1.1
        - Installer guidance explains that Tesseract OCR must be installed locally and SQLite storage is local/offline.
        - Added SQLite database preview and application log viewer.
        - Added About/What's New release page with installed version number.
        - Added Check for Updates with download-and-launch installer flow.

        Version 1.1.0
        - Added Windows installer support and release packaging for the standalone EXE.
        - Added GitHub release pipeline assets for setup installer distribution.

        Version 1.0.0
        - Initial offline OCR desktop app with PySide6 GUI, Tesseract OCR, OpenCV preprocessing, document parsing, and SQLite storage.
        - Supported template/source upload for Word, Excel, CSV, images, and PDF where available.
        - Added visual OCR region selection, extracted field mapping, editable output table preview, and export to CSV, Excel, Word, and PDF.
        - Added traceability code storage, mapping configurations, uploaded file metadata, and core packaging scripts.
        """;
    }

    private static string HelpText()
    {
        return """
        IntelliFill OCR Avalonia User Guide

        1. Upload Template
        Use Upload Template. CSV, TXT, XLSX, DOCX, PDF, PNG, JPG, and JPEG files are accepted. Every detected table appears in the template and output table selectors.

        2. Upload Sources
        Use Upload Sources. Add up to five documents. The selected document visual preview, parsed text, and detected tables appear in Sources.

        3. OCR Region Selection
        Select a PDF, image, Word, or text-style document, choose Select Region, then drag a rectangle on the preview. Use zoom, reset, and rotate before selecting if the document is hard to read. CSV and Excel files show a zoomable/rotatable table preview, but region OCR is disabled because those formats already provide native table data.

        4. Manual Mapping
        Select an extracted field, click a destination output cell, then choose Map Selected. Output cells remain editable before database save/export.

        5. Auto Fill
        Auto Fill compares blank template cells with extracted field labels using fuzzy matching and fills confident matches.

        6. Validation
        Validate warns about blank fields, duplicate values, amount/date issues, and GST/GSTIN-like fields.

        7. SQLite
        Save SQLite stores the traceability code, source metadata, extracted values, mappings, and timestamps.

        8. Exports
        Export CSV, Excel, Word, or PDF. Review shows a default in-app PDF export preview, and PDF includes one high-contrast traceability barcode/code at the bottom center of the final page.

        9. Settings
        Open the Settings page to auto-detect or browse for the Tesseract executable, choose the SQLite database path, theme, logs, updates, and database preview tools.
        """;
    }

    private sealed record CellAddress(int TableIndex, int RowIndex, int ColumnIndex);

    private sealed record DocumentItem(string Kind, DocumentPreview Preview);

    private sealed record ExtractedField(int Id, string SourceName, string Label, string Value, double Confidence);

    private sealed record MatchCandidate(ExtractedField Field, double Score);

    private sealed record MappingSnapshot(string SourceLabel, string SourceValue, int TableIndex, int RowIndex, int ColumnIndex, string Value, string DestinationLabel);

    private sealed record ReleaseUpdate(string Version, string Tag, string ReleaseUrl, string AssetName, string DownloadUrl, string Notes);

    private sealed class AppSettings
    {
        public string TesseractPath { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public string Theme { get; set; } = "Default";
    }
}
