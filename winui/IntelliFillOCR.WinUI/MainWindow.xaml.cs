using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelliFillOCR.WinUI.Services;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace IntelliFillOCR.WinUI;

public sealed partial class MainWindow : Window
{
    private const string AppVersion = "3.5.4";
    private const double PreviewBaseWidth = 760;
    private const double PreviewBaseHeight = 430;
    private const double PreviewMinZoom = 0.5;
    private const double PreviewMaxZoom = 3.0;
    private const double PreviewZoomStep = 0.25;

    private readonly NativeTemplateLoader _loader = new();
    private readonly NativeExportService _exportService = new();
    private readonly NativeDatabaseService _databaseService = new();
    private readonly List<NativeDocumentTable> _templateTables = new();
    private readonly List<DocumentItem> _uploadedDocuments = new();
    private readonly List<NativeDocumentPreview> _sourcePreviews = new();
    private readonly List<ExtractedField> _extractedFields = new();
    private readonly List<List<List<string>>> _outputTables = new();
    private readonly List<MappingSnapshot> _mappings = new();
    private readonly Dictionary<TextBox, CellAddress> _outputCellBindings = new();
    private readonly string _appDataPath;
    private readonly string _settingsPath;
    private readonly string _logPath;
    private readonly string _learnedTemplatesPath;

    private NativeDocumentPreview? _templatePreview;
    private NativeDocumentPreview? _selectedDocumentPreview;
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
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelliFillOCR");
        _settingsPath = Path.Combine(_appDataPath, "settings.json");
        _logPath = Path.Combine(_appDataPath, "logs", "intellifill-winui.log");
        _learnedTemplatesPath = Path.Combine(_appDataPath, "learned_templates.json");
        Directory.CreateDirectory(_appDataPath);
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

