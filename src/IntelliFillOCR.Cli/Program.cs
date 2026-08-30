using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Docnet.Core;
using Docnet.Core.Models;
using IntelliFillOCR.Core;
using SkiaSharp;

namespace IntelliFillOCR.Cli;

internal static partial class Program
{
    private const string Version = "6.3.4";
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "csv", "xlsx", "docx", "pdf"
    };

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                WriteHelp();
                return 0;
            }

            if (args[0] is "--version" or "-v" or "version")
            {
                Console.WriteLine($"intellifill {Version}");
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            if (command == "scan")
            {
                CliOptions options = ParseScanOptions(args[1..]);
                ScanResult result = await ScanAsync(options);
                WriteResult(result, options.Json);
                return result.ValidationIssues.Count == 0 ? 0 : 2;
            }
            return await RunExtendedCommandAsync(command, args[1..]);
        }
        catch (CliException ex)
        {
            return Fail(ex.Message);
        }
        catch (Exception ex)
        {
            if (Environment.GetEnvironmentVariable("INTELLIFILL_DEBUG") == "1") Console.Error.WriteLine(ex);
            return Fail(ex.Message);
        }
    }

    private static CliOptions ParseScanOptions(string[] args)
    {
        if (args.Length == 0)
        {
            throw new CliException("A source file is required. Example: intellifill scan invoice.pdf");
        }

        string? source = null;
        string outputDirectory = Directory.GetCurrentDirectory();
        string language = "eng";
        string? tesseract = null;
        bool json = false;
        bool noExport = false;
        var formats = new List<string> { "xlsx", "pdf" };

        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            switch (argument)
            {
                case "--output" or "-o":
                    outputDirectory = RequireValue(args, ref index, argument);
                    break;
                case "--format" or "-f":
                    formats = RequireValue(args, ref index, argument)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    break;
                case "--language" or "-l":
                    language = RequireValue(args, ref index, argument);
                    break;
                case "--tesseract":
                    tesseract = RequireValue(args, ref index, argument);
                    break;
                case "--json":
                    json = true;
                    break;
                case "--no-export":
                    noExport = true;
                    break;
                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        throw new CliException($"Unknown option '{argument}'.");
                    }
                    if (source is not null)
                    {
                        throw new CliException("Only one source file can be scanned at a time.");
                    }
                    source = argument;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new CliException("A source file is required. Example: intellifill scan invoice.pdf");
        }

        string fullSourcePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(source));
        if (!File.Exists(fullSourcePath))
        {
            throw new CliException($"Source file was not found: {fullSourcePath}");
        }

        if (!noExport && formats.Count == 0)
        {
            throw new CliException("Select at least one export format or use --no-export.");
        }

        string? unsupported = formats.FirstOrDefault(format => !SupportedFormats.Contains(format));
        if (unsupported is not null)
        {
            throw new CliException($"Unsupported export format '{unsupported}'. Use csv, xlsx, docx, or pdf.");
        }

        return new CliOptions(fullSourcePath, Path.GetFullPath(Environment.ExpandEnvironmentVariables(outputDirectory)), formats, language, tesseract, json, noExport);
    }

    private static string RequireValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
        {
            throw new CliException($"Option {option} requires a value.");
        }
        return args[index];
    }

    private static async Task<ScanResult> ScanAsync(CliOptions options)
    {
        ExtractedDocument document = await ExtractDocumentAsync(options);
        IReadOnlyList<DocumentTable> tables = document.Tables;
        int fieldCount = CountFields(tables);
        List<string> validationIssues = ValidateScan(tables, fieldCount);
        string traceabilityId = TraceabilityService.Create();
        var exportedFiles = new List<string>();

        IReadOnlyList<OutputTable> outputTables = tables
            .Select(table => new OutputTable(table.Label, table.Rows))
            .ToList();
        if (!options.NoExport && outputTables.Count > 0)
        {
            Directory.CreateDirectory(options.OutputDirectory);
            string baseName = SafeFileName(Path.GetFileNameWithoutExtension(options.SourcePath));
            var exporter = new ExportService();
            foreach (string format in options.Formats)
            {
                string path = Path.Combine(options.OutputDirectory, $"{baseName}-{traceabilityId}.{format.ToLowerInvariant()}");
                Export(exporter, outputTables, path, format, traceabilityId);
                exportedFiles.Add(path);
            }
        }

        return new ScanResult(
            Path.GetFileName(options.SourcePath),
            document.Engine,
            document.Pages,
            tables.Count,
            fieldCount,
            traceabilityId,
            validationIssues,
            exportedFiles);
    }

    private static async Task<ExtractedDocument> ExtractDocumentAsync(CliOptions options)
    {
        string extension = Path.GetExtension(options.SourcePath).ToLowerInvariant();
        bool usesOcr = extension is ".pdf" or ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp";
        string engine;
        int pages;
        IReadOnlyList<DocumentTable> tables;

        if (usesOcr)
        {
            string tesseract = ResolveTesseract(options.TesseractPath);
            IReadOnlyList<string> pageImages = extension == ".pdf"
                ? RenderPdfPages(options.SourcePath)
                : new[] { options.SourcePath };
            pages = pageImages.Count;
            var pageTables = new List<DocumentTable>();
            try
            {
                for (int index = 0; index < pageImages.Count; index++)
                {
                    string text = await RunTesseractAsync(tesseract, pageImages[index], options.Language);
                    IReadOnlyList<IReadOnlyList<string>> rows = ParseOcrRows(text);
                    if (rows.Count > 0)
                    {
                        pageTables.Add(new DocumentTable(pageImages.Count == 1 ? "Extracted fields" : $"Page {index + 1}", rows));
                    }
                }
            }
            finally
            {
                if (extension == ".pdf")
                {
                    foreach (string path in pageImages)
                    {
                        TryDelete(path);
                    }
                }
            }
            tables = pageTables;
            engine = "Tesseract / local";
        }
        else
        {
            DocumentPreview preview = new DocumentLoader().Load(options.SourcePath);
            tables = preview.Tables;
            pages = 1;
            engine = "Native document parser";
        }

        return new ExtractedDocument(engine, pages, tables);
    }

    private static void Export(ExportService exporter, IReadOnlyList<OutputTable> tables, string path, string format, string traceabilityId)
    {
        switch (format.ToLowerInvariant())
        {
            case "csv": exporter.ExportCsv(tables, path, traceabilityId); break;
            case "xlsx": exporter.ExportXlsx(tables, path, traceabilityId); break;
            case "docx": exporter.ExportDocx(tables, path, traceabilityId); break;
            case "pdf": exporter.ExportPdf(tables, path, traceabilityId); break;
            default: throw new CliException($"Unsupported export format '{format}'.");
        }
    }

    private static IReadOnlyList<string> RenderPdfPages(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var reader = DocLib.Instance.GetDocReader(bytes, new PageDimensions(2600, 3600));
        int pageCount = reader.GetPageCount();
        if (pageCount == 0)
        {
            throw new CliException("The PDF contains no readable pages.");
        }

        var paths = new List<string>(pageCount);
        try
        {
            for (int index = 0; index < pageCount; index++)
            {
                using var page = reader.GetPageReader(index);
                int width = page.GetPageWidth();
                int height = page.GetPageHeight();
                byte[] pixels = page.GetImage();
                using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
                Marshal.Copy(pixels, 0, bitmap.GetPixels(), Math.Min(pixels.Length, width * height * 4));
                using SKImage image = SKImage.FromBitmap(bitmap);
                using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
                string imagePath = Path.Combine(Path.GetTempPath(), $"intellifill-cli-{Guid.NewGuid():N}-page-{index + 1}.png");
                using FileStream output = File.Create(imagePath);
                data.SaveTo(output);
                paths.Add(imagePath);
            }
            return paths;
        }
        catch
        {
            foreach (string imagePath in paths)
            {
                TryDelete(imagePath);
            }
            throw;
        }
    }

    private static async Task<string> RunTesseractAsync(string executable, string imagePath, string language)
    {
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
        startInfo.ArgumentList.Add(language);
        startInfo.ArgumentList.Add("--psm");
        startInfo.ArgumentList.Add("6");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("preserve_interword_spaces=1");

        using Process process = Process.Start(startInfo) ?? throw new CliException("Tesseract could not be started.");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
        Task<string> errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string output = await outputTask;
        string error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new CliException($"Tesseract failed with exit code {process.ExitCode}: {error.Trim()}");
        }
        return output;
    }

    private static IReadOnlyList<IReadOnlyList<string>> ParseOcrRows(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (string rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            List<string> cells = Regex.Split(line, @"\t+|\s{2,}")
                .Select(value => Regex.Replace(value.Trim(), @"\s+", " "))
                .Where(value => value.Length > 0)
                .ToList();
            if (cells.Count == 1)
            {
                Match keyValue = Regex.Match(line, @"^([^:]{2,40}):\s*(.+)$");
                if (keyValue.Success)
                {
                    cells = new List<string> { keyValue.Groups[1].Value.Trim(), keyValue.Groups[2].Value.Trim() };
                }
            }
            rows.Add(cells);
        }
        return rows;
    }

    private static int CountFields(IReadOnlyList<DocumentTable> tables) =>
        tables.Sum(table => table.Rows.Sum(row => row.Count(value => !string.IsNullOrWhiteSpace(value))));

    private static List<string> ValidateScan(IReadOnlyList<DocumentTable> tables, int fieldCount)
    {
        var issues = new List<string>();
        if (tables.Count == 0)
        {
            issues.Add("No tables or text fields were detected.");
        }
        if (fieldCount == 0)
        {
            issues.Add("No non-empty field values were extracted.");
        }
        return issues;
    }

    private static string ResolveTesseract(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configuredPath));
            if (!File.Exists(fullPath))
            {
                throw new CliException($"Configured Tesseract executable was not found: {fullPath}");
            }
            return fullPath;
        }

        var candidates = new List<string>();
        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tesseract-OCR", "tesseract.exe"));
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Tesseract-OCR", "tesseract.exe"));
        }
        if (OperatingSystem.IsMacOS())
        {
            candidates.Add("/opt/homebrew/bin/tesseract");
            candidates.Add("/usr/local/bin/tesseract");
            candidates.Add("/opt/local/bin/tesseract");
        }
        string executableName = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            candidates.AddRange(pathValue.Split(Path.PathSeparator).Select(directory => Path.Combine(directory.Trim('"'), executableName)));
        }

        string? detected = candidates.FirstOrDefault(File.Exists);
        return detected ?? throw new CliException("Tesseract OCR was not found. Install Tesseract or pass --tesseract <path>.");
    }

    private static string SafeFileName(string value)
    {
        string safe = Regex.Replace(value, @"[^A-Za-z0-9._-]+", "-").Trim('-');
        return safe.Length == 0 ? "intellifill-output" : safe;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    private static void WriteResult(ScanResult result, bool json)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        Console.WriteLine($"intellifill scan {result.Source}");
        Console.WriteLine();
        WriteRow("Source", result.Source);
        WriteRow("OCR engine", result.Engine);
        WriteRow("Pages", $"{result.Pages} processed");
        WriteRow("Tables", $"{result.Tables} detected");
        WriteRow("Fields", $"{result.Fields} extracted");
        WriteRow("Traceability", result.TraceabilityId);
        Console.WriteLine();
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━ 100%  ✓ Review ready");
        Console.WriteLine(result.ValidationIssues.Count == 0
            ? "✓ Validation passed"
            : $"! Validation completed with {result.ValidationIssues.Count} warning(s)");
        foreach (string issue in result.ValidationIssues)
        {
            Console.WriteLine("  - " + issue);
        }
        if (result.ExportedFiles.Count > 0)
        {
            string formats = string.Join(" + ", result.ExportedFiles.Select(path => Path.GetExtension(path).TrimStart('.').ToUpperInvariant()));
            Console.WriteLine($"✓ Exported to {formats}");
            foreach (string path in result.ExportedFiles)
            {
                Console.WriteLine("  " + path);
            }
        }
    }

    private static void WriteRow(string label, string value) => Console.WriteLine($"{label,-14}{value}");

    private static int Fail(string message)
    {
        Console.Error.WriteLine("intellifill: " + message);
        return 1;
    }

    private static void WriteHelp()
    {
        Console.WriteLine($"""
        IntelliFill OCR CLI {Version}

        Usage:
          intellifill scan <file> [options]
          intellifill batch <files...> [options]
          intellifill inspect <files...> [--json]
          intellifill fill --template <file> --source <file>... [options]
          intellifill validate <file> [--json]
          intellifill history <database> [--json]
          intellifill trace-id [options]
          intellifill health [options]
          intellifill signatures <files...> [--json]
          intellifill repo status [--json]
          intellifill repo install
          intellifill update check [--json]
          intellifill --version

        Scan options:
          -o, --output <directory>       Output directory (default: current directory)
          -f, --format <formats>         Comma-separated csv,xlsx,docx,pdf (default: xlsx,pdf)
          -l, --language <code>          Tesseract language code (default: eng)
              --tesseract <path>         Explicit Tesseract executable
              --no-export               Scan and validate without creating files
              --json                    Print a machine-readable JSON result

        Examples:
          intellifill scan invoice.pdf
          intellifill scan receipt.png --format csv,xlsx --output ./exports
          intellifill scan report.docx --no-export --json
          intellifill batch scans/*.pdf --format xlsx,pdf --output ./exports
          intellifill fill --template template.xlsx --source invoice.pdf --save-db runs.db
          intellifill trace-id --mode prefix-random --value ACME-
          intellifill health --tesseract /usr/bin/tesseract --database runs.db
        """);
    }

    private sealed record CliOptions(
        string SourcePath,
        string OutputDirectory,
        IReadOnlyList<string> Formats,
        string Language,
        string? TesseractPath,
        bool Json,
        bool NoExport);

    private sealed record ScanResult(
        string Source,
        string Engine,
        int Pages,
        int Tables,
        int Fields,
        string TraceabilityId,
        IReadOnlyList<string> ValidationIssues,
        IReadOnlyList<string> ExportedFiles);

    private sealed record ExtractedDocument(string Engine, int Pages, IReadOnlyList<DocumentTable> Tables);

    private sealed class CliException(string message) : Exception(message);
}
