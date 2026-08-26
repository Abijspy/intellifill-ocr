using System.Globalization;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using IntelliFillOCR.Core;

namespace IntelliFillOCR.Cli;

internal static partial class Program
{
    private static async Task<int> RunExtendedCommandAsync(string command, string[] args) => command switch
    {
        "batch" => await RunBatchAsync(args),
        "inspect" => await RunInspectAsync(args),
        "fill" => await RunFillAsync(args),
        "validate" => await RunValidateAsync(args),
        "history" => RunHistory(args),
        "trace-id" => RunTraceId(args),
        "health" => RunHealth(args),
        "signatures" => RunSignatures(args),
        "repo" => RunRepository(args),
        "update" => await RunUpdateAsync(args),
        _ => Fail($"Unknown command '{command}'. Run 'intellifill --help' for usage.")
    };

    private static async Task<int> RunBatchAsync(string[] args)
    {
        (List<string> files, List<string> options) = SplitFilesAndOptions(args);
        if (files.Count == 0) throw new CliException("batch requires one or more source files.");
        bool json = options.Contains("--json");
        var results = new List<ScanResult>();
        int exitCode = 0;
        foreach (string file in files)
        {
            CliOptions parsed = ParseScanOptions(new[] { file }.Concat(options).ToArray());
            ScanResult result = await ScanAsync(parsed);
            results.Add(result);
            if (result.ValidationIssues.Count > 0) exitCode = 2;
            if (!json) WriteResult(result, false);
        }
        if (json) Console.WriteLine(JsonSerializer.Serialize(results, JsonIndented));
        return exitCode;
    }

    private static async Task<int> RunInspectAsync(string[] args)
    {
        bool json = args.Contains("--json");
        string language = Option(args, "--language", "-l") ?? "eng";
        string? tesseract = Option(args, "--tesseract");
        List<string> files = Positional(args, "--json", "--language", "-l", "--tesseract");
        if (files.Count == 0) throw new CliException("inspect requires one or more files.");
        var reports = new List<object>();
        foreach (string file in files)
        {
            string path = ExistingFile(file);
            var options = new CliOptions(path, Directory.GetCurrentDirectory(), Array.Empty<string>(), language, tesseract, json, true);
            ExtractedDocument document = await ExtractDocumentAsync(options);
            var preview = new DocumentPreview(Path.GetFileNameWithoutExtension(path), path,
                string.Join(Environment.NewLine, document.Tables.SelectMany(t => t.Rows).Select(r => string.Join(" | ", r))), document.Tables);
            IReadOnlyList<ExtractedField> fields = new AutomationService().ExtractFields(preview);
            reports.Add(new { File = path, document.Engine, document.Pages, Tables = document.Tables.Count, Fields = fields });
            if (!json)
            {
                Console.WriteLine(Path.GetFileName(path));
                WriteRow("Engine", document.Engine);
                WriteRow("Pages", document.Pages.ToString(CultureInfo.InvariantCulture));
                WriteRow("Tables", document.Tables.Count.ToString(CultureInfo.InvariantCulture));
                foreach (ExtractedField field in fields) Console.WriteLine($"  {field.Label}: {field.Value} ({field.Confidence:P0})");
                Console.WriteLine();
            }
        }
        if (json) Console.WriteLine(JsonSerializer.Serialize(reports, JsonIndented));
        return 0;
    }

