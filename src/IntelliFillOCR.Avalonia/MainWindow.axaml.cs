using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
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
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using IntelliFillOCR.Core;

namespace IntelliFillOCR.Avalonia;

public sealed partial class MainWindow : Window
{
    private const string AppVersion = "3.5.5";
    private const double PreviewBaseWidth = 780;
    private const double PreviewBaseHeight = 500;
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
        ApplySettingsToUi();
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

        string extension = System.IO.Path.GetExtension(_selectedDocumentPreview.Path).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".pdf")
        {
            SetStatus("Region selection is available for images and PDFs. For documents and spreadsheets, use parsed text or detected tables.");
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
        await ShowMessageAsync("About IntelliFill OCR", $"IntelliFill OCR {AppVersion}{Environment.NewLine}Avalonia desktop edition{Environment.NewLine}{Environment.NewLine}Offline OCR, document extraction, table filling, SQLite storage, and traceable exports.");
    }

    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            ReleaseUpdate latest = await GetLatestReleaseAsync();
            if (!IsNewerVersion(latest.Version, AppVersion))
            {
                await ShowMessageAsync("Check for Updates", $"You are on the latest version ({AppVersion}).");
                return;
            }

            await PromptForUpdateAsync(latest, isStartupNotice: false);
        }
        catch (Exception ex)
        {
            await ShowMessageAsync("Check for Updates", $"Could not check GitHub releases. Offline use is still supported.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
    }

    private void ApplyTheme_Click(object? sender, RoutedEventArgs e)
    {
        _settings.TesseractPath = TesseractPathBox.Text ?? string.Empty;
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

    private void ShowReviewPage_Click(object? sender, RoutedEventArgs e) =>
        ShowPage(ReviewPage, ReviewPageButton);

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
        RenderOutputTable();
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

    private void PreviewCanvas_PointerReleased(object? sender, PointerReleasedEventArgs e)
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
        AddSelectedRegionField(_selectedRegion.Value);
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
                candidates.Add(new ReleaseUpdate(version, tag, releaseUrl, name, downloadUrl));
            }
        }

        ReleaseUpdate? versionMatched = candidates.FirstOrDefault(candidate => AssetMatchesVersion(candidate.AssetName, version));
        return versionMatched ?? candidates.FirstOrDefault() ?? new ReleaseUpdate(version, tag, releaseUrl, string.Empty, string.Empty);
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
        string body = $"IntelliFill OCR {latest.Version} is available.{Environment.NewLine}Current version: {AppVersion}{Environment.NewLine}{Environment.NewLine}{installText}";
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
echo Launching installer.>>"%LOG%"
start "IntelliFill OCR Setup" /wait "%INSTALLER%"
set "INSTALL_EXIT=%ERRORLEVEL%"
echo Installer finished with exit code %INSTALL_EXIT%.>>"%LOG%"
del "%INSTALLER%" >>"%LOG%" 2>&1
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

        Window box = CreateStyledDialog(title, 600, 340, body, buttons);
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
        for (int index = 0; index < _outputTables.Count; index++)
        {
            OutputTableSelector.Items.Add(TableLabel(index));
        }
        OutputTableSelector.SelectedIndex = 0;
        RenderOutputTable();
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
        PreviewImage.IsVisible = false;
        PreviewMessage.IsVisible = true;
        PreviewMessage.Text = "Preview will appear here for images. PDF files can still be region-marked and table/parsed text is shown on the right.";

        try
        {
            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                using FileStream stream = File.OpenRead(preview.Path);
                PreviewImage.Source = new Bitmap(stream);
                PreviewImage.IsVisible = true;
                PreviewMessage.IsVisible = false;
                RegionSelectionText.Text = "Use Select Region to draw OCR area.";
                return;
            }

            if (extension == ".pdf")
            {
                PreviewMessage.Text = "PDF selected. Use Select Region for OCR window capture, or Open Original to inspect the PDF in the system viewer.";
                RegionSelectionText.Text = "Use Select Region to draw PDF area.";
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"Preview render failed for {preview.Path}: {ex}");
        }

        RegionSelectionText.Text = "Table/text document.";
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

    private void AddSelectedRegionField(Rect region)
    {
        if (_selectedDocumentPreview is null)
        {
            return;
        }

        string label = $"Region from {System.IO.Path.GetFileNameWithoutExtension(_selectedDocumentPreview.Path)}";
        string value = FormatRegion(region);
        AddExtractedField(new ExtractedField(_extractedFields.Count + 1, _selectedDocumentPreview.Name, label, value, 0.80));
        SetStatus("Region field added. Select an output cell and choose Map Selected.");
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
        PreviewImage.RenderTransform = new RotateTransform(_previewRotation);
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
            SetBrush("AppBackgroundBrush", "#0B1020");
            SetBrush("ShellBrush", "#070A18");
            SetBrush("ShellRailBrush", "#0D1122");
            SetBrush("ShellCardBrush", "#151A2B");
            SetBrush("ShellBorderBrush", "#29314A");
            SetBrush("PanelBrush", "#111827");
            SetBrush("SoftPanelBrush", "#182033");
            SetBrush("PreviewPanelBrush", "#101827");
            SetBrush("PanelBorderBrush", "#2B3550");
            SetBrush("TitleTextBrush", "#F7F9FF");
            SetBrush("BodyTextBrush", "#E5EAF6");
            SetBrush("MutedTextBrush", "#A6B1C5");
            SetBrush("ShellTitleTextBrush", "#F7F9FF");
            SetBrush("ShellBodyTextBrush", "#D7DBEF");
            SetBrush("ShellMutedTextBrush", "#AAB0CF");
            SetBrush("PrimaryBrush", "#60A5FA");
            SetBrush("TealBrush", "#2DD4BF");
            SetBrush("TealTextBrush", "#031417");
            SetBrush("RailButtonBrush", "#222A44");
            SetBrush("RailButtonTextBrush", "#F2F5FF");
            SetBrush("RailButtonBorderBrush", "#3A4565");
            SetBrush("PreviewCanvasBrush", "#0B1220");
            SetBrush("SelectionStrokeBrush", "#60A5FA");
            SetBrush("SelectionFillBrush", "#3360A5FA");
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

        Window box = CreateStyledDialog(title, 760, 560, CreateDialogTextPanel(text), buttons);
        closeButton.Click += (_, _) => box.Close();

        await box.ShowDialog(this);
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
        Use Upload Sources. Add up to five documents. The selected document preview, parsed text, and detected tables appear in Uploaded Documents.

        3. OCR Region Selection
        Select an image or PDF, choose Select OCR Region, then drag a rectangle on the preview. Use zoom and rotate before selecting if the document is hard to read.

        4. Manual Mapping
        Select an extracted field, click a destination output cell, then choose Map Selected. Output cells remain editable before database save/export.

        5. Auto Fill
        Auto Fill compares blank template cells with extracted field labels using fuzzy matching and fills confident matches.

        6. Validation
        Validate warns about blank fields, duplicate values, amount/date issues, and GST/GSTIN-like fields.

        7. SQLite
        Save SQLite stores the traceability code, source metadata, extracted values, mappings, and timestamps.

        8. Exports
        Export CSV, Excel, Word, or PDF. PDF includes one traceability barcode/code at the bottom center.

        9. Settings
        Open the Settings page to choose the Tesseract executable, SQLite database path, theme, logs, updates, and database preview tools.
        """;
    }

    private sealed record CellAddress(int TableIndex, int RowIndex, int ColumnIndex);

    private sealed record DocumentItem(string Kind, DocumentPreview Preview);

    private sealed record ExtractedField(int Id, string SourceName, string Label, string Value, double Confidence);

    private sealed record MatchCandidate(ExtractedField Field, double Score);

    private sealed record MappingSnapshot(string SourceLabel, string SourceValue, int TableIndex, int RowIndex, int ColumnIndex, string Value, string DestinationLabel);

    private sealed record ReleaseUpdate(string Version, string Tag, string ReleaseUrl, string AssetName, string DownloadUrl);

    private sealed class AppSettings
    {
        public string TesseractPath { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public string Theme { get; set; } = "Default";
    }
}
