using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace IntelliFillOCR.PortableInstaller;

internal static class Program
{
    private const string ResourceName = "IntelliFillOCR.Payload.zip";

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Options options = Options.Parse(args);
            string target = options.TargetPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "IntelliFill OCR");

            Directory.CreateDirectory(Path.GetDirectoryName(target) ?? ".");
            WaitForExistingAppToClose(target, options);
            ReplaceTargetWithPayload(target, options);
            WriteInstallMetadata(target);
            CreateStartMenuShortcut(target);

            if (options.Launch)
            {
                string appExe = Path.Combine(target, "IntelliFillOCR.exe");
                Process.Start(new ProcessStartInfo(appExe) { WorkingDirectory = target, UseShellExecute = true });
            }

            return 0;
        }
        catch (Exception ex)
        {
            ShowMessage("IntelliFill OCR install/update failed", ex.ToString());
            return 1;
        }
    }

    private static void WaitForExistingAppToClose(string target, Options options)
    {
        if (options.WaitProcessId is int waitProcessId && waitProcessId != Environment.ProcessId)
        {
            WaitForProcessIdToExit(waitProcessId, target, options);
        }

        Process[] running = FindRunningAppProcesses(target);
        if (running.Length == 0)
        {
            return;
        }

        RequestProcessesToClose(running);
        if (WaitForProcessesToExit(running, TimeSpan.FromSeconds(20)))
        {
            return;
        }

        running = FindRunningAppProcesses(target);
        if (running.Length == 0)
        {
            return;
        }

        if (!options.Silent && !ConfirmForceClose(running.Length))
        {
            throw new IOException("IntelliFill OCR is still running. Close the app and run the updater again.");
        }

        ForceCloseProcesses(running);
        if (!WaitForProcessesToExit(running, TimeSpan.FromSeconds(20)))
        {
            throw new IOException("IntelliFill OCR is still running and could not be closed. Close it from Task Manager and run the updater again.");
        }
    }

    private static void WaitForProcessIdToExit(int processId, string target, Options options)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            if (!IsProcessFromTarget(process, target))
            {
                return;
            }

            if (process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds))
            {
                return;
            }

            RequestProcessesToClose(new[] { process });
            if (process.WaitForExit((int)TimeSpan.FromSeconds(20).TotalMilliseconds))
            {
                return;
            }

            if (!options.Silent && !ConfirmForceClose(1))
            {
                throw new IOException("IntelliFill OCR is still running. Close the app and run the updater again.");
            }

            ForceCloseProcesses(new[] { process });
            process.WaitForExit((int)TimeSpan.FromSeconds(20).TotalMilliseconds);
        }
        catch (ArgumentException)
        {
            // The process has already exited.
        }
    }

    private static void ReplaceTargetWithPayload(string target, Options options)
    {
        string tempTarget = target + ".new";
        string oldTarget = target + ".old";
        DeleteDirectory(tempTarget);
        Directory.CreateDirectory(tempTarget);

        using Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded IntelliFill OCR payload was not found.");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read);
        string? rootPrefix = DetectRootPrefix(archive);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string relativePath = entry.FullName.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(rootPrefix) && relativePath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath[rootPrefix.Length..];
            }
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                continue;
            }

            string destination = Path.Combine(tempTarget, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
        }

        DeleteDirectory(oldTarget);
        if (Directory.Exists(target))
        {
            RunFileSystemStep(
                () => Directory.Move(target, oldTarget),
                target,
                options,
                "prepare the existing installation for update");
        }

        RunFileSystemStep(
            () => Directory.Move(tempTarget, target),
            target,
            options,
            "activate the updated installation");
        TryDeleteDirectory(oldTarget);
    }

    private static string? DetectRootPrefix(ZipArchive archive)
    {
        string[] firstSegments = archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(path => path.Contains('/'))
            .Select(path => path[..(path.IndexOf('/') + 1)])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return firstSegments.Length == 1 ? firstSegments[0] : null;
    }

    private static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (int attempt = 0; attempt < 12; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch when (attempt < 11)
            {
                Thread.Sleep(400);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            DeleteDirectory(path);
        }
        catch
        {
            // The old folder is no longer active. A future update can clean it if Windows releases it later.
        }
    }

    private static void RunFileSystemStep(Action action, string target, Options options, string description)
    {
        Exception? lastError = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                WaitForExistingAppToClose(target, options);
                Thread.Sleep(500 + (attempt * 250));
            }
        }

        throw new IOException(
            $"Could not {description}. Close IntelliFill OCR and any file explorer windows opened inside the install folder, then run the updater again.",
            lastError);
    }

    private static Process[] FindRunningAppProcesses(string target)
    {
        return Process.GetProcessesByName("IntelliFillOCR")
            .Where(process => process.Id != Environment.ProcessId && IsProcessFromTarget(process, target))
            .ToArray();
    }

    private static bool IsProcessFromTarget(Process process, string target)
    {
        try
        {
            string targetRoot = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string? processPath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return true;
            }

            string fullProcessPath = Path.GetFullPath(processPath);
            return fullProcessPath.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }

    private static void RequestProcessesToClose(Process[] processes)
    {
        foreach (Process process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
                // Continue with wait/force-close fallback.
            }
        }
    }

    private static bool WaitForProcessesToExit(Process[] processes, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (processes.All(process => HasExited(process)))
            {
                return true;
            }

            Thread.Sleep(250);
        }

        return processes.All(process => HasExited(process));
    }

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch
        {
            return true;
        }
    }

    private static void ForceCloseProcesses(Process[] processes)
    {
        foreach (Process process in processes)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // The final wait will report a clear error if the process remains alive.
            }
        }
    }

    private static void WriteInstallMetadata(string target)
    {
        var metadata = new
        {
            app = "IntelliFill OCR",
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            installedAt = DateTimeOffset.Now,
            installPath = target,
            packageType = "portable-exe"
        };
        File.WriteAllText(Path.Combine(target, "portable-install.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void CreateStartMenuShortcut(string target)
    {
        try
        {
            string shortcutDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs");
            Directory.CreateDirectory(shortcutDir);
            string shortcutPath = Path.Combine(shortcutDir, "IntelliFill OCR.lnk");
            string appExe = Path.Combine(target, "IntelliFillOCR.exe");
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                return;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = appExe;
            shortcut.WorkingDirectory = target;
            shortcut.IconLocation = appExe;
            shortcut.Save();
        }
        catch
        {
            // A shortcut is helpful, but the portable app is still usable without it.
        }
    }

    private static void ShowMessage(string title, string message)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic? shell = shellType is null ? null : Activator.CreateInstance(shellType);
            shell?.Popup(message, 0, title, 16);
        }
        catch
        {
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "IntelliFillOCR-portable-installer-error.txt"), message);
        }
    }

    private static bool ConfirmForceClose(int processCount)
    {
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic? shell = shellType is null ? null : Activator.CreateInstance(shellType);
            int result = shell?.Popup(
                $"IntelliFill OCR is still running ({processCount} process(es)). The updater must close it before replacing files.\n\nClick OK to close IntelliFill OCR and continue, or Cancel to stop.",
                0,
                "Close IntelliFill OCR to update",
                49) ?? 2;
            return result == 1;
        }
        catch
        {
            return false;
        }
    }

    private sealed record Options(string? TargetPath, bool Launch, bool Silent, int? WaitProcessId)
    {
        public static Options Parse(string[] args)
        {
            string? target = null;
            bool launch = true;
            bool silent = false;
            int? waitProcessId = null;

            foreach (string arg in args)
            {
                if (arg.Equals("--no-launch", StringComparison.OrdinalIgnoreCase))
                {
                    launch = false;
                }
                else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) || arg.Equals("/silent", StringComparison.OrdinalIgnoreCase))
                {
                    silent = true;
                }
                else if (arg.StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
                {
                    target = arg["--target=".Length..].Trim('"');
                }
                else if (arg.StartsWith("--wait-pid=", StringComparison.OrdinalIgnoreCase) &&
                         int.TryParse(arg["--wait-pid=".Length..].Trim('"'), out int parsedProcessId))
                {
                    waitProcessId = parsedProcessId;
                }
            }

            return new Options(target, launch, silent, waitProcessId);
        }
    }
}
