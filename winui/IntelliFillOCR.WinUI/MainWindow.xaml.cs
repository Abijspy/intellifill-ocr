using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using IntelliFillOCR.WinUI.Services;

namespace IntelliFillOCR.WinUI;

public sealed partial class MainWindow : Window
{
    private readonly PythonBackendLauncher _backendLauncher = new();

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        RefreshBackendStatus();
    }

    private async void LaunchWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        BusyRing.IsActive = true;
        try
        {
            BackendLaunchResult result = await _backendLauncher.LaunchAsync();
            ShowStatus(result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning, result.Title, result.Message);
            RefreshBackendStatus();
        }
        catch (Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Backend launch failed", ex.Message);
        }
        finally
        {
            BusyRing.IsActive = false;
        }
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

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag)
        {
            return;
        }

        string message = tag switch
        {
            "workspace" => "Workspace commands are ready.",
            "extraction" => "Native extraction screens are being migrated. Use Open OCR Workspace for the full current workflow.",
            "database" => "SQLite preview remains available in the Python workspace during the native migration.",
            "settings" => "Tesseract and SQLite settings remain available in the Python workspace during the native migration.",
            "about" => "IntelliFill OCR v3.0.0 native WinUI 3 shell.",
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
}