    private static async Task<int> RunFillAsync(string[] args)
    {
        string templatePath = ExistingFile(RequiredOption(args, "--template"));
        List<string> sourcePaths = RepeatedOption(args, "--source").Select(ExistingFile).Take(5).ToList();
        if (sourcePaths.Count == 0) throw new CliException("fill requires at least one --source file (maximum five)." );
        string output = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Option(args, "--output", "-o") ?? Directory.GetCurrentDirectory()));
        List<string> formats = (Option(args, "--format", "-f") ?? "xlsx,pdf").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        string? unsupported = formats.FirstOrDefault(format => !SupportedFormats.Contains(format));
        if (unsupported is not null) throw new CliException($"Unsupported export format '{unsupported}'.");
        string language = Option(args, "--language", "-l") ?? "eng";
        string? tesseract = Option(args, "--tesseract");
        bool json = args.Contains("--json");
        bool noExport = args.Contains("--no-export");
        double threshold = double.TryParse(Option(args, "--threshold"), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0.42;
        string traceability = Option(args, "--trace-id") ?? TraceabilityService.Create(Option(args, "--trace-mode") ?? "timestamp", Option(args, "--trace-value"));

        DocumentPreview template = new DocumentLoader().Load(templatePath);
        var sources = new List<DocumentPreview>();
        foreach (string source in sourcePaths)
        {
            ExtractedDocument extracted = await ExtractDocumentAsync(new CliOptions(source, output, Array.Empty<string>(), language, tesseract, json, true));
            sources.Add(new DocumentPreview(Path.GetFileNameWithoutExtension(source), source,
                string.Join(Environment.NewLine, extracted.Tables.SelectMany(t => t.Rows).Select(r => string.Join(" | ", r))), extracted.Tables));
        }
        List<CellOverride> overrides = RepeatedOption(args, "--set").Select(ParseOverride).ToList();
        FillResult result = new AutomationService().FillTemplate(template, sources, threshold, overrides);
        var exported = new List<string>();
        if (!noExport)
        {
            Directory.CreateDirectory(output);
            var exporter = new ExportService();
            foreach (string format in formats)
            {
                string path = Path.Combine(output, $"{SafeFileName(Path.GetFileNameWithoutExtension(templatePath))}-{traceability}.{format}");
                Export(exporter, result.Tables, path, format, traceability);
                exported.Add(path);
            }
        }

        string? database = Option(args, "--save-db");
        if (!string.IsNullOrWhiteSpace(database))
        {
            string databasePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(database));
            IReadOnlyList<RunValue> values = result.Tables.SelectMany((table, tableIndex) => table.Rows.SelectMany((row, rowIndex) =>
                row.Select((value, columnIndex) => new RunValue(traceability, table.Label, tableIndex, rowIndex, columnIndex, value)))).ToList();
            new DatabaseService().SaveRun(databasePath, traceability, templatePath, sourcePaths, values,
                result.Mappings.Select(m => $"{m.DestinationLabel} <- {m.SourceLabel}: {m.SourceValue}").ToList());
        }

        var report = new { Template = templatePath, Sources = sourcePaths, TraceabilityId = traceability, result.Mappings, result.ValidationIssues, ExportedFiles = exported, Database = database };
        if (json) Console.WriteLine(JsonSerializer.Serialize(report, JsonIndented));
        else
        {
            Console.WriteLine($"Filled {result.Mappings.Count} template cell(s) from {sourcePaths.Count} source file(s).");
            WriteRow("Traceability", traceability);
            WriteRow("Validation", result.ValidationIssues.Count == 0 ? "passed" : $"{result.ValidationIssues.Count} warning(s)");
            foreach (FieldMapping mapping in result.Mappings) Console.WriteLine($"  {mapping.DestinationLabel} <- {mapping.SourceLabel} ({mapping.Score:P0})");
            foreach (string path in exported) Console.WriteLine("  Exported: " + path);
            if (database is not null) Console.WriteLine("  Saved SQLite: " + Path.GetFullPath(database));
        }
        return result.ValidationIssues.Count == 0 ? 0 : 2;
    }

    private static async Task<int> RunValidateAsync(string[] args)
    {
        bool json = args.Contains("--json");
        List<string> files = Positional(args, "--json", "--language", "-l", "--tesseract");
        if (files.Count != 1) throw new CliException("validate requires exactly one file.");
        string path = ExistingFile(files[0]);
        ExtractedDocument document = await ExtractDocumentAsync(new CliOptions(path, Directory.GetCurrentDirectory(), Array.Empty<string>(), Option(args, "--language", "-l") ?? "eng", Option(args, "--tesseract"), json, true));
        IReadOnlyList<OutputTable> tables = document.Tables.Select(t => new OutputTable(t.Label, t.Rows)).ToList();
        IReadOnlyList<string> issues = new AutomationService().Validate(tables);
        if (json) Console.WriteLine(JsonSerializer.Serialize(new { File = path, Issues = issues }, JsonIndented));
        else
        {
            Console.WriteLine(issues.Count == 0 ? "Validation passed." : $"Validation found {issues.Count} warning(s):");
            foreach (string issue in issues) Console.WriteLine("  - " + issue);
        }
        return issues.Count == 0 ? 0 : 2;
    }

    private static int RunHistory(string[] args)
    {
        bool json = args.Contains("--json");
        List<string> paths = args.Where(a => !a.StartsWith('-')).ToList();
        if (paths.Count != 1) throw new CliException("history requires one SQLite database path.");
        string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(paths[0]));
        string preview = new DatabaseService().Preview(path);
        Console.WriteLine(json ? JsonSerializer.Serialize(new { Database = path, Preview = preview }, JsonIndented) : preview);
        return 0;
    }

    private static int RunTraceId(string[] args)
    {
        string id = TraceabilityService.Create(Option(args, "--mode") ?? "timestamp", Option(args, "--value"));
        Console.WriteLine(args.Contains("--json") ? JsonSerializer.Serialize(new { TraceabilityId = id }, JsonIndented) : id);
        return 0;
    }

    private static int RunHealth(string[] args)
    {
        string? configuredTesseract = Option(args, "--tesseract");
        string database = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Option(args, "--database") ?? Path.Combine(DefaultAppData(), "intellifill.db")));
        string? tesseract = null;
        try { tesseract = ResolveTesseract(configuredTesseract); } catch { }
        string databaseDirectory = Path.GetDirectoryName(database) ?? ".";
        bool writable = ProbeWritable(databaseDirectory, out string detail);
        var report = new { Version, InstallFolder = AppContext.BaseDirectory, Tesseract = tesseract, TesseractReady = tesseract is not null, Database = database, DatabaseDirectoryWritable = writable, StorageDetail = detail };
        if (args.Contains("--json")) Console.WriteLine(JsonSerializer.Serialize(report, JsonIndented));
        else
        {
            WriteRow("Version", Version);
            WriteRow("Install folder", AppContext.BaseDirectory);
            WriteRow("Tesseract", tesseract ?? "not found");
            WriteRow("Database", database);
            WriteRow("Storage", detail);
        }
        return tesseract is not null && writable ? 0 : 2;
    }

    private static int RunSignatures(string[] args)
    {
        bool json = args.Contains("--json");
        List<string> files = args.Where(a => !a.StartsWith('-')).Select(ExistingFile).ToList();
        if (files.Count == 0) throw new CliException("signatures requires one or more source documents.");
        var candidates = files.Select(path => new { File = path, Status = "review-candidate", Note = "Original document preserved; visually review signatures and stamps." }).ToList();
        if (json) Console.WriteLine(JsonSerializer.Serialize(candidates, JsonIndented));
        else foreach (var item in candidates) Console.WriteLine($"- {item.File}: {item.Note}");
        return 0;
    }

    private static int RunRepository(string[] args)
    {
        if (args.Length == 0) throw new CliException("Use: intellifill repo status [--json] or intellifill repo install");
        if (args[0].Equals("install", StringComparison.OrdinalIgnoreCase)) return InstallRepository();
        if (!args[0].Equals("status", StringComparison.OrdinalIgnoreCase)) throw new CliException("Use: intellifill repo status [--json] or intellifill repo install");
        string manager;
        string path;
        bool configured;
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/pacman")) { manager = "pacman"; path = "/etc/pacman.d/intellifill-ocr.conf"; configured = File.Exists(path); }
        else if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/dnf")) { manager = "DNF"; path = "/etc/yum.repos.d/intellifill-ocr.repo"; configured = File.Exists(path); }
        else if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/eopkg")) { manager = "eopkg"; path = "eopkg list-repo"; configured = false; }
        else if (OperatingSystem.IsLinux()) { manager = "APT"; path = "/etc/apt/sources.list.d/intellifill-ocr.list"; configured = File.Exists(path); }
        else { manager = "unsupported"; path = ""; configured = false; }
        var report = new { PackageManager = manager, Configured = configured, Configuration = path };
        Console.WriteLine(args.Contains("--json") ? JsonSerializer.Serialize(report, JsonIndented) : $"{manager}: {(configured ? "configured" : "not configured")} {path}");
        return configured ? 0 : 2;
    }

    private static int InstallRepository()
    {
        if (!OperatingSystem.IsLinux()) throw new CliException("Package repository installation is available only on Linux.");
        string bundled = Path.Combine(AppContext.BaseDirectory, "repositories", "install-linux-repository.sh");
        string system = "/usr/share/intellifill-ocr/repositories/install-linux-repository.sh";
        string script = File.Exists(bundled) ? bundled : system;
        if (!File.Exists(script)) throw new CliException("The repository installer is not included in this installation. Reinstall the Linux package or use the website installer.");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "pkexec",
            UseShellExecute = false,
            ArgumentList = { "bash", script }
        }) ?? throw new CliException("Could not start the administrator authentication prompt.");
        process.WaitForExit();
        return process.ExitCode;
    }

    private static async Task<int> RunUpdateAsync(string[] args)
    {
        if (args.Length == 0 || !args[0].Equals("check", StringComparison.OrdinalIgnoreCase)) throw new CliException("Use: intellifill update check [--json]");
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IntelliFillOCR", Version));
        using Stream stream = await client.GetStreamAsync("https://api.github.com/repos/Abijspy/intellifill-ocr/releases/latest");
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        string tag = json.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
        string latest = tag.TrimStart('v', 'V');
        bool available = IsNewer(latest, Version);
        var report = new { CurrentVersion = Version, LatestVersion = latest, UpdateAvailable = available, ReleaseUrl = json.RootElement.GetProperty("html_url").GetString() };
        Console.WriteLine(args.Contains("--json") ? JsonSerializer.Serialize(report, JsonIndented) : available ? $"Update available: {latest}" : $"IntelliFill OCR {Version} is current.");
        return available ? 2 : 0;
    }

    private static (List<string> Files, List<string> Options) SplitFilesAndOptions(string[] args)
    {
        var files = new List<string>();
        var options = new List<string>();
        var needsValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--output", "-o", "--format", "-f", "--language", "-l", "--tesseract" };
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-')) { files.Add(args[i]); continue; }
            options.Add(args[i]);
            if (needsValue.Contains(args[i])) options.Add(RequireValue(args, ref i, args[i]));
        }
        return (files, options);
    }

    private static List<string> Positional(string[] args, params string[] knownOptions)
    {
        var valueOptions = new HashSet<string>(knownOptions.Where(o => o is not "--json"), StringComparer.OrdinalIgnoreCase);
        var files = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (valueOptions.Contains(args[i])) { i++; continue; }
            if (!args[i].StartsWith('-')) files.Add(args[i]);
        }
        return files;
    }

    private static string? Option(string[] args, params string[] names)
    {
        for (int i = 0; i < args.Length - 1; i++) if (names.Contains(args[i], StringComparer.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static string RequiredOption(string[] args, string name) => Option(args, name) ?? throw new CliException($"Option {name} is required.");
    private static List<string> RepeatedOption(string[] args, string name) => args.Select((value, index) => (value, index)).Where(x => x.value.Equals(name, StringComparison.OrdinalIgnoreCase) && x.index + 1 < args.Length).Select(x => args[x.index + 1]).ToList();
    private static string ExistingFile(string value) { string path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(value)); return File.Exists(path) ? path : throw new CliException($"File was not found: {path}"); }

    private static CellOverride ParseOverride(string value)
    {
        string[] pair = value.Split('=', 2);
        string[] address = pair[0].Split(',', StringSplitOptions.TrimEntries);
        if (pair.Length != 2 || address.Length != 3 || !address.All(v => int.TryParse(v, out int n) && n > 0)) throw new CliException("--set must use 1-based table,row,column=value syntax.");
        return new CellOverride(int.Parse(address[0]) - 1, int.Parse(address[1]) - 1, int.Parse(address[2]) - 1, pair[1]);
    }

    private static string DefaultAppData() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntelliFillOCR");
    private static bool ProbeWritable(string directory, out string detail)
    {
        try { Directory.CreateDirectory(directory); string path = Path.Combine(directory, $".intellifill-{Guid.NewGuid():N}"); File.WriteAllText(path, "ok"); File.Delete(path); detail = "writable"; return true; }
        catch (Exception ex) { detail = ex.Message; return false; }
    }
    private static bool IsNewer(string candidate, string current)
    {
        int[] left = candidate.Split('.').Select(v => int.TryParse(v, out int n) ? n : 0).ToArray();
        int[] right = current.Split('.').Select(v => int.TryParse(v, out int n) ? n : 0).ToArray();
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++) { int a = i < left.Length ? left[i] : 0, b = i < right.Length ? right[i] : 0; if (a != b) return a > b; }
        return false;
    }

    private static readonly JsonSerializerOptions JsonIndented = new() { WriteIndented = true };
}
