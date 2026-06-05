using System;
using System.Diagnostics;
using System.IO;

namespace IntelliFillOCR.WinUI.Services;

public sealed record BackendLaunchResult(bool Success, string Title, string Message);
public sealed record BackendProcessStartResult(bool Success, string Message, ProcessStartInfo? StartInfo);

public sealed class PythonBackendLauncher
{
    private readonly string _applicationBaseDirectory;
    private readonly DirectoryInfo? _repositoryRoot;

    public PythonBackendLauncher()
    {
        _applicationBaseDirectory = AppContext.BaseDirectory;
        _repositoryRoot = FindRepositoryRoot(_applicationBaseDirectory);
    }

    public string GetStatusSummary()
    {
        string exePath = GetPackagedPythonExePath();
        string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "IntelliFillOCR",
            "settings.json");

        if (_repositoryRoot is null)
        {
            return
                "Running from packaged WinUI shell.\n" +
                $"Packaged backend: {(File.Exists(exePath) ? exePath : "not found")}\n" +
                $"Settings: {settingsPath}";
        }

        string modulePath = Path.Combine(_repositoryRoot.FullName, "src", "intellifill_ocr", "backend_main.py");
        string venvPython = GetVirtualEnvPythonPath();

        return
            $"Repository: {_repositoryRoot.FullName}\n" +
            $"Packaged backend: {(File.Exists(exePath) ? exePath : "not built yet")}\n" +
            $"Python backend source: {(File.Exists(modulePath) ? modulePath : "not found")}\n" +
            $"Virtual env: {(File.Exists(venvPython) ? venvPython : "not found")}\n" +
            $"Settings: {settingsPath}";
    }

    public BackendProcessStartResult CreateIpcStartInfo()
    {
        if (_repositoryRoot is not null)
        {
            string sourceMain = Path.Combine(_repositoryRoot.FullName, "src", "intellifill_ocr", "backend_main.py");
            if (File.Exists(sourceMain))
            {
                string pythonPath = File.Exists(GetVirtualEnvPythonPath()) ? GetVirtualEnvPythonPath() : "python";
                var sourceStartInfo = new ProcessStartInfo(pythonPath)
                {
                    WorkingDirectory = _repositoryRoot.FullName,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                sourceStartInfo.ArgumentList.Add("-m");
                sourceStartInfo.ArgumentList.Add("intellifill_ocr.backend_main");
                sourceStartInfo.Environment["PYTHONPATH"] = Path.Combine(_repositoryRoot.FullName, "src");
                return new BackendProcessStartResult(true, "Using source Python backend IPC.", sourceStartInfo);
            }
        }

        string exePath = GetPackagedPythonExePath();
        if (!File.Exists(exePath))
        {
            return new BackendProcessStartResult(false, "Backend IPC was not found in source or package output.", null);
        }

        var packagedStartInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? _applicationBaseDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        if (Path.GetFileName(exePath).Equals("IntelliFillOCR.exe", StringComparison.OrdinalIgnoreCase))
        {
            packagedStartInfo.ArgumentList.Add("--ipc");
        }
        return new BackendProcessStartResult(true, "Using packaged Python backend IPC.", packagedStartInfo);
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
        if (_repositoryRoot is not null)
        {
            string sourceDist = Path.Combine(_repositoryRoot.FullName, "dist", "IntelliFillOCRBackend", "IntelliFillOCRBackend.exe");
            if (File.Exists(sourceDist))
            {
                return sourceDist;
            }

            sourceDist = Path.Combine(_repositoryRoot.FullName, "dist", "IntelliFillOCR", "IntelliFillOCR.exe");
            if (File.Exists(sourceDist))
            {
                return sourceDist;
            }
        }

        string backendExe = Path.Combine(_applicationBaseDirectory, "Backend", "IntelliFillOCRBackend.exe");
        if (File.Exists(backendExe))
        {
            return backendExe;
        }

        return Path.Combine(_applicationBaseDirectory, "Backend", "IntelliFillOCR.exe");
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