        _settings = LoadSettings();
        ApplySettingsToUi();
        PackageStatusText.Text = PackageStatus();
        TraceabilityText.Text = $"Traceability ID: {_traceabilityCode}";
        Log("Application started.");
    }

    private void OpenAppDataButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_appDataPath);
        Process.Start(new ProcessStartInfo("explorer.exe", _appDataPath) { UseShellExecute = true });
        ShowStatus(InfoBarSeverity.Success, "App data opened", _appDataPath);
    }

    private async void UploadTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = CreateDocumentPicker();
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        TemplateProgressRing.IsActive = true;
        BusyRing.IsActive = true;
        try
        {
            NativeDocumentPreview preview = _loader.Load(file.Path);
            _templatePreview = preview;
            _traceabilityCode = CreateTraceabilityCode();
            TraceabilityText.Text = $"Traceability ID: {_traceabilityCode}";
            TemplatePathTextBox.Text = file.Path;
            LoadTemplatePreview(preview);
            ResetOutputFromTemplate(preview);
            RefreshUploadedFilesList();
            Log($"Template loaded: {file.Path}");
            ShowStatus(InfoBarSeverity.Success, "Template loaded", $"{Path.GetFileName(file.Path)} with {_templateTables.Count} table(s).");
        }
        catch (Exception ex)
        {
            Log($"Template upload failed: {ex}");
            ShowStatus(InfoBarSeverity.Error, "Template upload failed", ex.Message);
        }
        finally
        {
            TemplateProgressRing.IsActive = false;
            BusyRing.IsActive = false;
        }
    }

    private async void UploadSourcesButton_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = CreateDocumentPicker();
        IReadOnlyList<Windows.Storage.StorageFile> files = await picker.PickMultipleFilesAsync();
        if (files.Count == 0)
        {
            return;
        }

        BusyRing.IsActive = true;
        try
        {
            _sourcePreviews.Clear();
            foreach (Windows.Storage.StorageFile file in files.Take(5))
            {
                NativeDocumentPreview preview = _loader.LoadManyText(file.Path);
                _sourcePreviews.Add(preview);
                Log($"Source loaded: {file.Path}");
            }

            RefreshUploadedFilesList();
            RefreshParsedText();
            RefreshExtractedFields();
            ShowStatus(InfoBarSeverity.Success, "Sources loaded", $"{_sourcePreviews.Count} source file(s) parsed.");
        }
        catch (Exception ex)
        {
            Log($"Source upload failed: {ex}");
            ShowStatus(InfoBarSeverity.Error, "Source upload failed", ex.Message);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private async void ScanSourceDocument_Click(object sender, RoutedEventArgs e)
    {
        await ShowTextDialogAsync(
            "Scan Source Document",
            "Direct scanner device acquisition still depends on the installed Windows scanner driver. Save the scan as PNG, JPG, or PDF from your scanner utility, then use Upload Source Files. The selected scanned document will show in the preview panel and can be region-selected for mapping.");
    }

    private void SelectOcrRegion_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDocumentPreview is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "Select a document", "Upload and select an image, PDF, or document before drawing a region.");
            UploadedFilesPanel.StartBringIntoView();
            return;
        }

        string extension = Path.GetExtension(_selectedDocumentPreview.Path).ToLowerInvariant();
        if (extension is not ".png" and not ".jpg" and not ".jpeg" and not ".pdf")
        {
            ShowStatus(InfoBarSeverity.Warning, "Region selection is for visual documents", "Select an image or PDF document. For CSV, Excel, and Word, use the table selector and parsed text.");
            UploadedFilesPanel.StartBringIntoView();
            return;
        }

        _regionSelectionMode = true;
        _regionStart = null;
        _selectedRegion = null;
        PreviewRegionRectangle.Visibility = Visibility.Collapsed;
        RegionSelectionText.Text = "Draw a rectangle on the preview";
        UploadedFilesPanel.StartBringIntoView();
        ShowStatus(InfoBarSeverity.Informational, "Region selection enabled", "Drag on the document preview to select an OCR region.");
    }

    private void AutoFillButton_Click(object sender, RoutedEventArgs e)
    {
        if (_outputTables.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "Template required", "Upload a template before auto filling.");
            return;
        }
        if (_extractedFields.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "Source fields required", "Upload source files before auto filling.");
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
        ValidationResultsBox.Text = filled == 0
            ? "No confident automatic matches were found."
            : "Auto-fill matches:" + Environment.NewLine + string.Join(Environment.NewLine, details.Take(40));
        ShowStatus(filled > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning, "Auto fill complete", $"{filled} cell(s) filled.");
        Log($"Auto fill completed. Filled={filled}");
    }

    private void MapSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedField is null || _selectedDestination is null)
        {
            ShowStatus(InfoBarSeverity.Warning, "Selection required", "Select one extracted field and one output cell first.");
            return;
        }

        CellAddress address = _selectedDestination;
        if (!IsValidAddress(address))
        {
            ShowStatus(InfoBarSeverity.Error, "Invalid destination", "The selected output cell is no longer available.");
            return;
        }

        string value = string.IsNullOrWhiteSpace(_selectedField.Value) ? _selectedField.Label : _selectedField.Value;
        _outputTables[address.TableIndex][address.RowIndex][address.ColumnIndex] = value;
        AddMapping(_selectedField, address, value);
        RenderOutputTable();
        OutputSelectionText.Text = $"Mapped {_selectedField.Label} to table {address.TableIndex + 1}, row {address.RowIndex + 1}, column {address.ColumnIndex + 1}.";
        ShowStatus(InfoBarSeverity.Success, "Mapped selected field", value);
        Log($"Manual mapping: {_selectedField.Label} -> T{address.TableIndex} R{address.RowIndex} C{address.ColumnIndex}");
    }

    private async void SaveMappingTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_mappings.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "No mappings", "Map at least one field before saving a mapping template.");
            return;
        }

        FileSavePicker picker = CreateSavePicker("mapping.json", "JSON mapping", ".json");
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        var payload = new MappingFile(AppVersion, _templatePreview?.Name ?? "Template", _mappings);
        File.WriteAllText(file.Path, JsonSerializer.Serialize(payload, JsonOptions()), System.Text.Encoding.UTF8);
        ShowStatus(InfoBarSeverity.Success, "Mapping saved", file.Path);
        Log($"Mapping saved: {file.Path}");
    }

    private async void LoadMappingTemplate_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new() { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializePicker(picker);
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            MappingFile? payload = JsonSerializer.Deserialize<MappingFile>(File.ReadAllText(file.Path), JsonOptions());
            if (payload is null)
            {
                throw new InvalidDataException("Mapping file is empty.");
            }
            ApplyMappings(payload.Mappings);
            ShowStatus(InfoBarSeverity.Success, "Mapping loaded", $"{payload.Mappings.Count} mapping(s) applied.");
            Log($"Mapping loaded: {file.Path}");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Mapping load failed", ex.Message);
        }
    }

    private async void SaveLearnedTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_mappings.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "No mappings", "Map fields before saving a learned template.");
            return;
        }

        string name = _templatePreview?.Name ?? $"Learned Template {DateTime.Now:yyyyMMdd-HHmm}";
        List<LearnedTemplate> learned = LoadLearnedTemplates();
        learned.RemoveAll(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        learned.Add(new LearnedTemplate(
            name,
            DateTimeOffset.Now,
            FingerprintCurrentSources(),
            _mappings.ToList()));
        SaveLearnedTemplates(learned);
        await ShowTextDialogAsync("Learned Template Saved", $"{name} was saved with {_mappings.Count} mapping(s). Future similar sources can be suggested or applied from Actions > Template Learning.");
        ShowStatus(InfoBarSeverity.Success, "Learned template saved", name);
    }

    private async void SuggestLearnedTemplates_Click(object sender, RoutedEventArgs e)
    {
        List<(LearnedTemplate Template, double Score)> matches = ScoreLearnedTemplates();
        if (matches.Count == 0)
        {
            await ShowTextDialogAsync("Learned Template Suggestions", "No learned templates have been saved yet.");
            return;
        }

        string text = string.Join(
            Environment.NewLine,
            matches.Take(10).Select(match => $"{match.Template.Name}: {match.Score:P0} confidence, {match.Template.Mappings.Count} mapping(s)"));
        await ShowTextDialogAsync("Learned Template Suggestions", text);
    }

    private void ApplyBestLearnedTemplate_Click(object sender, RoutedEventArgs e)
    {
        List<(LearnedTemplate Template, double Score)> matches = ScoreLearnedTemplates();
        (LearnedTemplate Template, double Score) best = matches.FirstOrDefault();
        if (best.Template is null || best.Score <= 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "No learned match", "No learned template matched the current source documents.");
            return;
        }

        ApplyMappings(best.Template.Mappings);
        ShowStatus(InfoBarSeverity.Success, "Learned template applied", $"{best.Template.Name} ({best.Score:P0} confidence).");
    }

    private async void RunValidationChecks_Click(object sender, RoutedEventArgs e)
    {
        List<string> issues = ValidateOutput();
        ValidationResultsBox.Text = issues.Count == 0 ? "Validation passed. No warnings found." : string.Join(Environment.NewLine, issues);
        await ShowTextDialogAsync("Validation Results", ValidationResultsBox.Text);
        ShowStatus(issues.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning, "Validation complete", $"{issues.Count} issue(s) found.");
    }

    private void SaveToDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (_outputTables.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "Template required", "Upload and fill a template before saving to SQLite.");
            return;
        }

        try
        {
            _databaseService.SaveRun(
                _settings.DatabasePath,
                _traceabilityCode,
                _templatePreview?.Path ?? "",
                _sourcePreviews.Select(source => source.Path).ToList(),
                BuildRunValues(),
                _mappings.Select(mapping => $"{mapping.DestinationLabel} <- {mapping.SourceLabel}: {mapping.Value}").ToList());
            DatabasePreviewBox.Text = _databaseService.Preview(_settings.DatabasePath);
            ShowStatus(InfoBarSeverity.Success, "Saved to SQLite", _settings.DatabasePath);
            Log($"Saved run to SQLite: {_settings.DatabasePath}");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "SQLite save failed", ex.Message);
            Log($"SQLite save failed: {ex}");
        }
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e) => await ExportAsync("CSV", ".csv", path => _exportService.ExportCsv(BuildOutputTables(), path, _traceabilityCode));

    private async void ExportExcel_Click(object sender, RoutedEventArgs e) => await ExportAsync("Excel workbook", ".xlsx", path => _exportService.ExportXlsx(BuildOutputTables(), path, _traceabilityCode));

    private async void ExportWord_Click(object sender, RoutedEventArgs e) => await ExportAsync("Word document", ".docx", path => _exportService.ExportDocx(BuildOutputTables(), path, _traceabilityCode));

    private async void ExportPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("PDF with traceability barcode", ".pdf", path => _exportService.ExportPdf(BuildOutputTables(), path, _traceabilityCode));

    private async void ExportOriginalLayout_Click(object sender, RoutedEventArgs e)
    {
        string extension = Path.GetExtension(_templatePreview?.Path ?? "").ToLowerInvariant();
        if (extension == ".xlsx")
        {
            await ExportAsync("filled template workbook", ".xlsx", path => _exportService.ExportXlsx(BuildOutputTables(), path, _traceabilityCode));
        }
        else if (extension == ".docx")
        {
            await ExportAsync("filled template Word document", ".docx", path => _exportService.ExportDocx(BuildOutputTables(), path, _traceabilityCode));
        }
        else
        {
            await ExportAsync("filled template CSV", ".csv", path => _exportService.ExportCsv(BuildOutputTables(), path, _traceabilityCode));
        }
    }

    private async void ExportPreservedPdf_Click(object sender, RoutedEventArgs e) => await ExportAsync("filled template PDF", ".pdf", path => _exportService.ExportPdf(BuildOutputTables(), path, _traceabilityCode));

    private async void DetectSignaturesAndStamps_Click(object sender, RoutedEventArgs e)
    {
        string result = _sourcePreviews.Count == 0
            ? "Upload source files first. Signature and stamp review uses the parsed source list and keeps the original files untouched for exports."
            : "Signature/stamp review candidates:" + Environment.NewLine + string.Join(Environment.NewLine, _sourcePreviews.Select(source => "- " + source.Name));
        await ShowTextDialogAsync("Signature and Stamp Detection", result);
    }

    private void PreviewDatabase_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            DatabasePreviewBox.Text = _databaseService.Preview(_settings.DatabasePath);
            ShowStatus(InfoBarSeverity.Success, "Database preview refreshed", _settings.DatabasePath);
        }
        catch (Exception ex)
        {
            DatabasePreviewBox.Text = ex.ToString();
            ShowStatus(InfoBarSeverity.Error, "Database preview failed", ex.Message);
        }
    }

    private async void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        string text = File.Exists(_logPath) ? File.ReadAllText(_logPath) : "No log file has been created yet.";
        await ShowTextDialogAsync("Application Logs", text);
    }

    private void RestoreAllPanels_Click(object sender, RoutedEventArgs e)
    {
        UploadedFilesPanel.Visibility = Visibility.Visible;
        ExtractedFieldsPanel.Visibility = Visibility.Visible;
        OutputPreviewPanel.Visibility = Visibility.Visible;
        TemplatePanel.Visibility = Visibility.Visible;
        DatabasePanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Visible;
        ShowStatus(InfoBarSeverity.Success, "Panels restored", "All workflow panels are visible.");
    }

    private void ToggleUploadedFilesPanel_Click(object sender, RoutedEventArgs e) => TogglePanel(UploadedFilesPanel, "Uploaded Files");

    private void ToggleExtractedFieldsPanel_Click(object sender, RoutedEventArgs e) => TogglePanel(ExtractedFieldsPanel, "Extracted Fields");

    private void ToggleOutputPreviewPanel_Click(object sender, RoutedEventArgs e) => TogglePanel(OutputPreviewPanel, "Output Preview");

    private void ShowSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Visible;
        ShowStatus(InfoBarSeverity.Informational, "Settings", "Tesseract, SQLite, and appearance settings are visible.");
    }

    private async void OpenHelpGuide_Click(object sender, RoutedEventArgs e)
    {
        await ShowTextDialogAsync("User Guide and Feature Help", HelpText());
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IntelliFillOCR-WinUI");
            string json = await client.GetStringAsync("https://api.github.com/repos/Abijspy/intellifill-ocr/releases/latest");
            using JsonDocument document = JsonDocument.Parse(json);
            string latest = document.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
            string latestVersion = latest.TrimStart('v');
            if (!IsNewerVersion(latestVersion, AppVersion))
            {
                await ShowTextDialogAsync("Check for Updates", $"You are on the latest version ({AppVersion}).");
                return;
            }

            JsonElement? asset = document.RootElement
                .GetProperty("assets")
                .EnumerateArray()
                .Where(item => item.TryGetProperty("name", out JsonElement nameElement) &&
                               nameElement.GetString() is string name &&
                               name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                               name.Contains("setup-win-x64", StringComparison.OrdinalIgnoreCase))
                .Cast<JsonElement?>()
                .FirstOrDefault();
            if (asset is null)
            {
                await ShowTextDialogAsync("Check for Updates", $"IntelliFill OCR {latestVersion} is available, but the release does not contain a Windows setup installer.");
                return;
            }

            string assetName = asset.Value.GetProperty("name").GetString() ?? $"IntelliFillOCR-{latestVersion}-setup-win-x64.exe";
            string downloadUrl = asset.Value.GetProperty("browser_download_url").GetString() ?? "";
            var prompt = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = $"Update available: {latestVersion}",
                Content = $"Current version: {AppVersion}\nLatest version: {latestVersion}\n\nDownload and run {assetName} now?",
                PrimaryButtonText = "Download and Install",
                CloseButtonText = "Later"
            };
            ContentDialogResult answer = await prompt.ShowAsync();
            if (answer != ContentDialogResult.Primary)
            {
                return;
            }

            string updateDir = Path.Combine(_appDataPath, "updates");
            Directory.CreateDirectory(updateDir);
            string updatePath = Path.Combine(updateDir, SanitizeFileName(assetName));
            await using (Stream download = await client.GetStreamAsync(downloadUrl))
            await using (FileStream file = File.Create(updatePath))
            {
                await download.CopyToAsync(file);
            }

            Log($"Downloaded update: {updatePath}");
            Process.Start(new ProcessStartInfo(updatePath, "/S") { UseShellExecute = true });
            Microsoft.UI.Xaml.Application.Current.Exit();
        }
        catch (Exception ex)
        {
            await ShowTextDialogAsync("Check for Updates", $"Could not check GitHub releases. Offline use is still supported.\n\n{ex.Message}");
        }
    }

    private async void OpenWhatsNew_Click(object sender, RoutedEventArgs e)
    {
        await ShowTextDialogAsync(
            "What's New",
            $"Version {AppVersion}\n\n- Legacy WinUI reference shell remains available during Avalonia migration.\n- Release packaging now uses the Avalonia app and Windows NSIS setup installer.\n- Portable updater EXE packaging has been removed.\n\nEarlier highlights\n- v3.4.0: Avalonia desktop migration and NSIS installer support.\n- v3.x: native WinUI shell.\n- v2.x: traceability barcode, preserved exports, template learning, validation, signature/stamp tools, scanner workflow, and detailed help.");
    }

    private async void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        await ShowTextDialogAsync("About IntelliFill OCR", $"IntelliFill OCR v{AppVersion}\nOffline Windows desktop application for OCR-driven data extraction, table filling, validation, SQLite storage, and traceable exports.");
    }

    private async void BrowseTesseractPath_Click(object sender, RoutedEventArgs e)
    {
        FileOpenPicker picker = new() { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".exe");
        InitializePicker(picker);
        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            TesseractPathBox.Text = file.Path;
        }
    }

    private async void BrowseDatabasePath_Click(object sender, RoutedEventArgs e)
    {
        FileSavePicker picker = CreateSavePicker("intellifill.sqlite3", "SQLite database", ".sqlite3");
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            DatabasePathBox.Text = file.Path;
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = new AppSettings
        {
            TesseractPath = TesseractPathBox.Text.Trim(),
            DatabasePath = string.IsNullOrWhiteSpace(DatabasePathBox.Text) ? DefaultDatabasePath() : Environment.ExpandEnvironmentVariables(DatabasePathBox.Text.Trim()),
            Theme = SelectedTheme()
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_settings.DatabasePath) ?? _appDataPath);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_settings, JsonOptions()), System.Text.Encoding.UTF8);
        ApplyTheme();
        PackageStatusText.Text = PackageStatus();
        ShowStatus(InfoBarSeverity.Success, "Settings saved", _settingsPath);
        Log("Settings saved.");
    }

    private void TemplateTableSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderSelectedTemplateTable();
    }

    private void OutputTableSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderOutputTable();
    }

    private void ExtractedFieldsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = ExtractedFieldsListView.SelectedIndex;
        _selectedField = index >= 0 && index < _extractedFields.Count ? _extractedFields[index] : null;
        if (_selectedField is not null)
        {
            ShowStatus(InfoBarSeverity.Informational, "Source field selected", $"{_selectedField.Label}: {_selectedField.Value}");
        }
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        FrameworkElement? target = tag switch
        {
            "workspace" => WorkflowPanel,
            "template" => TemplatePanel,
            "sources" => UploadedFilesPanel,
            "mapping" => ExtractedFieldsPanel,
            "output" => OutputPreviewPanel,
            "database" => DatabasePanel,
            "settings" => SettingsPanel,
            "about" => WorkflowPanel,
            _ => null
        };

        target?.StartBringIntoView();

        string message = tag switch
        {
            "workspace" => "Use Actions for every workflow command.",
            "template" => "Upload and preview every detected template table.",
            "sources" => "Select uploaded documents, preview files, select document tables, and draw regions.",
            "mapping" => "Select extracted fields and destination cells, then map or auto-fill.",
            "output" => "Edit output cells, validate, save, and export.",
            "database" => "Preview SQLite records and saved runs.",
            "settings" => "Configure Tesseract, SQLite, and appearance.",
            "about" => $"IntelliFill OCR v{AppVersion}.",
            _ => "Ready."
        };
        ShowStatus(InfoBarSeverity.Informational, item.Content?.ToString() ?? "IntelliFill OCR", message);
    }

    private FileOpenPicker CreateDocumentPicker()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        foreach (string extension in new[] { ".csv", ".txt", ".xlsx", ".xls", ".docx", ".pdf", ".png", ".jpg", ".jpeg" })
        {
            picker.FileTypeFilter.Add(extension);
        }
        InitializePicker(picker);
        return picker;
    }

    private FileSavePicker CreateSavePicker(string suggestedName, string label, string extension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName)
        };
        picker.FileTypeChoices.Add(label, new List<string> { extension });
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return picker;
    }

    private void InitializePicker(FileOpenPicker picker)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    private async Task ShowTextDialogAsync(string title, string content)
    {
        var text = new TextBox
        {
            Text = content,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 620,
            MaxHeight = 520
        };
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = new ScrollViewer { Content = text, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "Close"
        };
        await dialog.ShowAsync();
    }

    private void LoadTemplatePreview(NativeDocumentPreview preview)
    {
        _templateTables.Clear();
        TemplateTableSelector.Items.Clear();
        foreach (NativeDocumentTable table in preview.Tables)
        {
            _templateTables.Add(table);
            TemplateTableSelector.Items.Add($"{table.Label} ({table.RowCount} x {table.ColumnCount})");
        }

        TemplateSummaryText.Text = $"{preview.Name}: {_templateTables.Count} table(s) detected from {Path.GetFileName(preview.Path)}.";
        TemplateTableSelector.SelectedIndex = _templateTables.Count > 0 ? 0 : -1;
        RenderSelectedTemplateTable();
    }

    private void RenderSelectedTemplateTable()
    {
        TemplatePreviewGrid.Children.Clear();
        TemplatePreviewGrid.RowDefinitions.Clear();
        TemplatePreviewGrid.ColumnDefinitions.Clear();

        int selectedIndex = TemplateTableSelector.SelectedIndex;
        if (selectedIndex < 0 || selectedIndex >= _templateTables.Count)
        {
            return;
        }

        NativeDocumentTable table = _templateTables[selectedIndex];
        RenderReadOnlyGrid(TemplatePreviewGrid, table.Rows, table.Label, updateSummary: true);
    }

    private void RenderReadOnlyGrid(Grid grid, IReadOnlyList<IReadOnlyList<string>> rows, string label, bool updateSummary)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();

        int rowCount = rows.Count;
        int columnCount = rows.Select(row => row.Count).DefaultIfEmpty(0).Max();
        if (rowCount == 0 || columnCount == 0)
        {
            if (updateSummary)
            {
                TemplateSummaryText.Text = $"{label}: no cells detected.";
            }
            return;
        }

        grid.MinWidth = Math.Max(640, columnCount * 160);
        for (int column = 0; column < columnCount; column++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        }
        for (int row = 0; row < rowCount; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var borderBrush = new SolidColorBrush(Colors.Gray);
        var headerBrush = new SolidColorBrush(ColorHelper.FromArgb(36, 0, 120, 212));
        var emptyBrush = new SolidColorBrush(ColorHelper.FromArgb(20, 220, 38, 38));
        for (int row = 0; row < rowCount; row++)
        {
            IReadOnlyList<string> values = rows[row];
            for (int column = 0; column < columnCount; column++)
            {
                string value = column < values.Count ? values[column] : "";
                var text = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(value) ? " " : value,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    FontWeight = row == 0 ? FontWeights.SemiBold : FontWeights.Normal
                };

                var border = new Border
                {
                    BorderBrush = borderBrush,
                    BorderThickness = new Thickness(1),
                    Background = row == 0 ? headerBrush : IsFillablePlaceholder(value) ? emptyBrush : null,
                    MinHeight = 36,
                    Padding = new Thickness(8, 6, 8, 6),
                    Child = text
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, column);
                grid.Children.Add(border);
            }
        }

        if (updateSummary)
        {
            TemplateSummaryText.Text = $"{label}: {rowCount} rows and {columnCount} columns.";
        }
    }

    private void ResetOutputFromTemplate(NativeDocumentPreview preview)
    {
        _outputTables.Clear();
        _mappings.Clear();
        OutputTableSelector.Items.Clear();
        _selectedDestination = null;
        foreach (NativeDocumentTable table in preview.Tables)
        {
            int columnCount = Math.Max(1, table.ColumnCount);
            var rows = table.Rows
                .Select(row =>
                {
                    var values = row.ToList();
                    while (values.Count < columnCount)
                    {
                        values.Add(string.Empty);
                    }
                    return values;
                })
                .ToList();
            _outputTables.Add(rows);
            OutputTableSelector.Items.Add($"{table.Label} ({table.RowCount} x {table.ColumnCount})");
        }

        OutputTableSelector.SelectedIndex = _outputTables.Count > 0 ? 0 : -1;
        RenderOutputTable();
    }

    private void RenderOutputTable()
    {
        OutputPreviewGrid.Children.Clear();
        OutputPreviewGrid.RowDefinitions.Clear();
        OutputPreviewGrid.ColumnDefinitions.Clear();
        _outputCellBindings.Clear();

        int tableIndex = OutputTableSelector.SelectedIndex;
        if (tableIndex < 0 || tableIndex >= _outputTables.Count)
        {
            return;
        }

        List<List<string>> rows = _outputTables[tableIndex];
        int rowCount = rows.Count;
        int columnCount = rows.Select(row => row.Count).DefaultIfEmpty(0).Max();
        OutputPreviewGrid.MinWidth = Math.Max(680, columnCount * 170);
        for (int column = 0; column < columnCount; column++)
        {
            OutputPreviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        }
        for (int row = 0; row < rowCount; row++)
        {
            OutputPreviewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                string value = column < rows[row].Count ? rows[row][column] : string.Empty;
                var textBox = new TextBox
                {
                    Text = value,
                    TextWrapping = TextWrapping.Wrap,
                    MinHeight = 38,
                    BorderThickness = new Thickness(0),
                    Background = IsFillablePlaceholder(value)
                        ? new SolidColorBrush(ColorHelper.FromArgb(30, 220, 38, 38))
                        : row == 0
                            ? new SolidColorBrush(ColorHelper.FromArgb(36, 0, 120, 212))
                            : null,
                    FontWeight = row == 0 ? FontWeights.SemiBold : FontWeights.Normal
                };
                var address = new CellAddress(tableIndex, row, column);
                _outputCellBindings[textBox] = address;
                textBox.GotFocus += OutputCell_GotFocus;
                textBox.TextChanged += OutputCell_TextChanged;

                var border = new Border
                {
                    BorderBrush = new SolidColorBrush(Colors.Gray),
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

    private void OutputCell_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && _outputCellBindings.TryGetValue(textBox, out CellAddress? address) && address is not null)
        {
            _selectedDestination = address;
            OutputSelectionText.Text = $"Destination selected: table {address.TableIndex + 1}, row {address.RowIndex + 1}, column {address.ColumnIndex + 1}.";
        }
    }

    private void OutputCell_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox textBox && _outputCellBindings.TryGetValue(textBox, out CellAddress? address) && address is not null && IsValidAddress(address))
        {
            _outputTables[address.TableIndex][address.RowIndex][address.ColumnIndex] = textBox.Text;
        }
    }

    private void RefreshUploadedFilesList()
    {
        int previousIndex = SourcesListView.SelectedIndex;
        _uploadedDocuments.Clear();
        SourcesListView.Items.Clear();
        if (_templatePreview is not null)
        {
            _uploadedDocuments.Add(new DocumentItem("Template", _templatePreview));
            SourcesListView.Items.Add($"Template: {Path.GetFileName(_templatePreview.Path)}  -  {_templatePreview.Tables.Count} table(s)");
        }
        foreach (NativeDocumentPreview preview in _sourcePreviews)
        {
            _uploadedDocuments.Add(new DocumentItem("Source", preview));
            SourcesListView.Items.Add($"Source: {Path.GetFileName(preview.Path)}  -  {preview.Tables.Count} table(s)");
        }

        if (_uploadedDocuments.Count == 0)
        {
            SelectDocumentPreview(null);
            return;
        }

        SourcesListView.SelectedIndex = previousIndex >= 0 && previousIndex < _uploadedDocuments.Count ? previousIndex : 0;
    }

    private void RefreshParsedText()
    {
        if (_selectedDocumentPreview is not null)
        {
            ParsedTextBox.Text = _selectedDocumentPreview.ParsedText;
            return;
        }

        ParsedTextBox.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            _sourcePreviews.Select(preview => $"[{preview.Name}]{Environment.NewLine}{preview.ParsedText}"));
    }

    private void SourcesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int index = SourcesListView.SelectedIndex;
        SelectDocumentPreview(index >= 0 && index < _uploadedDocuments.Count ? _uploadedDocuments[index] : null);
    }

    private void SourceTableSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderSelectedSourceTable();
    }

    private void PreviewZoomOut_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewZoom(_previewZoom - PreviewZoomStep);
    }

    private void PreviewZoomIn_Click(object sender, RoutedEventArgs e)
    {
        SetPreviewZoom(_previewZoom + PreviewZoomStep);
    }

    private void PreviewResetView_Click(object sender, RoutedEventArgs e)
    {
        ResetPreviewView();
    }

    private void PreviewRotateLeft_Click(object sender, RoutedEventArgs e)
    {
        _previewRotation = NormalizeRotation(_previewRotation - 90);
        ApplyPreviewView(resetSelection: true);
    }

    private void PreviewRotateRight_Click(object sender, RoutedEventArgs e)
    {
        _previewRotation = NormalizeRotation(_previewRotation + 90);
        ApplyPreviewView(resetSelection: true);
    }

    private void SelectDocumentPreview(DocumentItem? item)
    {
        _selectedDocumentPreview = item?.Preview;
        SourceTableSelector.Items.Clear();
        SourceTablePreviewGrid.Children.Clear();
        SourceTablePreviewGrid.RowDefinitions.Clear();
        SourceTablePreviewGrid.ColumnDefinitions.Clear();
        PreviewRegionRectangle.Visibility = Visibility.Collapsed;
        RegionSelectionText.Text = "No region selected";
        _regionSelectionMode = false;
        _regionStart = null;
        _selectedRegion = null;
        ResetPreviewView();

        if (_selectedDocumentPreview is null)
        {
            SelectedDocumentText.Text = "Upload and select a document to preview it.";
            ParsedTextBox.Text = "";
            DocumentPreviewImage.Visibility = Visibility.Collapsed;
            DocumentPreviewWebView.Visibility = Visibility.Collapsed;
            DocumentPreviewMessage.Visibility = Visibility.Visible;
            DocumentPreviewMessage.Text = "Preview will appear here for images and PDFs. Tables and parsed text are shown on the right and below.";
            return;
        }

        SelectedDocumentText.Text = $"{item?.Kind}: {Path.GetFileName(_selectedDocumentPreview.Path)}";
        ParsedTextBox.Text = _selectedDocumentPreview.ParsedText;
        foreach (NativeDocumentTable table in _selectedDocumentPreview.Tables)
        {
            SourceTableSelector.Items.Add($"{table.Label} ({table.RowCount} x {table.ColumnCount})");
        }
        SourceTableSelector.SelectedIndex = _selectedDocumentPreview.Tables.Count > 0 ? 0 : -1;
        RenderDocumentPreview(_selectedDocumentPreview);
    }

    private void RenderDocumentPreview(NativeDocumentPreview preview)
    {
        string extension = Path.GetExtension(preview.Path).ToLowerInvariant();
        DocumentPreviewImage.Visibility = Visibility.Collapsed;
        DocumentPreviewWebView.Visibility = Visibility.Collapsed;
        DocumentPreviewMessage.Visibility = Visibility.Collapsed;
        ApplyPreviewView(resetSelection: false);

        try
        {
            if (extension is ".png" or ".jpg" or ".jpeg")
            {
                DocumentPreviewImage.Source = new BitmapImage(ToFileUri(preview.Path));
                DocumentPreviewImage.Visibility = Visibility.Visible;
                RegionSelectionText.Text = "Use Select Region to draw OCR area";
                return;
            }

            if (extension == ".pdf")
            {
                DocumentPreviewWebView.Source = ToFileUri(preview.Path);
                DocumentPreviewWebView.Visibility = Visibility.Visible;
                RegionSelectionText.Text = "Use Select Region to draw PDF area";
                return;
            }
        }
        catch (Exception ex)
        {
            Log($"Preview render failed for {preview.Path}: {ex}");
        }

        DocumentPreviewMessage.Visibility = Visibility.Visible;
        DocumentPreviewMessage.Text = preview.Tables.Count > 0
            ? "This document is table/text based. Use the table selector and parsed text preview."
            : "No visual preview is available for this document type.";
        RegionSelectionText.Text = "Table/text document";
    }

    private void RenderSelectedSourceTable()
    {
        SourceTablePreviewGrid.Children.Clear();
        SourceTablePreviewGrid.RowDefinitions.Clear();
        SourceTablePreviewGrid.ColumnDefinitions.Clear();

        if (_selectedDocumentPreview is null)
        {
            return;
        }

        int index = SourceTableSelector.SelectedIndex;
        if (index < 0 || index >= _selectedDocumentPreview.Tables.Count)
        {
            return;
        }

        NativeDocumentTable table = _selectedDocumentPreview.Tables[index];
        RenderReadOnlyGrid(SourceTablePreviewGrid, table.Rows, table.Label, updateSummary: false);
    }

    private void OpenSelectedDocument_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDocumentPreview is null || !File.Exists(_selectedDocumentPreview.Path))
        {
            ShowStatus(InfoBarSeverity.Warning, "No document selected", "Select an uploaded document first.");
            return;
        }

        Process.Start(new ProcessStartInfo(_selectedDocumentPreview.Path) { UseShellExecute = true });
    }

    private void PreviewSelectionCanvas_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_regionSelectionMode)
        {
            return;
        }

        _regionStart = e.GetCurrentPoint(PreviewSelectionCanvas).Position;
        Canvas.SetLeft(PreviewRegionRectangle, _regionStart.Value.X);
        Canvas.SetTop(PreviewRegionRectangle, _regionStart.Value.Y);
        PreviewRegionRectangle.Width = 0;
        PreviewRegionRectangle.Height = 0;
        PreviewRegionRectangle.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void PreviewSelectionCanvas_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_regionSelectionMode || _regionStart is null)
        {
            return;
        }

        Point current = e.GetCurrentPoint(PreviewSelectionCanvas).Position;
        UpdateSelectionRectangle(_regionStart.Value, current);
        e.Handled = true;
    }

    private async void PreviewSelectionCanvas_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_regionSelectionMode || _regionStart is null)
        {
            return;
        }

        Point end = e.GetCurrentPoint(PreviewSelectionCanvas).Position;
        _selectedRegion = UpdateSelectionRectangle(_regionStart.Value, end);
        _regionSelectionMode = false;
        _regionStart = null;
        e.Handled = true;

        if (_selectedRegion.Value.Width < 8 || _selectedRegion.Value.Height < 8)
        {
            PreviewRegionRectangle.Visibility = Visibility.Collapsed;
            RegionSelectionText.Text = "Selection too small";
            ShowStatus(InfoBarSeverity.Warning, "Region too small", "Draw a larger rectangle on the preview.");
            return;
        }

        RegionSelectionText.Text = FormatRegion(_selectedRegion.Value);
        await AddSelectedRegionFieldAsync(_selectedRegion.Value);
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

    private async Task AddSelectedRegionFieldAsync(Rect region)
    {
        if (_selectedDocumentPreview is null)
        {
            return;
        }

        var labelBox = new TextBox
        {
            Header = "Field label",
            Text = $"Region from {Path.GetFileNameWithoutExtension(_selectedDocumentPreview.Path)}",
            MinWidth = 420
        };
        var valueBox = new TextBox
        {
            Header = "Extracted value / manual correction",
            PlaceholderText = "Type the OCR result or field value from this selected region",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100
        };
        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(new TextBlock
        {
            Text = $"{Path.GetFileName(_selectedDocumentPreview.Path)}\n{FormatRegion(region)}\n\nNative region selection is captured locally. Enter or correct the OCR text here, then map it to the output table.",
            TextWrapping = TextWrapping.WrapWholeWords
        });
        stack.Children.Add(labelBox);
        stack.Children.Add(valueBox);

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "Add Selected Region as Field",
            Content = stack,
            PrimaryButtonText = "Add Field",
            CloseButtonText = "Cancel"
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        string label = string.IsNullOrWhiteSpace(labelBox.Text) ? "Selected Region" : labelBox.Text.Trim();
        string value = string.IsNullOrWhiteSpace(valueBox.Text) ? FormatRegion(region) : valueBox.Text.Trim();
        AddExtractedField(new ExtractedField(_extractedFields.Count + 1, _selectedDocumentPreview.Name, label, value, 0.80));
        ShowStatus(InfoBarSeverity.Success, "Region field added", $"{label}: {value}");
    }

    private void AddExtractedField(ExtractedField field)
    {
        _extractedFields.Add(field);
        ExtractedFieldsListView.Items.Add($"{field.Label}: {field.Value}  ({field.SourceName}, {field.Confidence:P0})");
        ExtractedFieldsListView.SelectedIndex = _extractedFields.Count - 1;
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
        DocumentPreviewHost.Width = PreviewBaseWidth * _previewZoom;
        DocumentPreviewHost.Height = PreviewBaseHeight * _previewZoom;
        PreviewSelectionCanvas.Width = DocumentPreviewHost.Width;
        PreviewSelectionCanvas.Height = DocumentPreviewHost.Height;
        PreviewZoomText.Text = $"{_previewZoom:P0} / {_previewRotation}°";

        DocumentPreviewImage.RenderTransform = new RotateTransform { Angle = _previewRotation };
        DocumentPreviewWebView.RenderTransform = new RotateTransform { Angle = _previewRotation };

        if (resetSelection)
        {
            _regionSelectionMode = false;
            _regionStart = null;
            _selectedRegion = null;
            PreviewRegionRectangle.Visibility = Visibility.Collapsed;
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

    private void RefreshExtractedFields()
    {
        _extractedFields.Clear();
        ExtractedFieldsListView.Items.Clear();
        foreach (NativeDocumentPreview preview in _sourcePreviews)
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

    private static IEnumerable<ExtractedField> ExtractFields(NativeDocumentPreview preview)
    {
        int id = 1;
        foreach (NativeDocumentTable table in preview.Tables)
        {
            if (table.Rows.Count > 1)
            {
                IReadOnlyList<string> headers = table.Rows[0];
                foreach (IReadOnlyList<string> row in table.Rows.Skip(1))
                {
                    for (int column = 0; column < row.Count; column++)
                    {
                        string value = row[column].Trim();
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            continue;
                        }
                        string label = column < headers.Count && !string.IsNullOrWhiteSpace(headers[column])
                            ? headers[column].Trim()
                            : $"Column {column + 1}";
                        yield return new ExtractedField(id++, preview.Name, label, value, 0.86);
                    }
                }
            }
        }

        foreach (string line in preview.ParsedText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 2)
            {
                continue;
            }
            string label = trimmed;
            string value = trimmed;
            Match match = Regex.Match(trimmed, @"^\s*(?<label>[^:=\-]{2,80})\s*[:=\-]\s*(?<value>.+?)\s*$");
            if (match.Success)
            {
                label = match.Groups["label"].Value.Trim();
                value = match.Groups["value"].Value.Trim();
            }
            yield return new ExtractedField(id++, preview.Name, label, value, match.Success ? 0.91 : 0.66);
        }
    }

    private void AddMapping(ExtractedField field, CellAddress address, string value)
    {
        string destinationLabel = DestinationLabel(_outputTables[address.TableIndex], address.RowIndex, address.ColumnIndex);
        _mappings.RemoveAll(mapping => mapping.TableIndex == address.TableIndex && mapping.RowIndex == address.RowIndex && mapping.ColumnIndex == address.ColumnIndex);
        _mappings.Add(new MappingSnapshot(field.Label, field.Value, address.TableIndex, address.RowIndex, address.ColumnIndex, value, destinationLabel));
    }

    private void ApplyMappings(IReadOnlyList<MappingSnapshot> mappings)
    {
        int applied = 0;
        foreach (MappingSnapshot mapping in mappings)
        {
            if (mapping.TableIndex < 0 || mapping.TableIndex >= _outputTables.Count)
            {
                continue;
            }
            CellAddress address = new(mapping.TableIndex, mapping.RowIndex, mapping.ColumnIndex);
            if (!IsValidAddress(address))
            {
                continue;
            }

            string value = mapping.Value;
            ExtractedField? currentField = _extractedFields
                .OrderByDescending(field => Math.Max(Similarity(mapping.SourceLabel, field.Label), Similarity(mapping.SourceValue, field.Value)))
                .FirstOrDefault();
            if (currentField is not null)
            {
                value = string.IsNullOrWhiteSpace(currentField.Value) ? currentField.Label : currentField.Value;
            }

            _outputTables[address.TableIndex][address.RowIndex][address.ColumnIndex] = value;
            applied++;
        }
        _mappings.Clear();
        _mappings.AddRange(mappings);
        RenderOutputTable();
        ShowStatus(InfoBarSeverity.Success, "Mappings applied", $"{applied} mapping(s) applied.");
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

        foreach (List<List<string>> table in _outputTables)
        {
            Dictionary<string, decimal> amounts = CollectNamedAmounts(table);
            if (amounts.TryGetValue("subtotal", out decimal subtotal) &&
                amounts.TryGetValue("tax", out decimal tax) &&
                amounts.TryGetValue("total", out decimal total) &&
                Math.Abs((subtotal + tax) - total) > 0.05m)
            {
                issues.Add($"Invoice total mismatch: subtotal {subtotal} + tax {tax} does not equal total {total}.");
            }
        }

        return issues;
    }

    private async Task ExportAsync(string label, string extension, Action<string> export)
    {
        if (_outputTables.Count == 0)
        {
            ShowStatus(InfoBarSeverity.Warning, "Template required", "Upload and fill a template before exporting.");
            return;
        }

        FileSavePicker picker = CreateSavePicker($"intellifill-output-{_traceabilityCode}{extension}", label, extension);
        Windows.Storage.StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        try
        {
            export(file.Path);
            ShowStatus(InfoBarSeverity.Success, $"Exported {label}", file.Path);
            Log($"Exported {label}: {file.Path}");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, $"Export {label} failed", ex.Message);
            Log($"Export failed ({label}): {ex}");
        }
    }

    private IReadOnlyList<NativeOutputTable> BuildOutputTables()
    {
        return _outputTables
            .Select((rows, index) => new NativeOutputTable(TableLabel(index), rows.Select(row => (IReadOnlyList<string>)row.ToList()).ToList()))
            .ToList();
    }

    private IReadOnlyList<NativeRunValue> BuildRunValues()
    {
        var values = new List<NativeRunValue>();
        for (int tableIndex = 0; tableIndex < _outputTables.Count; tableIndex++)
        {
            List<List<string>> table = _outputTables[tableIndex];
            for (int row = 0; row < table.Count; row++)
            {
                for (int column = 0; column < table[row].Count; column++)
                {
                    values.Add(new NativeRunValue(_traceabilityCode, TableLabel(tableIndex), tableIndex, row, column, table[row][column]));
                }
            }
        }
        return values;
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

    private static Dictionary<string, decimal> CollectNamedAmounts(List<List<string>> table)
    {
        var values = new Dictionary<string, decimal>();
        for (int row = 0; row < table.Count; row++)
        {
            for (int column = 0; column < table[row].Count; column++)
            {
                string label = table[row][column].ToLowerInvariant();
                if (column + 1 >= table[row].Count || !TryParseAmount(table[row][column + 1], out decimal amount))
                {
                    continue;
                }
                if (label.Contains("subtotal"))
                {
                    values["subtotal"] = amount;
                }
                else if (label.Contains("tax") || label.Contains("gst"))
                {
                    values["tax"] = amount;
                }
                else if (label.Contains("total"))
                {
                    values["total"] = amount;
                }
            }
        }
        return values;
    }

    private List<LearnedTemplate> LoadLearnedTemplates()
    {
        if (!File.Exists(_learnedTemplatesPath))
        {
            return new List<LearnedTemplate>();
        }
        return JsonSerializer.Deserialize<List<LearnedTemplate>>(File.ReadAllText(_learnedTemplatesPath), JsonOptions()) ?? new List<LearnedTemplate>();
    }

    private void SaveLearnedTemplates(List<LearnedTemplate> templates)
    {
        File.WriteAllText(_learnedTemplatesPath, JsonSerializer.Serialize(templates, JsonOptions()), System.Text.Encoding.UTF8);
    }

    private List<(LearnedTemplate Template, double Score)> ScoreLearnedTemplates()
    {
        string current = FingerprintCurrentSources();
        return LoadLearnedTemplates()
            .Select(template => (Template: template, Score: Similarity(current, template.Fingerprint)))
            .OrderByDescending(match => match.Score)
            .ToList();
    }

    private string FingerprintCurrentSources()
    {
        string sourceText = string.Join(" ", _sourcePreviews.Select(source => source.ParsedText));
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            sourceText = _templatePreview?.Name ?? "";
        }
        return Normalize(sourceText.Length > 2000 ? sourceText[..2000] : sourceText);
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
            // Fall back to defaults and allow the user to save fresh settings.
        }
        return new AppSettings { DatabasePath = DefaultDatabasePath() };
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
        RootGrid.RequestedTheme = _settings.Theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private string SelectedTheme()
    {
        return ThemeComboBox.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Default"
        };
    }

    private string PackageStatus()
    {
        string appBase = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string installTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "IntelliFill OCR");
        string tesseract = string.IsNullOrWhiteSpace(_settings.TesseractPath) ? "Not selected" : _settings.TesseractPath;
        return $"Version: {AppVersion}{Environment.NewLine}App folder: {appBase}{Environment.NewLine}Install/update target: {installTarget}{Environment.NewLine}App data: {_appDataPath}{Environment.NewLine}Tesseract: {tesseract}{Environment.NewLine}SQLite: {_settings.DatabasePath}";
    }

    private string DefaultDatabasePath() => Path.Combine(_appDataPath, "intellifill.sqlite3");

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static string CreateTraceabilityCode() => "IF" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

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

    private static int NormalizeRotation(int angle)
    {
        int normalized = angle % 360;
        return normalized < 0 ? normalized + 360 : normalized;
    }

    private static Uri ToFileUri(string path)
    {
        return new Uri(Path.GetFullPath(path));
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }
        return fileName;
    }

    private void TogglePanel(FrameworkElement panel, string name)
    {
        panel.Visibility = panel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        ShowStatus(InfoBarSeverity.Informational, $"{name} panel", panel.Visibility == Visibility.Visible ? "Visible" : "Hidden");
    }

    private void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath, $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never stop the UI.
        }
    }

    private static string HelpText()
    {
        return """
        IntelliFill OCR WinUI User Guide

        1. Upload Template
        Use Actions > Upload Template. Templates can be CSV, TXT, XLSX, DOCX, PDF, PNG, JPG, or JPEG. Detected tables appear in Template Upload and Output Preview. If a template has multiple tables, select each table from the dropdown.

        2. Upload Source Files
        Use Actions > Upload Source Files. Add up to five files. Parsed text appears in Uploaded Files and extracted field candidates appear in Extracted Fields.

        3. Manual Mapping
        Select an extracted field. Click a destination cell in Output Preview. Use Actions > Map Selected Field to Destination Cell. You can also edit output cells directly before saving.

        4. Intelligent Matching
        Use Actions > Auto Fill Matching Fields. The native matching engine compares labels near blank cells with source labels and fills confident matches.

        5. Mapping Templates and Template Learning
        Saved Mapping Templates store cell mappings in a JSON file. Template Learning stores reusable mappings under app data and can suggest or apply the best match later.

        6. Validation
        Actions > Run Validation Checks warns about blank required fields, GST/GSTIN format, dates, amounts, duplicate values, and invoice total mismatch.

        7. Database
        Actions > Save Filled Output to SQLite writes the current run, values, mappings, source metadata, timestamp, and traceability code to the configured SQLite database. Actions > Tools > Preview SQLite Database shows recent runs.

        8. Exports
        Actions > Export Filled Output creates CSV, XLSX, DOCX, and PDF outputs. PDF exports include the traceability barcode/code once at the bottom center.

        9. Settings
        Actions > Settings lets you select Tesseract OCR, SQLite path, and Light/Dark/Windows appearance.

        10. Panels
        Actions > Panels restores or toggles Uploaded Files, Extracted Fields, and Output Preview if a panel is hidden.
        """;
    }

    private sealed record CellAddress(int TableIndex, int RowIndex, int ColumnIndex);

    private sealed record DocumentItem(string Kind, NativeDocumentPreview Preview);

    private sealed record ExtractedField(int Id, string SourceName, string Label, string Value, double Confidence);

    private sealed record MatchCandidate(ExtractedField Field, double Score);

    private sealed record MappingSnapshot(string SourceLabel, string SourceValue, int TableIndex, int RowIndex, int ColumnIndex, string Value, string DestinationLabel);

    private sealed record MappingFile(string Version, string TemplateName, IReadOnlyList<MappingSnapshot> Mappings);

    private sealed record LearnedTemplate(string Name, DateTimeOffset UpdatedAt, string Fingerprint, IReadOnlyList<MappingSnapshot> Mappings);

    private sealed class AppSettings
    {
        public string TesseractPath { get; set; } = "";
        public string DatabasePath { get; set; } = "";
        public string Theme { get; set; } = "Default";
    }
}
