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
using Avalonia.Threading;
using Avalonia.VisualTree;
using Docnet.Core;
using Docnet.Core.Models;
using IntelliFillOCR.Core;
using SkiaSharp;

namespace IntelliFillOCR.Avalonia;

public sealed partial class MainWindow : Window
{
    private const string AppVersion = "3.8.0";
    private const double PreviewBaseWidth = 1120;
    private const double PreviewBaseHeight = 760;
    private const double PreviewMinZoom = 0.5;
    private const double PreviewMaxZoom = 3.0;
    private const double PreviewZoomStep = 0.25;
    private const int ButtonAnimationDurationMs = 120;
    private const int PageAnimationDurationMs = 180;
    private const int DialogAnimationDurationMs = 150;
    private const double DialogBlurRadius = 4.0;

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
    private bool _mainButtonAnimationsAttached;
    private bool _isCheckingForUpdates;
    private DateTimeOffset _lastStatusAt = DateTimeOffset.Now;
    private string _lastUpdateCheckSummary = "Not checked in this session.";

    public MainWindow()
    {
        InitializeComponent();
        ConfigureWindowTransparency();
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
        RefreshOperationalStatus("Ready.", StatusLevel.Ready, log: false);
        RefreshReadinessReport();
        TraceabilityText.Text = $"Traceability ID: {_traceabilityCode}";
        Log("Avalonia application started.");
        Opened += async (_, _) =>
        {
            AttachMainButtonAnimations();
            _ = AnimateControlAsync(AppShell, fromOpacity: 0.96, toOpacity: 1, fromY: 8, toY: 0, PageAnimationDurationMs);
            await NotifyIfUpdateAvailableAsync();
        };
    }

    private void ConfigureWindowTransparency()
    {
        TransparencyLevelHint = new[]
        {
            WindowTransparencyLevel.Mica,
            WindowTransparencyLevel.AcrylicBlur,
            WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.None
        };
        TransparencyBackgroundFallback = DialogBrush("AppBackgroundBrush");
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

    private void RunHealthCheck_Click(object? sender, RoutedEventArgs e)
    {
        ReadinessReport report = RefreshReadinessReport(verifyWritableStorage: true);
        string message = report.Level switch
        {
            StatusLevel.Success => "System readiness check passed. OCR, storage, and local application paths are ready.",
            StatusLevel.Warning => "System readiness check completed with warnings. Review the readiness panel before production use.",
            StatusLevel.Error => "System readiness check found blocking issues. Review the readiness panel before processing documents.",
            _ => "System readiness check completed."
        };
        SetStatus(message, report.Level, refreshReadiness: false);
        RefreshReadinessReport(verifyWritableStorage: true);
    }

    private async void CheckForUpdates_Click(object? sender, RoutedEventArgs e)
    {
        if (_isCheckingForUpdates)
        {
            SetStatus("Update check is already running. Please wait for the current check to finish.", StatusLevel.Working);
            return;
        }

        Window? progressDialog = null;
        _isCheckingForUpdates = true;
        CheckForUpdatesButton.IsEnabled = false;
        CheckForUpdatesButton.Content = "Checking for Updates...";
        try
        {
            progressDialog = CreateProgressDialog("Check for Updates", "Checking GitHub releases for a newer installer...");
            progressDialog.Show(this);
            SetStatus("Checking for updates...", StatusLevel.Working);
            await Task.Delay(250);
            ReleaseUpdate latest = await GetLatestReleaseAsync();
            await CloseDialogAnimatedAsync(progressDialog);
            progressDialog = null;
            if (!IsNewerVersion(latest.Version, AppVersion))
            {
                string checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                _lastUpdateCheckSummary = $"Current. Installed v{AppVersion}; latest release v{latest.Version}; checked {checkedAt} local time.";
                SetStatus($"No updates available. Installed version v{AppVersion} is current. Latest release: v{latest.Version}. Last checked: {checkedAt} local time.", StatusLevel.Success);
                await ShowMessageAsync("Check for Updates", $"You are on the latest version ({AppVersion}).");
                return;
            }

            _lastUpdateCheckSummary = $"Update available: v{latest.Version}.";
            RefreshReadinessReport();
            await PromptForUpdateAsync(latest, isStartupNotice: false);
        }
        catch (Exception ex)
        {
            if (progressDialog is not null)
            {
                await CloseDialogAnimatedAsync(progressDialog);
                progressDialog = null;
            }
            string checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            _lastUpdateCheckSummary = $"Failed at {checkedAt} local time. Offline use remains available.";
            SetStatus("Update check failed. Offline use is still available. See logs for details.", StatusLevel.Error);
            Log("Manual update check failed: " + ex);
            await ShowMessageAsync("Check for Updates", $"Could not check GitHub releases. Offline use is still supported.{Environment.NewLine}{Environment.NewLine}{ex.Message}");
        }
        finally
        {
            if (progressDialog is not null)
            {
                await CloseDialogAnimatedAsync(progressDialog);
            }
            _isCheckingForUpdates = false;
            CheckForUpdatesButton.IsEnabled = true;
            CheckForUpdatesButton.Content = "Check for Updates";
            RefreshReadinessReport();
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
        bool pageWasVisible = page.IsVisible;

        foreach (Control candidate in pages)
        {
            candidate.IsVisible = ReferenceEquals(candidate, page);
        }

        foreach (Button button in buttons)
        {
            button.Classes.Remove("primary");
        }
        selectedButton.Classes.Add("primary");

        if (!pageWasVisible)
        {
            _ = AnimateControlAsync(page, fromOpacity: 0, toOpacity: 1, fromY: 12, toY: 0, PageAnimationDurationMs);
        }
    }

    private void AttachMainButtonAnimations()
    {
        if (_mainButtonAnimationsAttached)
        {
            return;
        }

        foreach (Button button in this.GetVisualDescendants().OfType<Button>())
        {
            AttachClickAnimation(button);
        }

        _mainButtonAnimationsAttached = true;
    }

    private void AttachClickAnimation(Button button)
    {
        button.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        button.Click -= ButtonAnimation_Click;
        button.Click += ButtonAnimation_Click;
    }

    private void ButtonAnimation_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            _ = AnimateButtonPressAsync(button);
        }
    }

