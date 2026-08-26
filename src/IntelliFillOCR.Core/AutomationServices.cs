using System.Globalization;
using System.Text.RegularExpressions;

namespace IntelliFillOCR.Core;

public sealed record ExtractedField(int Id, string SourceName, string Label, string Value, double Confidence);

public sealed record FieldMapping(
    string SourceLabel,
    string SourceValue,
    int TableIndex,
    int RowIndex,
    int ColumnIndex,
    string DestinationLabel,
    double Score);

public sealed record FillResult(
    IReadOnlyList<OutputTable> Tables,
    IReadOnlyList<FieldMapping> Mappings,
    IReadOnlyList<string> ValidationIssues);

public sealed class AutomationService
{
    public FillResult FillTemplate(
        DocumentPreview template,
        IReadOnlyList<DocumentPreview> sources,
        double threshold = 0.42,
        IReadOnlyList<CellOverride>? overrides = null)
    {
        if (threshold is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Match threshold must be between 0 and 1.");
        }

        List<List<List<string>>> output = template.Tables
            .Select(table => table.Rows.Select(row => row.ToList()).ToList())
            .ToList();
        List<ExtractedField> fields = sources.SelectMany(ExtractFields).ToList();
        var mappings = new List<FieldMapping>();

        for (int tableIndex = 0; tableIndex < output.Count; tableIndex++)
        {
            List<List<string>> table = output[tableIndex];
            for (int row = 0; row < table.Count; row++)
            {
                for (int column = 0; column < table[row].Count; column++)
                {
                    if (!IsFillablePlaceholder(table[row][column]))
                    {
                        continue;
                    }

                    string destination = DestinationLabel(table, row, column);
                    var candidates = fields
                        .Select(field => (Field: field, Score: Similarity(destination, field.Label)))
                        .OrderByDescending(candidate => candidate.Score)
                        .ToList();
                    if (candidates.Count == 0 || candidates[0].Score < threshold)
                    {
                        continue;
                    }

                    (ExtractedField Field, double Score) match = candidates[0];

                    string value = string.IsNullOrWhiteSpace(match.Field.Value) ? match.Field.Label : match.Field.Value;
                    table[row][column] = value;
                    mappings.Add(new FieldMapping(match.Field.Label, match.Field.Value, tableIndex, row, column, destination, match.Score));
                }
            }
        }

        foreach (CellOverride item in overrides ?? Array.Empty<CellOverride>())
        {
            if (item.TableIndex < 0 || item.TableIndex >= output.Count ||
                item.RowIndex < 0 || item.RowIndex >= output[item.TableIndex].Count ||
                item.ColumnIndex < 0 || item.ColumnIndex >= output[item.TableIndex][item.RowIndex].Count)
            {
                throw new ArgumentOutOfRangeException(nameof(overrides), $"Cell override {item.TableIndex + 1},{item.RowIndex + 1},{item.ColumnIndex + 1} is outside the template.");
            }
            output[item.TableIndex][item.RowIndex][item.ColumnIndex] = item.Value;
        }

        IReadOnlyList<OutputTable> tables = output
            .Select((rows, index) => new OutputTable(
                index < template.Tables.Count ? template.Tables[index].Label : $"Table {index + 1}",
                rows.Select(row => (IReadOnlyList<string>)row).ToList()))
            .ToList();
        return new FillResult(tables, mappings, Validate(tables));
    }

