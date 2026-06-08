using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
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
        AddText(archive, "xl/styles.xml", BuildXlsxStyles());
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
        body.Add(Paragraph("IntelliFill OCR Filled Output", bold: true, fontSize: 32));
        body.Add(Paragraph($"Traceability ID: {traceabilityCode}", fontSize: 18, color: "475569"));
        body.Add(Paragraph($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}", fontSize: 16, color: "64748B"));
        foreach (OutputTable table in tables)
        {
            body.Add(Paragraph(table.Label, bold: true, fontSize: 24));
            body.Add(BuildWordTable(table));
        }
        body.Add(Paragraph($"Traceability barcode/code: {traceabilityCode}", bold: true, fontSize: 18));
        body.Add(SectionProperties());

        var document = new XDocument(new XElement(Word + "document", new XAttribute(XNamespace.Xmlns + "w", Word), body));
        AddText(archive, "word/document.xml", document.ToString(SaveOptions.DisableFormatting));
    }

    public void ExportPdf(IReadOnlyList<OutputTable> tables, string path, string traceabilityCode)
    {
        var pages = new List<StringBuilder>();
        StringBuilder page = NewPdfPage(traceabilityCode);
        double y = 712;
        foreach (OutputTable table in tables)
        {
            int columns = Math.Clamp(table.Rows.Select(row => row.Count).DefaultIfEmpty(1).Max(), 1, 8);
            double tableWidth = 516;
            double columnWidth = tableWidth / columns;
            if (y < 135)
            {
                pages.Add(page);
                page = NewPdfPage(traceabilityCode);
                y = 712;
            }

            DrawPdfText(page, 48, y, "F2", 12, table.Label);
            y -= 18;

            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                IReadOnlyList<string> row = table.Rows[rowIndex];
                var wrappedCells = new List<IReadOnlyList<string>>();
                int maxLines = 1;
                for (int column = 0; column < columns; column++)
                {
                    string value = column < row.Count ? row[column] : string.Empty;
                    IReadOnlyList<string> lines = WrapPdfText(value, columnWidth - 8, 8.2).Take(4).ToList();
                    if (lines.Count == 0)
                    {
                        lines = new[] { string.Empty };
                    }
                    wrappedCells.Add(lines);
                    maxLines = Math.Max(maxLines, lines.Count);
                }

                double rowHeight = Math.Max(22, 14 + maxLines * 10);
                if (y - rowHeight < 82)
                {
                    pages.Add(page);
                    page = NewPdfPage(traceabilityCode);
                    y = 712;
                }

                bool headerRow = rowIndex == 0;
                for (int column = 0; column < columns; column++)
                {
                    double x = 48 + column * columnWidth;
                    if (headerRow)
                    {
                        page.AppendLine("0.90 0.94 1 rg");
                        page.AppendLine($"{Format(x)} {Format(y - rowHeight)} {Format(columnWidth)} {Format(rowHeight)} re f");
                    }
                    page.AppendLine("0.62 0.67 0.75 RG");
                    page.AppendLine($"{Format(x)} {Format(y - rowHeight)} {Format(columnWidth)} {Format(rowHeight)} re S");
                    for (int lineIndex = 0; lineIndex < wrappedCells[column].Count; lineIndex++)
                    {
                        DrawPdfText(page, x + 4, y - 12 - lineIndex * 9.8, headerRow ? "F2" : "F1", 8.2, wrappedCells[column][lineIndex]);
                    }
                }
                y -= rowHeight;
            }
            y -= 16;
        }

        pages.Add(page);
        for (int index = 0; index < pages.Count; index++)
        {
            AddPdfFooter(pages[index], traceabilityCode, index + 1, pages.Count, includeBarcode: index == pages.Count - 1);
        }

        WritePdf(path, pages.Select(builder => builder.ToString()).ToList());
    }

    private static StringBuilder NewPdfPage(string traceabilityCode)
    {
        var content = new StringBuilder();
        content.AppendLine("q");
        DrawPdfText(content, 48, 760, "F2", 18, "IntelliFill OCR Filled Output");
        DrawPdfText(content, 48, 740, "F1", 9, $"Traceability ID: {traceabilityCode}");
        DrawPdfText(content, 48, 726, "F1", 8, $"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm zzz}");
        content.AppendLine("0.76 0.80 0.87 rg");
        content.AppendLine("48 720 516 0.8 re f");
        return content;
    }

    private static void AddPdfFooter(StringBuilder content, string traceabilityCode, int pageNumber, int pageCount, bool includeBarcode)
    {
        content.AppendLine("0.76 0.80 0.87 rg");
        content.AppendLine("48 58 516 0.6 re f");
        DrawPdfText(content, 48, 65, "F1", 7.5, $"Traceability ID: {traceabilityCode}");
        DrawPdfText(content, 516, 65, "F1", 7.5, $"Page {pageNumber} of {pageCount}");
        if (includeBarcode)
        {
            double barcodeWidth = MeasureCode39(traceabilityCode);
            double barcodeX = Math.Max(48, Math.Min((612 - barcodeWidth) / 2, 564 - barcodeWidth));
            content.AppendLine("1 1 1 rg");
            content.AppendLine($"{Format(barcodeX - 10)} 6 {Format(barcodeWidth + 20)} 48 re f");
            content.AppendLine("0 0 0 rg");
            DrawCode39(content, traceabilityCode, barcodeX, 20, 28);
            DrawPdfText(content, CenterTextX(traceabilityCode, 7.8), 8, "F1", 7.8, traceabilityCode);
        }
        content.AppendLine("Q");
    }

    private static void DrawPdfText(StringBuilder content, double x, double y, string font, double size, string text)
    {
        content.AppendLine($"0 0 0 rg BT /{font} {Format(size)} Tf {Format(x)} {Format(y)} Td ({PdfEscape(text)}) Tj ET");
    }

    private static IReadOnlyList<string> WrapPdfText(string value, double width, double fontSize)
    {
        value = string.IsNullOrWhiteSpace(value) ? string.Empty : Regex.Replace(value.Trim(), @"\s+", " ");
        int maxCharacters = Math.Max(8, (int)Math.Floor(width / (fontSize * 0.48)));
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (string word in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > maxCharacters)
            {
                if (current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
                for (int index = 0; index < word.Length; index += maxCharacters)
                {
                    lines.Add(word.Substring(index, Math.Min(maxCharacters, word.Length - index)));
                }
                continue;
            }

            if (current.Length > 0 && current.Length + word.Length + 1 > maxCharacters)
            {
                lines.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }
            current.Append(word);
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }
        return lines.Count == 0 ? new[] { string.Empty } : lines;
    }

    private static void DrawCode39(StringBuilder content, string value, double x, double y, double height)
    {
        string normalized = "*" + new string(value.ToUpperInvariant().Where(ch => Code39Patterns.ContainsKey(ch)).ToArray()) + "*";
        if (normalized.Length <= 2)
        {
            normalized = "*INTELLIFILL*";
        }

        double currentX = x;
        const double narrow = 1.55;
        const double wide = 3.65;
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

    private static double MeasureCode39(string value)
    {
        string normalized = "*" + new string(value.ToUpperInvariant().Where(ch => Code39Patterns.ContainsKey(ch)).ToArray()) + "*";
        if (normalized.Length <= 2)
        {
            normalized = "*INTELLIFILL*";
        }

        const double narrow = 1.55;
        const double wide = 3.65;
        double width = 0;
        foreach (char character in normalized)
        {
            string pattern = Code39Patterns[character];
            width += pattern.Sum(part => part == 'w' ? wide : narrow) + narrow;
        }
        return width;
    }

    private static double CenterTextX(string value, double fontSize)
    {
        double width = value.Length * fontSize * 0.48;
        return Math.Max(48, (612 - width) / 2);
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

    private static void WritePdf(string path, IReadOnlyList<string> pageContents)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"
        };

        var pageIds = new List<int>();
        foreach (string content in pageContents)
        {
            byte[] contentBytes = Encoding.ASCII.GetBytes(content);
            int pageObjectId = objects.Count + 1;
            int contentObjectId = pageObjectId + 1;
            pageIds.Add(pageObjectId);
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentObjectId} 0 R >>");
            objects.Add($"<< /Length {contentBytes.Length} >>\nstream\n{content}\nendstream");
        }

        objects[1] = $"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(id => $"{id} 0 R"))}] /Count {pageIds.Count} >>";

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
            new XElement("Override", new XAttribute("PartName", "/xl/workbook.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
            new XElement("Override", new XAttribute("PartName", "/xl/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml")));
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
        relationships.Add(new XElement(
            Relationships + "Relationship",
            new XAttribute("Id", $"rId{sheetCount + 1}"),
            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            new XAttribute("Target", "styles.xml")));
        return new XDocument(relationships).ToString(SaveOptions.DisableFormatting);
    }

    private static string BuildWorksheet(OutputTable table, string traceabilityCode)
    {
        int columns = Math.Max(2, table.Rows.Select(row => row.Count).DefaultIfEmpty(1).Max());
        var cols = new XElement(Spreadsheet + "cols");
        for (int column = 1; column <= Math.Min(columns, 24); column++)
        {
            cols.Add(new XElement(Spreadsheet + "col", new XAttribute("min", column), new XAttribute("max", column), new XAttribute("width", "22"), new XAttribute("customWidth", "1")));
        }

        var sheetData = new XElement(Spreadsheet + "sheetData");
        int rowIndex = 1;
        sheetData.Add(BuildXlsxRow(rowIndex++, new[] { "Traceability ID", traceabilityCode }, 2));
        sheetData.Add(BuildXlsxRow(rowIndex++, Array.Empty<string>()));
        sheetData.Add(BuildXlsxRow(rowIndex++, new[] { table.Label }, 1));
        for (int index = 0; index < table.Rows.Count; index++)
        {
            sheetData.Add(BuildXlsxRow(rowIndex++, table.Rows[index], index == 0 ? 3 : 0));
        }
        return new XDocument(new XElement(Spreadsheet + "worksheet", cols, sheetData)).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement BuildXlsxRow(int rowIndex, IReadOnlyList<string> values, int styleIndex = 0)
    {
        var row = new XElement(Spreadsheet + "row", new XAttribute("r", rowIndex));
        for (int column = 0; column < values.Count; column++)
        {
            var cell = new XElement(
                Spreadsheet + "c",
                new XAttribute("r", ColumnName(column + 1) + rowIndex),
                new XAttribute("t", "inlineStr"),
                new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t", values[column] ?? string.Empty)));
            if (styleIndex > 0)
            {
                cell.Add(new XAttribute("s", styleIndex));
            }
            row.Add(cell);
        }
        return row;
    }

    private static string BuildXlsxStyles()
    {
        XNamespace ns = Spreadsheet;
        var styles = new XElement(
            ns + "styleSheet",
            new XElement(
                ns + "fonts",
                new XAttribute("count", "3"),
                new XElement(ns + "font", new XElement(ns + "sz", new XAttribute("val", "11")), new XElement(ns + "name", new XAttribute("val", "Calibri"))),
                new XElement(ns + "font", new XElement(ns + "b"), new XElement(ns + "sz", new XAttribute("val", "13")), new XElement(ns + "name", new XAttribute("val", "Calibri"))),
                new XElement(ns + "font", new XElement(ns + "b"), new XElement(ns + "sz", new XAttribute("val", "11")), new XElement(ns + "name", new XAttribute("val", "Calibri")))),
            new XElement(
                ns + "fills",
                new XAttribute("count", "3"),
                new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "none"))),
                new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "gray125"))),
                new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "solid"), new XElement(ns + "fgColor", new XAttribute("rgb", "FFDDEBFF")), new XElement(ns + "bgColor", new XAttribute("indexed", "64"))))),
            new XElement(
                ns + "borders",
                new XAttribute("count", "2"),
                new XElement(ns + "border", new XElement(ns + "left"), new XElement(ns + "right"), new XElement(ns + "top"), new XElement(ns + "bottom"), new XElement(ns + "diagonal")),
                new XElement(ns + "border", BorderSide("left"), BorderSide("right"), BorderSide("top"), BorderSide("bottom"), new XElement(ns + "diagonal"))),
            new XElement(ns + "cellStyleXfs", new XAttribute("count", "1"), new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
            new XElement(
                ns + "cellXfs",
                new XAttribute("count", "4"),
                new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "1"), new XAttribute("xfId", "0"), new XAttribute("applyBorder", "1")),
                new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1")),
                new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "2"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1")),
                new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "2"), new XAttribute("fillId", "2"), new XAttribute("borderId", "1"), new XAttribute("xfId", "0"), new XAttribute("applyFont", "1"), new XAttribute("applyFill", "1"), new XAttribute("applyBorder", "1"))),
            new XElement(ns + "cellStyles", new XAttribute("count", "1"), new XElement(ns + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0"))));
        return new XDocument(styles).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement BorderSide(string name) =>
        new(Spreadsheet + name, new XAttribute("style", "thin"), new XElement(Spreadsheet + "color", new XAttribute("rgb", "FF94A3B8")));

    private static XElement BuildWordTable(OutputTable table)
    {
        int columns = Math.Max(1, table.Rows.Select(row => row.Count).DefaultIfEmpty(1).Max());
        int cellWidth = Math.Max(900, 9000 / columns);
        var wordTable = new XElement(
            Word + "tbl",
            new XElement(
                Word + "tblPr",
                new XElement(Word + "tblW", new XAttribute(Word + "w", "5000"), new XAttribute(Word + "type", "pct")),
                new XElement(
                    Word + "tblBorders",
                    WordBorder("top"),
                    WordBorder("left"),
                    WordBorder("bottom"),
                    WordBorder("right"),
                    WordBorder("insideH"),
                    WordBorder("insideV"))),
            new XElement(Word + "tblGrid", Enumerable.Range(0, columns).Select(_ => new XElement(Word + "gridCol", new XAttribute(Word + "w", cellWidth)))));

        for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            IReadOnlyList<string> row = table.Rows[rowIndex];
            wordTable.Add(new XElement(
                Word + "tr",
                Enumerable.Range(0, columns).Select(column =>
                {
                    string value = column < row.Count ? row[column] : string.Empty;
                    return new XElement(
                        Word + "tc",
                        new XElement(
                            Word + "tcPr",
                            new XElement(Word + "tcW", new XAttribute(Word + "w", cellWidth), new XAttribute(Word + "type", "dxa")),
                            rowIndex == 0 ? new XElement(Word + "shd", new XAttribute(Word + "fill", "DDEBFF")) : null),
                        Paragraph(value, bold: rowIndex == 0, fontSize: 17));
                })));
        }
        return wordTable;
    }

    private static XElement Paragraph(string text, bool bold = false, int fontSize = 20, string? color = null)
    {
        var run = new XElement(Word + "r");
        var runProperties = new XElement(Word + "rPr");
        if (bold)
        {
            runProperties.Add(new XElement(Word + "b"));
        }
        runProperties.Add(new XElement(Word + "sz", new XAttribute(Word + "val", fontSize)));
        if (!string.IsNullOrWhiteSpace(color))
        {
            runProperties.Add(new XElement(Word + "color", new XAttribute(Word + "val", color)));
        }
        run.Add(runProperties);
        run.Add(new XElement(Word + "t", text ?? string.Empty));
        return new XElement(
            Word + "p",
            new XElement(Word + "pPr", new XElement(Word + "spacing", new XAttribute(Word + "after", "120"))),
            run);
    }

    private static XElement WordBorder(string name) =>
        new(Word + name, new XAttribute(Word + "val", "single"), new XAttribute(Word + "sz", "6"), new XAttribute(Word + "color", "94A3B8"));

    private static XElement SectionProperties() =>
        new(
            Word + "sectPr",
            new XElement(Word + "pgSz", new XAttribute(Word + "w", "12240"), new XAttribute(Word + "h", "15840")),
            new XElement(Word + "pgMar", new XAttribute(Word + "top", "720"), new XAttribute(Word + "right", "720"), new XAttribute(Word + "bottom", "720"), new XAttribute(Word + "left", "720"), new XAttribute(Word + "header", "360"), new XAttribute(Word + "footer", "360"), new XAttribute(Word + "gutter", "0")));

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