    private Task AnimateButtonPressAsync(Button button)
    {
        var scale = new ScaleTransform(0.96, 0.96);
        button.RenderTransform = scale;
        return AnimateValueAsync(
            ButtonAnimationDurationMs,
            progress =>
            {
                double eased = EaseOutCubic(progress);
                double value = 0.96 + 0.04 * eased;
                scale.ScaleX = value;
                scale.ScaleY = value;
            },
            () =>
            {
                scale.ScaleX = 1;
                scale.ScaleY = 1;
            });
    }

    private Task AnimateControlAsync(Control control, double fromOpacity, double toOpacity, double fromY, double toY, int durationMs)
    {
        var transform = new TranslateTransform(0, fromY);
        control.RenderTransform = transform;
        control.Opacity = fromOpacity;
        return AnimateValueAsync(
            durationMs,
            progress =>
            {
                double eased = EaseOutCubic(progress);
                control.Opacity = fromOpacity + (toOpacity - fromOpacity) * eased;
                transform.Y = fromY + (toY - fromY) * eased;
            },
            () =>
            {
                control.Opacity = toOpacity;
                transform.Y = toY;
            });
    }

    private Task AnimateShellBlurAsync(double fromRadius, double toRadius, int durationMs)
    {
        var blur = new BlurEffect { Radius = fromRadius };
        AppShell.Effect = blur;
        return AnimateValueAsync(
            durationMs,
            progress =>
            {
                blur.Radius = fromRadius + (toRadius - fromRadius) * EaseOutCubic(progress);
            },
            () =>
            {
                blur.Radius = toRadius;
                if (toRadius <= 0.05)
                {
                    AppShell.Effect = null;
                }
            });
    }

