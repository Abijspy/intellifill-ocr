using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using IntelliFillOCR.WinUI.Services;
using Windows.Storage.Pickers;

namespace IntelliFillOCR.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly NativeTemplateLoader _loader = new();
    private readonly List<NativeDocumentTable> _templateTables = new();
    private readonly List<NativeDocumentPreview> _sourcePreviews = new();

    public MainWindow()
    {
        InitializeComponent();
        PackageStatusText.Text = PackageStatus();
    }

    private void OpenAppDataButton_Click(object sender, RoutedEventArgs e)
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelliFillOCR");
        Directory.CreateDirectory(appData);
        Process.Start(new ProcessStartInfo("explorer.exe", appData) { UseShellExecute = true });
        ShowStatus(InfoBarSeverity.Success, "App data opened", appData);
    }

    private void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
    {
        PackageStatusText.Text = PackageStatus();
        ShowStatus(InfoBarSeverity.Informational, "Status refreshed", "Package paths were checked again.");
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
            TemplatePathTextBox.Text = file.Path;
            LoadTemplatePreview(preview);
            ShowStatus(InfoBarSeverity.Success, "Template loaded", Path.GetFileName(file.Path));
        }
        catch (Exception ex)
        {
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
            SourcesListView.Items.Clear();
            foreach (Windows.Storage.StorageFile file in files.Take(5))
            {
                NativeDocumentPreview preview = _loader.LoadManyText(file.Path);
                _sourcePreviews.Add(preview);
                SourcesListView.Items.Add($"{Path.GetFileName(file.Path)}  -  {preview.Tables.Count} table(s)");
            }

            ParsedTextBox.Text = string.Join(
                Environment.NewLine + Environment.NewLine,
                _sourcePreviews.Select(preview => $"[{preview.Name}]{Environment.NewLine}{preview.ParsedText}"));
            ShowStatus(InfoBarSeverity.Success, "Sources loaded", $"{_sourcePreviews.Count} source file(s) parsed natively.");
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Source upload failed", ex.Message);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private void TemplateTableSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RenderSelectedTemplateTable();
    }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        string message = tag switch
        {
            "workspace" => "Native WinUI package is ready.",
            "template" => "Upload and preview template tables directly in WinUI.",
            "extraction" => "Upload source files and review parsed text.",
            "database" => "SQLite storage will be implemented inside the native WinUI engine next.",
            "settings" => "Tesseract and export settings will be native WinUI settings.",
            "about" => "IntelliFill OCR v3.2.0 native WinUI package.",
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

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        return picker;
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
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
        if (_templateTables.Count > 0)
        {
            TemplateTableSelector.SelectedIndex = 0;
        }
        else
        {
            TemplatePreviewGrid.Children.Clear();
            TemplatePreviewGrid.RowDefinitions.Clear();
            TemplatePreviewGrid.ColumnDefinitions.Clear();
        }
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
        int rowCount = table.RowCount;
        int columnCount = table.ColumnCount;
        if (rowCount == 0 || columnCount == 0)
        {
            TemplateSummaryText.Text = $"{table.Label}: no cells detected.";
            return;
        }

        TemplatePreviewGrid.MinWidth = Math.Max(640, columnCount * 160);
        for (int column = 0; column < columnCount; column++)
        {
            TemplatePreviewGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        }
        for (int row = 0; row < rowCount; row++)
        {
            TemplatePreviewGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        var borderBrush = new SolidColorBrush(Colors.Gray);
        var headerBrush = new SolidColorBrush(ColorHelper.FromArgb(36, 0, 120, 212));
        var emptyBrush = new SolidColorBrush(ColorHelper.FromArgb(20, 220, 38, 38));
        for (int row = 0; row < rowCount; row++)
        {
            IReadOnlyList<string> values = table.Rows[row];
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
                    Background = row == 0 ? headerBrush : string.IsNullOrWhiteSpace(value) ? emptyBrush : null,
                    MinHeight = 36,
                    Padding = new Thickness(8, 6, 8, 6),
                    Child = text
                };
                Grid.SetRow(border, row);
                Grid.SetColumn(border, column);
                TemplatePreviewGrid.Children.Add(border);
            }
        }

        TemplateSummaryText.Text = $"{table.Label}: {rowCount} rows and {columnCount} columns.";
    }

    private static string PackageStatus()
    {
        string appBase = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelliFillOCR");
        string installTarget = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "IntelliFill OCR");
        return $"Version: 3.2.0{Environment.NewLine}App folder: {appBase}{Environment.NewLine}Install/update target: {installTarget}{Environment.NewLine}App data: {appData}";
    }
}
