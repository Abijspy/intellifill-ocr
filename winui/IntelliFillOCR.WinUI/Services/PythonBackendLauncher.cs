using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace IntelliFillOCR.WinUI.Services;

public sealed record BackendLaunchResult(bool Success, string Title, string Message);

public sealed class PythonBackendLauncher
{
    private readonly DirectoryInfo? _repositoryRoot;

    public PythonBackendLauncher()
    {
        _repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
    }

    public string GetStatusSummary()
    {
        if (_repositoryRoot is null)
        {
            return "Repository root was not found. Build or run from inside the IntelliFill OCR source tree.";
        }

        string exePath = GetPackagedPythonExePath();
        string modulePath = Path.Combine(_repositoryRoot.FullName, "src", "intellifill_ocr", "main.py");
        string venvPython = GetVirtualEnvPythonPath();
        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelliFillOCR",
            "settings.json");

        return
            $"Repository: {_repositoryRoot.FullName}\n" +
            $"Packaged app: {(File.Exists(exePath) ? exePath : "not built yet")}\n" +
            $"Python source: {(File.Exists(modulePath) ? modulePath : "not found")}\n" +
            $"Virtual env: {(File.Exists(venvPython) ? venvPython : "not found")}\n" +
            $"Settings: {settingsPath}";
    }

    public Task<BackendLaunchResult> LaunchAsync()
    {
        if (_repositoryRoot is null)
        {
            return Task.FromResult(new BackendLaunchResult(false, "Repository not found", "Run this WinUI shell from the IntelliFill OCR source tree or packaged install folder."));
        }

        string exePath = GetPackagedPythonExePath();
        if (File.Exists(exePath))
        {
            StartProcess(new ProcessStartInfo(exePath)
            {
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? _repositoryRoot.FullName,
                UseShellExecute = true
            });
            return Task.FromResult(new BackendLaunchResult(true, "OCR workspace opened", "Started the packaged Python OCR workspace."));
        }

        string sourceMain = Path.Combine(_repositoryRoot.FullName, "src", "intellifill_ocr", "main.py");
        if (!File.Exists(sourceMain))
        {
            return Task.FromResult(new BackendLaunchResult(false, "Python backend missing", "The Python source backend was not found under src/intellifill_ocr."));
        }

        string pythonPath = File.Exists(GetVirtualEnvPythonPath()) ? GetVirtualEnvPythonPath() : "python";
        var startInfo = new ProcessStartInfo(pythonPath)
        {
            WorkingDirectory = _repositoryRoot.FullName,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("intellifill_ocr.main");
        startInfo.Environment["PYTHONPATH"] = Path.Combine(_repositoryRoot.FullName, "src");

        StartProcess(startInfo);
        return Task.FromResult(new BackendLaunchResult(true, "OCR workspace opened", "Started the Python OCR workspace from source."));
    }

    public BackendLaunchResult OpenRepositoryFolder()
    {
        if (_repositoryRoot is null)
        {
            return new BackendLaunchResult(false, "Repository not found", "The source folder could not be located.");
        }

        OpenFolder(_repositoryRoot.FullName);
        return new BackendLaunchResult(true, "Project folder opened", _repositoryRoot.FullName);
    }

    public BackendLaunchResult OpenAppDataFolder()
    {
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelliFillOCR");
        Directory.CreateDirectory(appData);
        OpenFolder(appData);
        return new BackendLaunchResult(true, "App data opened", appData);
    }

    private string GetPackagedPythonExePath()
    {
        if (_repositoryRoot is null)
        {
            return string.Empty;
        }

        string sourceDist = Path.Combine(_repositoryRoot.FullName, "dist", "IntelliFillOCR", "IntelliFillOCR.exe");
        if (File.Exists(sourceDist))
        {
            return sourceDist;
        }

        return Path.Combine(AppContext.BaseDirectory, "Backend", "IntelliFillOCR.exe");
    }

    private string GetVirtualEnvPythonPath()
    {
        if (_repositoryRoot is null)
        {
            return string.Empty;
        }

        return Path.Combine(_repositoryRoot.FullName, ".venv", "Scripts", "python.exe");
    }

    private static DirectoryInfo? FindRepositoryRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src", "intellifill_ocr")) &&
                File.Exists(Path.Combine(current.FullName, "pyproject.toml")))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void OpenFolder(string path)
    {
        StartProcess(new ProcessStartInfo("explorer.exe", path)
        {
            UseShellExecute = true
        });
    }

    private static void StartProcess(ProcessStartInfo startInfo)
    {
        Process.Start(startInfo);
    }
}