    private static Task AnimateValueAsync(int durationMs, Action<double> apply, Action complete)
    {
        var completion = new TaskCompletionSource();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            double elapsed = (DateTimeOffset.UtcNow - start).TotalMilliseconds;
            double progress = Math.Clamp(elapsed / Math.Max(1, durationMs), 0, 1);
            apply(progress);
            if (progress < 1)
            {
                return;
            }

            timer.Stop();
            complete();
            completion.TrySetResult();
        };
        timer.Start();
        return completion.Task;
    }

    private static double EaseOutCubic(double progress)
    {
        double inverse = 1 - Math.Clamp(progress, 0, 1);
        return 1 - inverse * inverse * inverse;
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
                _lastUpdateCheckSummary = $"Update available: v{latest.Version}.";
                RefreshReadinessReport();
                await PromptForUpdateAsync(latest, isStartupNotice: true);
            }
            else
            {
                string checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                _lastUpdateCheckSummary = $"Current. Installed v{AppVersion}; latest release v{latest.Version}; checked {checkedAt} local time.";
                RefreshReadinessReport();
            }
        }
        catch (Exception ex)
        {
            string checkedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            _lastUpdateCheckSummary = $"Startup check skipped at {checkedAt} local time. Offline use remains available.";
            RefreshReadinessReport();
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
            _lastUpdateCheckSummary = $"Update available: v{latest.Version}. User deferred installation.";
            SetStatus($"Update {latest.Version} is available. Installation was deferred.", StatusLevel.Warning);
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
                _lastUpdateCheckSummary = $"Update v{latest.Version} download or launch failed.";
                SetStatus("Update install failed: " + ex.Message, StatusLevel.Error);
                Log("Update install failed: " + ex);
                await ShowMessageAsync(
                    "Update Install Failed",
                    $"The update could not be downloaded or launched automatically.{Environment.NewLine}{Environment.NewLine}{ex.Message}{Environment.NewLine}{Environment.NewLine}The GitHub release page will open so you can download the installer manually.");
                OpenUrl(latest.ReleaseUrl);
            }
            return;
        }

        OpenUrl(latest.ReleaseUrl);
        _lastUpdateCheckSummary = $"Update available: v{latest.Version}. Release page opened.";
        SetStatus("Opened release page for update " + latest.Version, StatusLevel.Warning);
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
            _lastUpdateCheckSummary = $"Update available: v{latest.Version}. No direct installer asset was available; release page opened.";
            SetStatus("Opened release page because no direct installer asset was available for this platform.", StatusLevel.Warning);
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

        _lastUpdateCheckSummary = $"Downloading update v{latest.Version}.";
        SetStatus($"Downloading IntelliFill OCR {latest.Version} installer...", StatusLevel.Working);
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
                        _lastUpdateCheckSummary = $"Downloading update v{latest.Version}: {progress}%.";
                        SetStatus($"Downloading IntelliFill OCR {latest.Version} installer... {progress}%", StatusLevel.Working);
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

        _lastUpdateCheckSummary = $"Downloaded update v{latest.Version}. Installer handoff prepared.";
        SetStatus($"Download complete. Launching update installer after IntelliFill OCR closes: {targetPath}", StatusLevel.Success);
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
        primaryButton.Click += async (_, _) => await CloseDialogAnimatedAsync(box, "primary");
        closeButton.Click += async (_, _) => await CloseDialogAnimatedAsync(box, "close");
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
        TransparencyBackgroundFallback = DialogBrush("AppBackgroundBrush");
    }

    private void ApplyThemePalette(bool isDark)
    {
        if (isDark)
        {
            SetBrush("AppBackgroundBrush", "#E0000000");
            SetBrush("ShellBrush", "#D8070707");
            SetBrush("ShellRailBrush", "#D80D0D0E");
            SetBrush("ShellCardBrush", "#E0121214");
            SetBrush("ShellBorderBrush", "#802A2A2D");
            SetBrush("PanelBrush", "#E0080808");
            SetBrush("SoftPanelBrush", "#E0111113");
            SetBrush("PreviewPanelBrush", "#E00D0D0F");
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
            SetBrush("RailButtonBrush", "#D818181B");
            SetBrush("RailButtonTextBrush", "#F4F4F5");
            SetBrush("RailButtonBorderBrush", "#333337");
            SetBrush("PreviewCanvasBrush", "#F0050505");
            SetBrush("SelectionStrokeBrush", "#22D3EE");
            SetBrush("SelectionFillBrush", "#3322D3EE");
            return;
        }

        SetBrush("AppBackgroundBrush", "#EDEAF2FF");
        SetBrush("ShellBrush", "#F2FBFCFF");
        SetBrush("ShellRailBrush", "#F0EEF4FF");
        SetBrush("ShellCardBrush", "#F0F2F5FE");
        SetBrush("ShellBorderBrush", "#DDE5F5");
        SetBrush("PanelBrush", "#F2FBFCFF");
        SetBrush("SoftPanelBrush", "#F0F2F5FE");
        SetBrush("PreviewPanelBrush", "#F2F8FAFF");
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
        SetBrush("RailButtonBrush", "#EDE8EEF9");
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
        string tesseract = string.IsNullOrWhiteSpace(_settings.TesseractPath) ? "Not configured" : _settings.TesseractPath;
        return string.Join(
            Environment.NewLine,
            $"Version: v{AppVersion}",
            $"Install folder: {AppContext.BaseDirectory}",
            $"App data: {_appDataPath}",
            $"Tesseract OCR: {tesseract}",
            $"SQLite database: {_settings.DatabasePath}",
            $"Update status: {_lastUpdateCheckSummary}",
            $"Template tables loaded: {_templateTables.Count}",
            $"Source documents loaded: {_sourcePreviews.Count}",
            $"Traceability ID: {_traceabilityCode}");
    }

    private string DefaultDatabasePath() => System.IO.Path.Combine(_appDataPath, "intellifill.sqlite3");

    private void SetStatus(string message, StatusLevel? level = null, bool refreshReadiness = true)
    {
        RefreshOperationalStatus(message, level ?? InferStatusLevel(message), log: true);
        if (refreshReadiness)
        {
            RefreshReadinessReport();
        }
    }

    private void RefreshOperationalStatus(string message, StatusLevel level, bool log)
    {
        _lastStatusAt = DateTimeOffset.Now;

        StatusBadgeText.Text = StatusLabel(level);
        StatusBadge.Background = StatusBrush(level);
        StatusBadgeText.Foreground = Brushes.White;
        StatusHeadlineText.Text = message;
        StatusUpdatedText.Text = "Updated " + _lastStatusAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        StatusText.Text = string.Join(
            Environment.NewLine,
            $"State: {StatusLabel(level)}",
            $"Last event: {message}",
            $"Last updated: {_lastStatusAt:yyyy-MM-dd HH:mm:ss zzz}",
            string.Empty,
            PackageStatus());

        if (log)
        {
            Log($"{StatusLabel(level)}: {message}");
        }
    }

    private ReadinessReport RefreshReadinessReport(bool verifyWritableStorage = false)
    {
        ReadinessReport report = BuildReadinessReport(verifyWritableStorage);
        ReadinessBox.Text = report.Text;
        ReadinessBadgeText.Text = report.Badge;
        ReadinessBadge.Background = StatusBrush(report.Level);
        ReadinessBadgeText.Foreground = Brushes.White;
        return report;
    }

    private ReadinessReport BuildReadinessReport(bool verifyWritableStorage)
    {
        var builder = new StringBuilder();
        int blockingIssues = 0;
        int warnings = 0;

        builder.AppendLine("System readiness report");
        builder.AppendLine("Generated: " + DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture));
        builder.AppendLine();

        AddReadinessLine(builder, "Application version", $"v{AppVersion}", true, "Installed version is known.", ref blockingIssues, ref warnings);
        AddReadinessLine(builder, "Install folder", AppContext.BaseDirectory, Directory.Exists(AppContext.BaseDirectory), "Folder is present.", ref blockingIssues, ref warnings);

        string appDataResult = verifyWritableStorage
            ? ProbeWritableDirectory(_appDataPath)
            : Directory.Exists(_appDataPath) ? "Folder exists." : "Folder will be created when settings are saved.";
        bool appDataReady = verifyWritableStorage
            ? appDataResult.StartsWith("Writable", StringComparison.OrdinalIgnoreCase)
            : Directory.Exists(_appDataPath);
        AddReadinessLine(builder, "App data folder", _appDataPath, appDataReady, appDataResult, ref blockingIssues, ref warnings);

        bool tesseractReady = !string.IsNullOrWhiteSpace(_settings.TesseractPath) && File.Exists(_settings.TesseractPath);
        string tesseractDetail = tesseractReady
            ? _settings.TesseractPath
            : "Not configured. Use Settings > Auto Detect or Browse before OCR region extraction.";
        AddReadinessLine(builder, "Tesseract OCR", tesseractDetail, tesseractReady, tesseractReady ? "OCR executable is available." : "OCR extraction will be blocked until configured.", ref blockingIssues, ref warnings);

        string databasePath = string.IsNullOrWhiteSpace(_settings.DatabasePath) ? DefaultDatabasePath() : _settings.DatabasePath;
        string databaseDirectory = System.IO.Path.GetDirectoryName(databasePath) ?? ".";
        string databaseResult = verifyWritableStorage
            ? ProbeWritableDirectory(databaseDirectory)
            : Directory.Exists(databaseDirectory) ? "Database folder exists." : "Database folder will be created on first save.";
        bool databaseReady = verifyWritableStorage
            ? databaseResult.StartsWith("Writable", StringComparison.OrdinalIgnoreCase)
            : Directory.Exists(databaseDirectory);
        AddReadinessLine(builder, "SQLite storage", databasePath, databaseReady, databaseResult, ref blockingIssues, ref warnings);

        AddInformationalLine(builder, "SQLite database file", File.Exists(databasePath) ? "Existing database found." : "Database will be created on first Save SQLite.");
        AddInformationalLine(builder, "Template state", _templateTables.Count > 0 ? $"{_templateTables.Count} table(s) loaded." : "No template loaded.");
        AddInformationalLine(builder, "Source state", _sourcePreviews.Count > 0 ? $"{_sourcePreviews.Count} source document(s) loaded." : "No sources loaded.");
        AddInformationalLine(builder, "Update check", _lastUpdateCheckSummary);

        StatusLevel level = blockingIssues > 0 ? StatusLevel.Error : warnings > 0 ? StatusLevel.Warning : StatusLevel.Success;
        string badge = blockingIssues > 0 ? "Action needed" : warnings > 0 ? "Review" : "Ready";
        builder.AppendLine();
        builder.AppendLine($"Summary: {blockingIssues} blocking issue(s), {warnings} warning(s).");
        return new ReadinessReport(builder.ToString(), level, badge);
    }

    private static void AddReadinessLine(StringBuilder builder, string label, string value, bool ok, string detail, ref int blockingIssues, ref int warnings)
    {
        string marker = ok ? "[OK]" : "[ACTION]";
        builder.AppendLine($"{marker} {label}");
        builder.AppendLine($"    {value}");
        builder.AppendLine($"    {detail}");
        if (!ok)
        {
            blockingIssues++;
        }
    }

    private static void AddInformationalLine(StringBuilder builder, string label, string detail)
    {
        builder.AppendLine($"[INFO] {label}");
        builder.AppendLine($"    {detail}");
    }

    private static string ProbeWritableDirectory(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);
            string testPath = System.IO.Path.Combine(directoryPath, ".intellifill-write-test");
            File.WriteAllText(testPath, DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            File.Delete(testPath);
            return "Writable and ready.";
        }
        catch (Exception ex)
        {
            return "Not writable: " + ex.Message;
        }
    }

    private static StatusLevel InferStatusLevel(string message)
    {
        string value = message.ToLowerInvariant();
        if (value.Contains("failed", StringComparison.Ordinal) ||
            value.Contains("error", StringComparison.Ordinal) ||
            value.Contains("could not", StringComparison.Ordinal))
        {
            return StatusLevel.Error;
        }

        if (value.Contains("not found", StringComparison.Ordinal) ||
            value.Contains("not auto-detected", StringComparison.Ordinal) ||
            value.Contains("select ", StringComparison.Ordinal) ||
            value.Contains("upload ", StringComparison.Ordinal) ||
            value.Contains("disabled", StringComparison.Ordinal))
        {
            return StatusLevel.Warning;
        }

        if (value.Contains("checking", StringComparison.Ordinal) ||
            value.Contains("downloading", StringComparison.Ordinal) ||
            value.Contains("enabled", StringComparison.Ordinal))
        {
            return StatusLevel.Working;
        }

        if (value.Contains("saved", StringComparison.Ordinal) ||
            value.Contains("loaded", StringComparison.Ordinal) ||
            value.Contains("complete", StringComparison.Ordinal) ||
            value.Contains("refreshed", StringComparison.Ordinal) ||
            value.Contains("current", StringComparison.Ordinal) ||
            value.Contains("exported", StringComparison.Ordinal) ||
            value.Contains("auto-detected", StringComparison.Ordinal) ||
            value.Contains("mapped", StringComparison.Ordinal) ||
            value.Contains("available", StringComparison.Ordinal))
        {
            return StatusLevel.Success;
        }

        return StatusLevel.Ready;
    }

    private static string StatusLabel(StatusLevel level) => level switch
    {
        StatusLevel.Working => "Working",
        StatusLevel.Success => "Success",
        StatusLevel.Warning => "Warning",
        StatusLevel.Error => "Error",
        _ => "Ready"
    };

    private static IBrush StatusBrush(StatusLevel level)
    {
        string color = level switch
        {
            StatusLevel.Working => "#7C3AED",
            StatusLevel.Success => "#15803D",
            StatusLevel.Warning => "#B45309",
            StatusLevel.Error => "#B91C1C",
            _ => "#2563EB"
        };
        return new SolidColorBrush(Color.Parse(color));
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
        closeButton.Click += async (_, _) => await CloseDialogAnimatedAsync(box);

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
        closeButton.Click += async (_, _) => await CloseDialogAnimatedAsync(box);
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

        var dialogContent = new Border
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
        };

        var dialog = new Window
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
            TransparencyLevelHint = new[]
            {
                WindowTransparencyLevel.AcrylicBlur,
                WindowTransparencyLevel.Blur,
                WindowTransparencyLevel.None
            },
            TransparencyBackgroundFallback = DialogBrush("AppBackgroundBrush"),
            Content = dialogContent
        };

        dialog.Opened += (_, _) =>
        {
            _ = AnimateShellBlurAsync(0, DialogBlurRadius, DialogAnimationDurationMs);
            _ = AnimateControlAsync(dialogContent, fromOpacity: 0, toOpacity: 1, fromY: 14, toY: 0, DialogAnimationDurationMs);
        };
        dialog.Closed += (_, _) =>
        {
            if (AppShell.Effect is not null)
            {
                _ = AnimateShellBlurAsync(DialogBlurRadius, 0, DialogAnimationDurationMs);
            }
        };

        return dialog;
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
        var button = new Button
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
        AttachClickAnimation(button);
        return button;
    }

    private async Task CloseDialogAnimatedAsync(Window dialog, object? result = null)
    {
        if (dialog.Content is Control content)
        {
            await AnimateControlAsync(content, fromOpacity: 1, toOpacity: 0, fromY: 0, toY: -10, DialogAnimationDurationMs);
        }

        await AnimateShellBlurAsync(DialogBlurRadius, 0, DialogAnimationDurationMs);
        if (result is null)
        {
            dialog.Close();
        }
        else
        {
            dialog.Close(result);
        }
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

        Version 3.8.0
        - Upgraded Settings with an industrial-style Application Status model that tracks state, severity, timestamp, update status, loaded template/source counts, and traceability ID.
        - Added a System Readiness panel for local OCR, SQLite storage, app data folder, template/source state, and update-check state.
        - Added Run System Readiness Check to verify writable storage paths before production document processing.
        - Hardened update checking with duplicate-click protection, disabled button state while checking, clear success/failure/deferred states, and readiness refresh after startup/manual checks.
        - Removed direct status-text bypasses so all status changes use one consistent operational pipeline.

        Version 3.7.5
        - Refreshed Application Status after a manual update check finds no newer release.
        - The status panel now records installed version, latest release version, and local checked time.
        - Update-check failures now leave a clear offline-safe status message and write details to the log.

        Version 3.7.4
        - Added Mica/Acrylic/Blur transparency hints with readable black-theme fallback surfaces.
        - Added subtle click feedback animations for buttons.
        - Added smooth page-switch animations for Template, Sources, Mapping, Review, and Settings.
        - Added styled popup open/close animations with a main-window blur treatment while dialogs are active.
        - Expanded User Guide and Feature Help into a detailed step-by-step guide for template upload, source preview, OCR region selection, mapping, validation, export, SQLite, updates, and troubleshooting.

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
        IntelliFill OCR User Guide and Feature Help

        IntelliFill OCR is an offline desktop workflow for turning document content into filled template tables. It does not send files to cloud services. OCR runs through local Tesseract, parsed data is kept locally, and saved runs are stored in your selected SQLite database.

        Quick Start
        1. Open Template and choose Upload Template.
        2. Open Sources and choose Upload Sources.
        3. Pick a source file, inspect the preview, then use zoom, rotate, and Select Region when you need OCR from a specific area.
        4. Open Mapping, select an extracted field, click a destination cell, and choose Map Selected.
        5. Use Auto Fill when the field labels are similar enough for automatic matching.
        6. Open Review, check the output, validate it, then export or save to SQLite.

        Template Page
        Upload Template accepts DOCX, PDF, PNG, JPG, JPEG, TXT, CSV, XLSX, and XLS files. The template is the document you want to fill. It can contain headings, logos, normal text, approval sections, signature areas, and one or more tables.

        Detected Tables lists every table found in the template. If the template has multiple tables, use the table selector to move between them. Export keeps all output tables in one final document, so Table 1, Table 2, and later tables are not lost.

        Template Preview shows the selected table in a read-only grid. The Output Table is a fillable copy of the template table. Blank cells and placeholder-like cells are treated as fill targets. Existing headings and labels remain unchanged unless you manually edit them.

        Sources Page
        Upload Sources accepts up to five source documents. Sources can be images, PDFs, Word documents, Excel workbooks, CSV files, or text-style files.

        The Files list shows uploaded sources. Select one file to refresh the visual preview, parsed text, extracted fields, and detected source tables.

        Large Visual Preview is the working area for visual review. For image, PDF, Word, and text-style sources, use it to inspect the document and draw OCR regions. For Excel and CSV, the app shows a table-style preview because those formats already contain structured rows and columns.

        Preview Controls
        - Zoom out and zoom in make small text easier to inspect.
        - Reset returns the preview to the default scale and rotation.
        - Rotate Left and Rotate Right help when scans were uploaded sideways.
        - Select Region starts visual selection. Drag a rectangle over the exact part of the document you want to read.
        - Extract Region OCR reads only the selected rectangle by using the local Tesseract executable configured in Settings.

        OCR Region Selection
        Use region OCR when the source is a scanned image, scanned PDF page, photo, receipt, signature area, stamp area, or a document where automatic parsing missed the required field.

        A good OCR selection is tight but not too tight. Include the label and value when possible, leave a little white space around the text, and avoid selecting unrelated columns. If OCR confidence is low, zoom in, rotate the page correctly, then select the region again.

        Parsed Text and Extracted Fields
        Parsed Text shows readable text collected from the selected source. Extracted Fields are label/value candidates that can be mapped to output cells. Confidence shows how reliable the extraction or match appears.

        Mapping Page
        Manual mapping is the most controlled workflow:
        1. Select an extracted field.
        2. Click the destination cell in the output table.
        3. Choose Map Selected.

        The selected value is copied into the destination cell. You can still edit the cell before saving or exporting.

        Auto Fill
        Auto Fill compares source labels and template labels with fuzzy matching. It is useful for common cases such as Invoice No to Invoice Number, Cust Name to Customer Name, or Date to Invoice Date. Always review the output after Auto Fill because OCR text can be imperfect.

        Template Learning
        Save reusable mappings when the same document type will be processed again. A learned template lets the app suggest future mappings faster by comparing document labels and structure. The confidence score tells you whether the suggestion is strong enough to trust or needs review.

        Validation Rules
        Validation checks for common problems before export:
        - Required fields left blank.
        - Duplicate values where duplicates are suspicious.
        - Invalid date or amount-like values.
        - GST/GSTIN-style field format problems.
        - Possible mismatch warnings when totals or key values do not look consistent.

        Validation warnings do not stop you from exporting. They are there so you can review and correct the output first.

        Review Page
        Review is the final checkpoint. It contains the traceability ID, output actions, validation results, and export preview. Use this page before saving to SQLite or creating the final document.

        Output Preview shows what will be saved or exported. PDF export preview appears in the app so you can check table readability, page breaks, and traceability barcode placement before sharing the result.

        Traceability and Barcode
        Each run has a traceability ID. PDF export places a single barcode and readable code at the bottom center of the output document. The barcode is meant for unique document identification and audit tracking. It should not appear multiple times on the same exported PDF page footer.

        Export Options
        - Export PDF creates a traceable PDF with filled tables and the bottom-center barcode.
        - Export Word creates a DOCX output for document workflows that need later editing.
        - Export Excel creates a workbook with the filled tables.
        - Export CSV creates a simple table export for lightweight sharing or import into other systems.

        Preserving Template Layout
        For structured templates, the app tries to fill blank fields while keeping headings, labels, logos, approval areas, signature areas, and table structure intact. Complex merged/split rows are preserved where the source format and export format allow it. If a template is highly customized, review the export preview before sharing the final document.

        SQLite Storage
        Save SQLite stores the run locally. Saved information includes the traceability code, source file metadata, extracted values, mappings, output values, and timestamps. Use Preview Database in Settings to inspect the local database summary.

        Settings Page
        Settings contains system and maintenance tools:
        - Tesseract OCR path: auto-detect or browse to tesseract.exe.
        - SQLite database path: choose where the local database is stored.
        - Theme: switch between default, light, and dark.
        - Run System Readiness Check: verifies local app data and database folders are writable and confirms whether Tesseract OCR is configured.
        - System Readiness: summarizes OCR readiness, SQLite readiness, loaded template/source state, and update-check state.
        - Application Status: shows the current operational state, severity, timestamp, version, paths, traceability ID, and last update result.
        - Preview Database: inspect saved runs and counts.
        - View Logs: open diagnostic logs.
        - Check for Updates: look for a newer GitHub release and launch the installer when available.
        - View Changelog: read the full version history.
        - About: see app version and branding.

        Tesseract OCR Setup
        OCR requires Tesseract installed on the machine. If OCR does not run, open Settings and choose Auto Detect Tesseract. If detection fails, browse to the executable manually. On Windows this is commonly:
        C:\Program Files\Tesseract-OCR\tesseract.exe

        Troubleshooting
        No preview appears:
        Confirm the file was uploaded and selected in the Files list. Excel and CSV files use table previews. Image, PDF, Word, and text-style files support visual preview and region selection.

        Region OCR button does nothing:
        Make sure Select Region was used and a rectangle is visible. Then confirm Tesseract OCR is installed and the path is saved in Settings.

        Text quality is poor:
        Rotate the scan, zoom in, select only the needed area, and avoid shadows or handwritten text when possible. For scanned PDFs, select the exact region instead of relying only on full-page parsing.

        Export looks wrong:
        Review the output table first, then use the in-app PDF preview. Large or complex tables may need manual cell edits before export.

        Update does not install:
        Close IntelliFill OCR before running a downloaded installer manually. Antivirus can briefly hold new installer files; wait a moment and run the installer again if Windows reports the file is busy.

        Best Practice
        Map one document type carefully, validate it, save the mapping as a reusable template, and use Auto Fill only after checking the first few runs. This gives the fastest workflow while keeping the exported documents reliable.
        """;
    }

    private sealed record CellAddress(int TableIndex, int RowIndex, int ColumnIndex);

    private sealed record DocumentItem(string Kind, DocumentPreview Preview);

    private sealed record ExtractedField(int Id, string SourceName, string Label, string Value, double Confidence);

    private sealed record MatchCandidate(ExtractedField Field, double Score);

    private sealed record MappingSnapshot(string SourceLabel, string SourceValue, int TableIndex, int RowIndex, int ColumnIndex, string Value, string DestinationLabel);

    private sealed record ReleaseUpdate(string Version, string Tag, string ReleaseUrl, string AssetName, string DownloadUrl, string Notes);

    private sealed record ReadinessReport(string Text, StatusLevel Level, string Badge);

    private enum StatusLevel
    {
        Ready,
        Working,
        Success,
        Warning,
        Error
    }

    private sealed class AppSettings
    {
        public string TesseractPath { get; set; } = string.Empty;
        public string DatabasePath { get; set; } = string.Empty;
        public string Theme { get; set; } = "Default";
    }
}
