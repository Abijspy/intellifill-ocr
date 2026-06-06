using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace IntelliFillOCR.Core;

public sealed record DocumentTable(string Label, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public int RowCount => Rows.Count;
    public int ColumnCount => Rows.Select(row => row.Count).DefaultIfEmpty(0).Max();
}

public sealed record DocumentPreview(string Name, string Path, string ParsedText, IReadOnlyList<DocumentTable> Tables);

public sealed class DocumentLoader
{
    private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public DocumentPreview Load(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".csv" => LoadCsv(path),
            ".txt" => LoadText(path),
            ".xlsx" => LoadXlsx(path),
            ".xls" => LoadPlaceholder(path, "Legacy .xls files are detected. Save as .xlsx for full native table parsing."),
            ".docx" => LoadDocx(path),
            ".pdf" => LoadPlaceholder(path, "PDF preview and region selection are available. OCR extraction uses the selected visual region workflow."),
            ".png" or ".jpg" or ".jpeg" => LoadPlaceholder(path, "Image preview and OCR region selection are available."),
            _ => LoadText(path)
        };
    }

    public DocumentPreview LoadManyText(string path)
    {
        try
        {
            return Load(path);
        }
        catch (Exception ex)
        {
            return new DocumentPreview(
                System.IO.Path.GetFileName(path),
                path,
                $"Could not parse this file natively yet: {ex.Message}",
                Array.Empty<DocumentTable>());
        }
    }

    private static DocumentPreview LoadCsv(string path)
    {
        var rows = File.ReadAllLines(path)
            .Select(ParseCsvLine)
            .Where(row => row.Count > 0)
            .ToList();
        string text = string.Join(Environment.NewLine, rows.Select(row => string.Join(" | ", row)));
        return new DocumentPreview(
            System.IO.Path.GetFileNameWithoutExtension(path),
            path,
            text,
            new[] { new DocumentTable("Table 1", rows) });
    }

    private static DocumentPreview LoadText(string path)
    {
        string text = File.ReadAllText(path);
        var rows = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => (IReadOnlyList<string>)new[] { line.Trim() })
            .ToList();
        return new DocumentPreview(
            System.IO.Path.GetFileNameWithoutExtension(path),
            path,
            text,
            rows.Count > 0 ? new[] { new DocumentTable("Text Lines", rows) } : Array.Empty<DocumentTable>());
    }

    private static DocumentPreview LoadPlaceholder(string path, string message)
    {
        return new DocumentPreview(
            System.IO.Path.GetFileNameWithoutExtension(path),
            path,
            message,
            new[] { new DocumentTable("Document", new[] { new[] { message } }) });
    }

    private static DocumentPreview LoadXlsx(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        List<string> sharedStrings = ReadSharedStrings(archive);
        var tables = new List<DocumentTable>();
        foreach (ZipArchiveEntry worksheet in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            using Stream stream = worksheet.Open();
            XDocument document = XDocument.Load(stream);
            var rows = new List<IReadOnlyList<string>>();
            foreach (XElement rowElement in document.Descendants(Spreadsheet + "row"))
            {
                var values = new List<string>();
                foreach (XElement cellElement in rowElement.Elements(Spreadsheet + "c"))
                {
                    int columnIndex = ColumnIndexFromCellReference((string?)cellElement.Attribute("r"));
                    while (values.Count < columnIndex)
                    {
                        values.Add(string.Empty);
                    }
                    values.Add(ReadCellValue(cellElement, sharedStrings));
                }
                if (values.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    rows.Add(values);
                }
            }

            if (rows.Count > 0)
            {
                tables.Add(new DocumentTable($"Sheet {tables.Count + 1}", rows));
            }
        }

        return new DocumentPreview(
            System.IO.Path.GetFileNameWithoutExtension(path),
            path,
            string.Join(Environment.NewLine + Environment.NewLine, tables.Select(TableToText)),
            tables);
    }

    private static DocumentPreview LoadDocx(string path)
    {
        using ZipArchive archive = ZipFile.OpenRead(path);
        ZipArchiveEntry? documentEntry = archive.GetEntry("word/document.xml");
        if (documentEntry is null)
        {
            throw new InvalidDataException("DOCX document.xml was not found.");
        }

        using Stream stream = documentEntry.Open();
        XDocument document = XDocument.Load(stream);
        var tables = new List<DocumentTable>();
        foreach (XElement tableElement in document.Descendants(Word + "tbl"))
        {
            var rows = new List<IReadOnlyList<string>>();
            foreach (XElement rowElement in tableElement.Elements(Word + "tr"))
            {
                var row = rowElement.Elements(Word + "tc")
                    .Select(CellText)
                    .ToList();
                if (row.Any(value => !string.IsNullOrWhiteSpace(value)))
                {
                    rows.Add(row);
                }
            }

            if (rows.Count > 0)
            {
                tables.Add(new DocumentTable($"Table {tables.Count + 1}", rows));
            }
        }

        string parsedText = string.Join(Environment.NewLine, document.Descendants(Word + "p").Select(ParagraphText).Where(text => text.Length > 0));
        if (tables.Count == 0 && !string.IsNullOrWhiteSpace(parsedText))
        {
            tables.Add(new DocumentTable("Paragraphs", parsedText.Split(Environment.NewLine).Select(line => (IReadOnlyList<string>)new[] { line }).ToList()));
        }

        return new DocumentPreview(System.IO.Path.GetFileNameWithoutExtension(path), path, parsedText, tables);
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return new List<string>();
        }

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream);
        return document.Descendants(Spreadsheet + "si").Select(element => string.Concat(element.Descendants(Spreadsheet + "t").Select(text => text.Value))).ToList();
    }

    private static string ReadCellValue(XElement cellElement, IReadOnlyList<string> sharedStrings)
    {
        string type = (string?)cellElement.Attribute("t") ?? "";
        string rawValue = cellElement.Element(Spreadsheet + "v")?.Value ?? cellElement.Element(Spreadsheet + "is")?.Value ?? "";
        if (type == "s" && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sharedIndex))
        {
            return sharedIndex >= 0 && sharedIndex < sharedStrings.Count ? sharedStrings[sharedIndex] : "";
        }
        return rawValue;
    }

    private static int ColumnIndexFromCellReference(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return 0;
        }

        int index = 0;
        foreach (char character in reference.TakeWhile(char.IsLetter))
        {
            index = index * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        }
        return Math.Max(0, index - 1);
    }

    private static string ParagraphText(XElement paragraph)
    {
        return string.Concat(paragraph.Descendants(Word + "t").Select(text => text.Value)).Trim();
    }

    private static string CellText(XElement cell)
    {
        return string.Join(" ", cell.Descendants(Word + "p").Select(ParagraphText).Where(text => text.Length > 0)).Trim();
    }

    private static string TableToText(DocumentTable table)
    {
        return string.Join(Environment.NewLine, table.Rows.Select(row => string.Join(" | ", row)));
    }

    private static IReadOnlyList<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (character == '"' && index + 1 < line.Length && line[index + 1] == '"')
            {
                current.Append('"');
                index++;
                continue;
            }
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }
            if (character == ',' && !inQuotes)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        values.Add(current.ToString());
        return values;
    }
}
