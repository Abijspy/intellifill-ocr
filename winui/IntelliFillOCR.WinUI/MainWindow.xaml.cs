using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
    private readonly PythonBackendLauncher _backendLauncher = new();
    private readonly PythonBackendIpcSession _backendIpcSession;
    private readonly List<TemplatePreviewTable> _templateTables = new();

    public MainWindow()
    {
        InitializeComponent();
        _backendIpcSession = new PythonBackendIpcSession(_backendLauncher);
        ExtendsContentIntoTitleBar = true;
        Closed += (_, _) => _backendIpcSession.Dispose();
        RefreshBackendStatus();
    }

    private void OpenRepositoryButton_Click(object sender, RoutedEventArgs e)
    {
        BackendLaunchResult result = _backendLauncher.OpenRepositoryFolder();
        ShowStatus(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning, result.Title, result.Message);
    }

    private void OpenPythonSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        BackendLaunchResult result = _backendLauncher.OpenAppDataFolder();
        ShowStatus(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning, result.Title, result.Message);
    }

    private void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshBackendStatus();
        ShowStatus(InfoBarSeverity.Informational, "Status refreshed", "Backend paths were checked again.");
    }

    private async void TestIpcButton_Click(object sender, RoutedEventArgs e)
    {
        BusyRing.IsActive = true;
        try
        {
            BackendIpcResult result = await _backendIpcSession.PingAsync();
            ShowStatus(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning, result.Title, result.Message);
            RefreshBackendStatus();
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "IPC test failed", ex.Message);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
    }

    private async void UploadTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        foreach (string extension in new[] { ".csv", ".xlsx", ".xls", ".docx", ".pdf", ".png", ".jpg", ".jpeg" })
        {
            picker.FileTypeFilter.Add(extension);
        }

        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        Windows.Storage.StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
        {
            return;
        }

        TemplateProgressRing.IsActive = true;
        BusyRing.IsActive = true;
        try
        {
            TemplatePathTextBox.Text = file.Path;
            BackendIpcResult result = await _backendIpcSession.InvokeAsync(
                "template.upload",
                new Dictionary<string, object?> { ["path"] = file.Path });
            if (!result.Success)
            {
                ShowStatus(InfoBarSeverity.Error, result.Title, result.Message);
                return;
            }

            LoadTemplatePreview(result.ResponseJson, file.Path);
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
            "workspace" => "Workspace commands are ready.",
            "template" => "Native template upload and preview are available in this WinUI shell.",
            "extraction" => "Native source upload, OCR region selection, and mapping screens are next.",
            "database" => "Native SQLite preview will be migrated after extraction and mapping.",
            "settings" => "Native Tesseract and SQLite settings are planned for the WinUI shell.",
            "about" => "IntelliFill OCR v3.1.0 native WinUI 3 shell.",
            _ => "Ready."
        };
        ShowStatus(InfoBarSeverity.Informational, item.Content?.ToString() ?? "IntelliFill OCR", message);
    }

    private void RefreshBackendStatus()
    {
        BackendStatusText.Text = _backendLauncher.GetStatusSummary();
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    private void LoadTemplatePreview(string responseJson, string selectedPath)
    {
        using JsonDocument document = JsonDocument.Parse(responseJson);
        JsonElement template = document.RootElement.GetProperty("result").GetProperty("template");
        string templateName = template.TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString() ?? Path.GetFileNameWithoutExtension(selectedPath)
            : Path.GetFileNameWithoutExtension(selectedPath);

        _templateTables.Clear();
        TemplateTableSelector.Items.Clear();

        foreach (JsonElement tableElement in template.GetProperty("tables").EnumerateArray())
        {
            string label = tableElement.TryGetProperty("label", out JsonElement labelElement)
                ? labelElement.GetString() ?? "Table"
                : "Table";
            int rowCount = tableElement.TryGetProperty("row_count", out JsonElement rowCountElement)
                ? rowCountElement.GetInt32()
                : 0;
            int columnCount = tableElement.TryGetProperty("column_count", out JsonElement columnCountElement)
                ? columnCountElement.GetInt32()
                : 0;
            var rows = new List<List<string>>();
            foreach (JsonElement rowElement in tableElement.GetProperty("cells").EnumerateArray())
            {
                rows.Add(rowElement.EnumerateArray()
                    .Select(cell => cell.TryGetProperty("value", out JsonElement valueElement) ? valueElement.GetString() ?? "" : "")
                    .ToList());
            }

            var table = new TemplatePreviewTable(label, rowCount, columnCount, rows);
            _templateTables.Add(table);
            TemplateTableSelector.Items.Add($"{label} ({rowCount} x {columnCount})");
        }

        TemplateSummaryText.Text = $"{templateName}: {_templateTables.Count} table(s) detected from {Path.GetFileName(selectedPath)}.";
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

        TemplatePreviewTable table = _templateTables[selectedIndex];
        int rowCount = Math.Max(table.Cells.Count, table.RowCount);
        int columnCount = Math.Max(table.ColumnCount, table.Cells.Select(row => row.Count).DefaultIfEmpty(0).Max());
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
            for (int column = 0; column < columnCount; column++)
            {
                string value = row < table.Cells.Count && column < table.Cells[row].Count ? table.Cells[row][column] : "";
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

    private sealed record TemplatePreviewTable(string Label, int RowCount, int ColumnCount, List<List<string>> Cells);
}
