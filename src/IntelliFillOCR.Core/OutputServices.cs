using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace IntelliFillOCR.Core;

public sealed record OutputTable(string Label, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record RunValue(
    string TraceabilityCode,
    string TableLabel,
    int TableIndex,
    int RowIndex,
    int ColumnIndex,
    string Value);

public sealed class ExportService
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace OfficeRelationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public void ExportCsv(IReadOnlyList<OutputTable> tables, string path, string traceabilityCode)
    {
        var lines = new List<string> { $"Traceability,{EscapeCsv(traceabilityCode)}" };
        foreach (OutputTable table in tables)
        {
            lines.Add(string.Empty);
            lines.Add(EscapeCsv(table.Label));
            lines.AddRange(table.Rows.Select(row => string.Join(",", row.Select(EscapeCsv))));
        }
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    public void ExportXlsx(IReadOnlyList<OutputTable> tables, string path, string traceabilityCode)
    {
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        AddText(archive, "[Content_Types].xml", BuildXlsxContentTypes(tables.Count));
        AddText(
            archive,
            "_rels/.rels",
            new XDocument(
                new XElement(
                    Relationships + "Relationships",
                    new XElement(
                        Relationships + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "xl/workbook.xml")))).ToString(SaveOptions.DisableFormatting));

        AddText(archive, "xl/workbook.xml", BuildWorkbook(tables));
        AddText(archive, "xl/_rels/workbook.xml.rels", BuildWorkbookRelationships(tables.Count));
        for (int i = 0; i < tables.Count; i++)
        {
            AddText(archive, $"xl/worksheets/sheet{i + 1}.xml", BuildWorksheet(tables[i], traceabilityCode));
        }
    }

    public void ExportDocx(IReadOnlyList<OutputTable> tables, string path, string traceabilityCode)
    {
        using FileStream file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        AddText(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        AddText(
            archive,
            "_rels/.rels",
            new XDocument(
                new XElement(
                    Relationships + "Relationships",
                    new XElement(
                        Relationships + "Relationship",
                        new XAttribute("Id", "rId1"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
                        new XAttribute("Target", "word/document.xml")))).ToString(SaveOptions.DisableFormatting));

        var body = new XElement(Word + "body");
        body.Add(Paragraph("IntelliFill OCR Filled Output", bold: true));
        body.Add(Paragraph($"Traceability: {traceabilityCode}"));
        foreach (OutputTable table in tables)
        {
            body.Add(Paragraph(table.Label, bold: true));
            body.Add(BuildWordTable(table));
        }
        body.Add(Paragraph($"Traceability barcode/code: {traceabilityCode}"));
        body.Add(new XElement(Word + "sectPr"));

        var document = new XDocument(new XElement(Word + "document", new XAttribute(XNamespace.Xmlns + "w", Word), body));
        AddText(archive, "word/document.xml", document.ToString(SaveOptions.DisableFormatting));
    }

    public void ExportPdf(IReadOnlyList<OutputTable> tables, string path, string traceabilityCode)
    {
        var content = new StringBuilder();
        content.AppendLine("q");
        content.AppendLine("BT /F1 16 Tf 50 790 Td (IntelliFill OCR Filled Output) Tj ET");
        content.AppendLine($"BT /F1 9 Tf 50 772 Td ({PdfEscape("Traceability: " + traceabilityCode)}) Tj ET");

        double y = 742;
        foreach (OutputTable table in tables)
        {
            if (y < 120)
            {
                break;
            }
            content.AppendLine($"BT /F1 11 Tf 50 {Format(y)} Td ({PdfEscape(table.Label)}) Tj ET");
            y -= 18;
            foreach (IReadOnlyList<string> row in table.Rows.Take(18))
            {
                string line = string.Join("   |   ", row).Trim();
                if (line.Length > 105)
                {
                    line = line[..105];
                }
                content.AppendLine($"BT /F1 8 Tf 50 {Format(y)} Td ({PdfEscape(line)}) Tj ET");
                y -= 13;
                if (y < 120)
                {
                    break;
                }
            }
            y -= 10;
        }

        DrawCode39(content, traceabilityCode, 180, 42, 38);
        content.AppendLine($"BT /F1 8 Tf 236 28 Td ({PdfEscape(traceabilityCode)}) Tj ET");
        content.AppendLine("Q");

        WriteSinglePagePdf(path, content.ToString());
    }

    private static void DrawCode39(StringBuilder content, string value, double x, double y, double height)
    {
        string normalized = "*" + new string(value.ToUpperInvariant().Where(ch => Code39Patterns.ContainsKey(ch)).ToArray()) + "*";
        if (normalized.Length <= 2)
        {
            normalized = "*INTELLIFILL*";
        }

        double currentX = x;
        const double narrow = 1.35;
        const double wide = 3.1;
        foreach (char character in normalized)
        {
            string pattern = Code39Patterns[character];
            for (int index = 0; index < pattern.Length; index++)
            {
                double width = pattern[index] == 'w' ? wide : narrow;
                if (index % 2 == 0)
                {
                    content.AppendLine($"{Format(currentX)} {Format(y)} {Format(width)} {Format(height)} re f");
                }
                currentX += width;
            }
            currentX += narrow;
        }
    }

    private static readonly Dictionary<char, string> Code39Patterns = new()
    {
        ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
        ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
        ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn", ['A'] = "wnnnnwnnw", ['B'] = "nnwnnwnnw",
        ['C'] = "wnwnnwnnn", ['D'] = "nnnnwwnnw", ['E'] = "wnnnwwnnn", ['F'] = "nnwnwwnnn",
        ['G'] = "nnnnnwwnw", ['H'] = "wnnnnwwnn", ['I'] = "nnwnnwwnn", ['J'] = "nnnnwwwnn",
        ['K'] = "wnnnnnnww", ['L'] = "nnwnnnnww", ['M'] = "wnwnnnnwn", ['N'] = "nnnnwnnww",
        ['O'] = "wnnnwnnwn", ['P'] = "nnwnwnnwn", ['Q'] = "nnnnnnwww", ['R'] = "wnnnnnwwn",
        ['S'] = "nnwnnnwwn", ['T'] = "nnnnwnwwn", ['U'] = "wwnnnnnnw", ['V'] = "nwwnnnnnw",
        ['W'] = "wwwnnnnnn", ['X'] = "nwnnwnnnw", ['Y'] = "wwnnwnnnn", ['Z'] = "nwwnwnnnn",
        ['-'] = "nwnnnnwnw", ['.'] = "wwnnnnwnn", [' '] = "nwwnnnwnn", ['$'] = "nwnwnwnnn",
        ['/'] = "nwnwnnnwn", ['+'] = "nwnnnwnwn", ['%'] = "nnnwnwnwn", ['*'] = "nwnnwnwnn"
    };

    private static void WriteSinglePagePdf(string path, string content)
    {
        byte[] contentBytes = Encoding.ASCII.GetBytes(content);
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream"
        };

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (int index = 0; index < objects.Count; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.AppendLine($"{index + 1} 0 obj");
            builder.AppendLine(objects[index]);
            builder.AppendLine("endobj");
        }

        int xrefOffset = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.AppendLine("xref");
        builder.AppendLine($"0 {objects.Count + 1}");
        builder.AppendLine("0000000000 65535 f ");
        foreach (int offset in offsets.Skip(1))
        {
            builder.AppendLine(offset.ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n ");
        }
        builder.AppendLine("trailer");
        builder.AppendLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        builder.AppendLine("startxref");
        builder.AppendLine(xrefOffset.ToString(CultureInfo.InvariantCulture));
        builder.AppendLine("%%EOF");
        File.WriteAllText(path, builder.ToString(), Encoding.ASCII);
    }

    private static string BuildXlsxContentTypes(int sheetCount)
    {
        var types = new XElement(
            XNamespace.Get("http://schemas.openxmlformats.org/package/2006/content-types") + "Types",
            new XElement("Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement("Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            new XElement("Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")));
        for (int i = 1; i <= sheetCount; i++)
        {
            types.Add(new XElement("Override", new XAttribute("PartName", $"/xl/worksheets/sheet{i}.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
        }
        return new XDocument(types).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildWorkbook(IReadOnlyList<OutputTable> tables)
    {
        var sheets = new XElement(Spreadsheet + "sheets");
        for (int i = 0; i < tables.Count; i++)
        {
            sheets.Add(new XElement(
                Spreadsheet + "sheet",
                new XAttribute("name", SafeSheetName(tables[i].Label, i)),
                new XAttribute("sheetId", i + 1),
                new XAttribute(OfficeRelationships + "id", $"rId{i + 1}")));
        }
        return new XDocument(new XElement(Spreadsheet + "workbook", new XAttribute(XNamespace.Xmlns + "r", OfficeRelationships), sheets)).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildWorkbookRelationships(int sheetCount)
    {
        var relationships = new XElement(Relationships + "Relationships");
        for (int i = 1; i <= sheetCount; i++)
        {
            relationships.Add(new XElement(
                Relationships + "Relationship",
                new XAttribute("Id", $"rId{i}"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                new XAttribute("Target", $"worksheets/sheet{i}.xml")));
        }
        return new XDocument(relationships).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildWorksheet(OutputTable table, string traceabilityCode)
    {
        var sheetData = new XElement(Spreadsheet + "sheetData");
        int rowIndex = 1;
        sheetData.Add(BuildXlsxRow(rowIndex++, new[] { "Traceability", traceabilityCode }));
        sheetData.Add(BuildXlsxRow(rowIndex++, Array.Empty<string>()));
        sheetData.Add(BuildXlsxRow(rowIndex++, new[] { table.Label }));
        foreach (IReadOnlyList<string> row in table.Rows)
        {
            sheetData.Add(BuildXlsxRow(rowIndex++, row));
        }
        return new XDocument(new XElement(Spreadsheet + "worksheet", sheetData)).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement BuildXlsxRow(int rowIndex, IReadOnlyList<string> values)
    {
        var row = new XElement(Spreadsheet + "row", new XAttribute("r", rowIndex));
        for (int column = 0; column < values.Count; column++)
        {
            row.Add(new XElement(
                Spreadsheet + "c",
                new XAttribute("r", ColumnName(column + 1) + rowIndex),
                new XAttribute("t", "inlineStr"),
                new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", values[column] ?? string.Empty))));
        }
        return row;
    }

    private static XElement BuildWordTable(OutputTable table)
    {
        var wordTable = new XElement(Word + "tbl");
        foreach (IReadOnlyList<string> row in table.Rows)
        {
            wordTable.Add(new XElement(Word + "tr", row.Select(value => new XElement(Word + "tc", Paragraph(value)))));
        }
        return wordTable;
    }

    private static XElement Paragraph(string text, bool bold = false)
    {
        var run = new XElement(Word + "r");
        if (bold)
        {
            run.Add(new XElement(Word + "rPr", new XElement(Word + "b")));
        }
        run.Add(new XElement(Word + "t", text ?? string.Empty));
        return new XElement(Word + "p", run);
    }

    private static string ColumnName(int column)
    {
        var name = new StringBuilder();
        while (column > 0)
        {
            int modulo = (column - 1) % 26;
            name.Insert(0, (char)('A' + modulo));
            column = (column - modulo) / 26;
        }
        return name.ToString();
    }

    private static string SafeSheetName(string value, int index)
    {
        string cleaned = new string((value.Length == 0 ? $"Table {index + 1}" : value).Where(ch => !":\\/?*[]".Contains(ch)).ToArray());
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static void AddText(ZipArchive archive, string entryPath, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string EscapeCsv(string value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    private static string PdfEscape(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class DatabaseService
{
    public void SaveRun(
        string databasePath,
        string traceabilityCode,
        string templatePath,
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<RunValue> values,
        IReadOnlyList<string> mappingSummaries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath) ?? ".");
        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        EnsureSchema(connection);

        using SqliteTransaction transaction = connection.BeginTransaction();
        using SqliteCommand runCommand = connection.CreateCommand();
        runCommand.Transaction = transaction;
        runCommand.CommandText = """
            INSERT INTO runs(traceability_code, template_path, source_paths, created_at)
            VALUES($traceability_code, $template_path, $source_paths, $created_at);
            SELECT last_insert_rowid();
            """;
        runCommand.Parameters.AddWithValue("$traceability_code", traceabilityCode);
        runCommand.Parameters.AddWithValue("$template_path", templatePath);
        runCommand.Parameters.AddWithValue("$source_paths", string.Join(Environment.NewLine, sourcePaths));
        runCommand.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
        long runId = (long)runCommand.ExecuteScalar()!;

        foreach (RunValue value in values)
        {
            using SqliteCommand valueCommand = connection.CreateCommand();
            valueCommand.Transaction = transaction;
            valueCommand.CommandText = """
                INSERT INTO extracted_values(run_id, table_label, table_index, row_index, column_index, value)
                VALUES($run_id, $table_label, $table_index, $row_index, $column_index, $value)
                """;
            valueCommand.Parameters.AddWithValue("$run_id", runId);
            valueCommand.Parameters.AddWithValue("$table_label", value.TableLabel);
            valueCommand.Parameters.AddWithValue("$table_index", value.TableIndex);
            valueCommand.Parameters.AddWithValue("$row_index", value.RowIndex);
            valueCommand.Parameters.AddWithValue("$column_index", value.ColumnIndex);
            valueCommand.Parameters.AddWithValue("$value", value.Value);
            valueCommand.ExecuteNonQuery();
        }

        foreach (string mapping in mappingSummaries)
        {
            using SqliteCommand mappingCommand = connection.CreateCommand();
            mappingCommand.Transaction = transaction;
            mappingCommand.CommandText = "INSERT INTO mappings(run_id, summary) VALUES($run_id, $summary)";
            mappingCommand.Parameters.AddWithValue("$run_id", runId);
            mappingCommand.Parameters.AddWithValue("$summary", mapping);
            mappingCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public string Preview(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            return $"Database file does not exist yet: {databasePath}";
        }

        using var connection = new SqliteConnection($"Data Source={databasePath}");
        connection.Open();
        EnsureSchema(connection);
        var builder = new StringBuilder();
        builder.AppendLine($"Database: {databasePath}");
        builder.AppendLine();

        using (SqliteCommand counts = connection.CreateCommand())
        {
            counts.CommandText = """
                SELECT
                  (SELECT COUNT(*) FROM runs) AS runs_count,
                  (SELECT COUNT(*) FROM extracted_values) AS values_count,
                  (SELECT COUNT(*) FROM mappings) AS mappings_count
                """;
            using SqliteDataReader reader = counts.ExecuteReader();
            if (reader.Read())
            {
                builder.AppendLine($"Runs: {reader.GetInt64(0)}");
                builder.AppendLine($"Extracted values: {reader.GetInt64(1)}");
                builder.AppendLine($"Mappings: {reader.GetInt64(2)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("Recent runs:");
        using SqliteCommand recent = connection.CreateCommand();
        recent.CommandText = "SELECT id, traceability_code, created_at, template_path FROM runs ORDER BY id DESC LIMIT 20";
        using SqliteDataReader recentReader = recent.ExecuteReader();
        while (recentReader.Read())
        {
            builder.AppendLine($"#{recentReader.GetInt64(0)}  {recentReader.GetString(1)}  {recentReader.GetString(2)}");
            builder.AppendLine($"    {recentReader.GetString(3)}");
        }
        return builder.ToString();
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS runs (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              traceability_code TEXT NOT NULL UNIQUE,
              template_path TEXT NOT NULL,
              source_paths TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS extracted_values (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              run_id INTEGER NOT NULL,
              table_label TEXT NOT NULL,
              table_index INTEGER NOT NULL,
              row_index INTEGER NOT NULL,
              column_index INTEGER NOT NULL,
              value TEXT NOT NULL,
              FOREIGN KEY(run_id) REFERENCES runs(id)
            );
            CREATE TABLE IF NOT EXISTS mappings (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              run_id INTEGER NOT NULL,
              summary TEXT NOT NULL,
              FOREIGN KEY(run_id) REFERENCES runs(id)
            );
            """;
        command.ExecuteNonQuery();
        EnsureColumn(connection, "extracted_values", "table_label", "TEXT NOT NULL DEFAULT 'Table 1'");
        EnsureColumn(connection, "extracted_values", "table_index", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "extracted_values", "row_index", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "extracted_values", "column_index", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string definition)
    {
        using SqliteCommand infoCommand = connection.CreateCommand();
        infoCommand.CommandText = $"PRAGMA table_info({tableName})";
        using SqliteDataReader reader = infoCommand.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        using SqliteCommand alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition}";
        alterCommand.ExecuteNonQuery();
    }
}