    public IReadOnlyList<ExtractedField> ExtractFields(DocumentPreview preview)
    {
        var fields = new List<ExtractedField>();
        int id = 1;
        foreach (DocumentTable table in preview.Tables)
        {
            foreach (IReadOnlyList<string> row in table.Rows)
            {
                for (int column = 0; column < row.Count; column++)
                {
                    string current = row[column].Trim();
                    if (current.Length == 0) continue;
                    if (column + 1 < row.Count && !string.IsNullOrWhiteSpace(row[column + 1]))
                    {
                        fields.Add(new ExtractedField(id++, preview.Name, current, row[column + 1].Trim(), 0.86));
                    }
                    else if (current.Contains(':'))
                    {
                        string[] parts = current.Split(':', 2);
                        if (!string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
                        {
                            fields.Add(new ExtractedField(id++, preview.Name, parts[0].Trim(), parts[1].Trim(), 0.82));
                        }
                    }
                }
            }
        }

        foreach (Match match in Regex.Matches(preview.ParsedText, @"(?m)^\s*([A-Za-z][A-Za-z0-9\s\/\.\-]{2,40})\s*[:\-]\s*(.{1,120})$").Cast<Match>().Take(80))
        {
            fields.Add(new ExtractedField(id++, preview.Name, match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim(), 0.74));
        }
        return fields;
    }

    public IReadOnlyList<string> Validate(IReadOnlyList<OutputTable> tables)
    {
        var issues = new List<string>();
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int tableIndex = 0; tableIndex < tables.Count; tableIndex++)
        {
            List<List<string>> rows = tables[tableIndex].Rows.Select(row => row.ToList()).ToList();
            for (int row = 0; row < rows.Count; row++)
            {
                for (int column = 0; column < rows[row].Count; column++)
                {
                    string value = rows[row][column].Trim();
                    string label = DestinationLabel(rows, row, column);
                    string location = $"{tables[tableIndex].Label} row {row + 1}, column {column + 1} ({label})";
                    if (IsFillablePlaceholder(value)) issues.Add($"Required/blank warning: {location} is empty.");
                    if (label.Contains("gst", StringComparison.OrdinalIgnoreCase) && value.Length > 0 &&
                        !Regex.IsMatch(value, @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z]Z[0-9A-Z]$", RegexOptions.IgnoreCase))
                        issues.Add($"GST/GSTIN warning: {label} value '{value}' does not look valid.");
                    if (label.Contains("date", StringComparison.OrdinalIgnoreCase) && value.Length > 0 &&
                        !DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                        issues.Add($"Date warning: {label} value '{value}' could not be parsed as a date.");
                    if ((label.Contains("amount", StringComparison.OrdinalIgnoreCase) || label.Contains("total", StringComparison.OrdinalIgnoreCase)) &&
                        value.Length > 0 && !TryParseAmount(value, out _))
                        issues.Add($"Amount warning: {label} value '{value}' is not a recognizable number.");
                    if (value.Length > 3 && seen.TryGetValue(value, out string? first)) issues.Add($"Duplicate warning: '{value}' appears in both {first} and {label}.");
                    else if (value.Length > 3) seen[value] = label;
                }
            }
        }
        return issues;
    }

    public static bool IsFillablePlaceholder(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        string trimmed = value.Trim();
        return trimmed.Contains("___", StringComparison.Ordinal) || Regex.IsMatch(trimmed, @"^\[.*\]$") ||
               Regex.IsMatch(trimmed, @"^\{\{.*\}\}$") || Regex.IsMatch(trimmed, @"^<.*>$");
    }

    private static string DestinationLabel(List<List<string>> table, int row, int column)
    {
        for (int left = column - 1; left >= 0; left--)
            if (!string.IsNullOrWhiteSpace(table[row][left]) && !IsFillablePlaceholder(table[row][left])) return table[row][left].Trim();
        for (int up = row - 1; up >= 0; up--)
            if (column < table[up].Count && !string.IsNullOrWhiteSpace(table[up][column]) && !IsFillablePlaceholder(table[up][column])) return table[up][column].Trim();
        return $"Row {row + 1} Column {column + 1}";
    }

    private static double Similarity(string left, string right)
    {
        string a = Regex.Replace(left.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        string b = Regex.Replace(right.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
        if (a.Length == 0 || b.Length == 0) return 0;
        if (a == b) return 1;
        HashSet<string> leftTokens = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        HashSet<string> rightTokens = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        int union = leftTokens.Union(rightTokens).Count();
        double tokenScore = union == 0 ? 0 : (double)leftTokens.Intersect(rightTokens).Count() / union;
        int distance = Levenshtein(a, b);
        return Math.Max(tokenScore, 1.0 - (double)distance / Math.Max(a.Length, b.Length));
    }

    private static int Levenshtein(string a, string b)
    {
        int[] costs = Enumerable.Range(0, b.Length + 1).ToArray();
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
}

public sealed record CellOverride(int TableIndex, int RowIndex, int ColumnIndex, string Value);

public static class TraceabilityService
{
    public static string Create(string mode = "timestamp", string? value = null)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        string random = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string candidate = mode.ToLowerInvariant() switch
        {
            "timestamp" => "IF" + timestamp,
            "prefix-timestamp" => (value ?? "IF") + timestamp,
            "prefix-random" => (value ?? "IF") + random,
            "manual" => value ?? throw new ArgumentException("Manual traceability mode requires a value."),
            _ => throw new ArgumentException("Traceability mode must be timestamp, prefix-timestamp, prefix-random, or manual.")
        };
        string normalized = Regex.Replace(candidate.ToUpperInvariant(), @"[^A-Z0-9. $/+%\-]", "-").Trim('-');
        return normalized.Length == 0 ? "IF" + timestamp : normalized[..Math.Min(24, normalized.Length)];
    }
}
