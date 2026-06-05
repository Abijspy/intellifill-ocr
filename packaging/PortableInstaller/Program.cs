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
            WaitForExistingAppToClose(options.Silent);
            ReplaceTargetWithPayload(target);
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

    private static void WaitForExistingAppToClose(bool silent)
    {
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Process[] running = Process.GetProcessesByName("IntelliFillOCR")
                .Where(process => process.Id != Environment.ProcessId)
                .ToArray();
            if (running.Length == 0)
            {
                return;
            }

            if (attempt == 0 && !silent)
            {
                foreach (Process process in running)
                {
                    try
                    {
                        process.CloseMainWindow();
                    }
                    catch
                    {
                        // Continue waiting; the update can still proceed after the app exits.
                    }
                }
            }
            Thread.Sleep(500);
        }
    }

    private static void ReplaceTargetWithPayload(string target)
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
            Directory.Move(target, oldTarget);
        }

        Directory.Move(tempTarget, target);
        DeleteDirectory(oldTarget);
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

    private sealed record Options(string? TargetPath, bool Launch, bool Silent)
    {
        public static Options Parse(string[] args)
        {
            string? target = null;
            bool launch = true;
            bool silent = false;

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
            }

            return new Options(target, launch, silent);
        }
    }
}
