using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace TablePlanner.Export.Tabular;

/// <summary>
/// Reads and writes the first worksheet of an Excel (.xlsx) file as text cells. An .xlsx file is a zip of XML
/// parts; only the parts needed for one sheet of text and numbers are handled, so no Excel library is needed.
/// </summary>
public static class XlsxFile
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    // ---- Writing ----

    /// <summary>Writes one sheet. The first row is the header (bold, frozen). Cells that are whole numbers are written as numbers where <paramref name="numericColumns"/> says so.</summary>
    public static byte[] Write(string sheetName, IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlySet<int>? numericColumns = null)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddPart(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            AddPart(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            AddPart(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            AddPart(zip, "xl/workbook.xml", new XDocument(
                new XElement(XName.Get("workbook", MainNs),
                    new XAttribute(XNamespace.Xmlns + "r", RelNs),
                    new XElement(XName.Get("sheets", MainNs),
                        new XElement(XName.Get("sheet", MainNs),
                            new XAttribute("name", SheetName(sheetName)),
                            new XAttribute("sheetId", 1),
                            new XAttribute(XName.Get("id", RelNs), "rId1"))))));
            // Style 1 = bold, for the header row.
            AddPart(zip, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
                </styleSheet>
                """);
            AddPart(zip, "xl/worksheets/sheet1.xml", Worksheet(rows, numericColumns ?? new HashSet<int>()));
        }
        return output.ToArray();
    }

    private static XDocument Worksheet(IReadOnlyList<IReadOnlyList<string>> rows, IReadOnlySet<int> numericColumns)
    {
        XName N(string name) => XName.Get(name, MainNs);
        var columnCount = rows.Count == 0 ? 0 : rows.Max(r => r.Count);

        var sheetData = new XElement(N("sheetData"));
        for (var r = 0; r < rows.Count; r++)
        {
            var row = new XElement(N("row"), new XAttribute("r", r + 1));
            for (var c = 0; c < rows[r].Count; c++)
            {
                var value = Clean(rows[r][c]);
                if (value.Length == 0)
                    continue;
                var cell = new XElement(N("c"), new XAttribute("r", CellReference(c, r + 1)));
                if (r == 0)
                    cell.Add(new XAttribute("s", 1));
                if (r > 0 && numericColumns.Contains(c) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
                    cell.Add(new XElement(N("v"), number.ToString(CultureInfo.InvariantCulture)));
                else
                    cell.Add(new XAttribute("t", "inlineStr"),
                        new XElement(N("is"), new XElement(N("t"), new XAttribute(XNamespace.Xml + "space", "preserve"), value)));
                row.Add(cell);
            }
            sheetData.Add(row);
        }

        var worksheet = new XElement(N("worksheet"));
        if (rows.Count > 1)
            worksheet.Add(new XElement(N("sheetViews"),
                new XElement(N("sheetView"), new XAttribute("workbookViewId", 0),
                    new XElement(N("pane"), new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"),
                        new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))));
        if (columnCount > 0)
        {
            var cols = new XElement(N("cols"));
            for (var c = 0; c < columnCount; c++)
            {
                var longest = rows.Max(r => c < r.Count ? r[c].Length : 0);
                cols.Add(new XElement(N("col"), new XAttribute("min", c + 1), new XAttribute("max", c + 1),
                    new XAttribute("width", Math.Clamp(longest + 3, 10, 50)), new XAttribute("customWidth", 1)));
            }
            worksheet.Add(cols);
        }
        worksheet.Add(sheetData);
        return new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), worksheet);
    }

    private static void AddPart(ZipArchive zip, string path, string xml)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(xml.Trim());
    }

    private static void AddPart(ZipArchive zip, string path, XDocument xml)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Optimal).Open();
        xml.Save(stream, SaveOptions.DisableFormatting);
    }

    /// <summary>Removes characters XML cannot hold (control characters other than tab and line breaks).</summary>
    private static string Clean(string value) =>
        value.Any(c => c < 0x20 && c is not '\t' and not '\n' and not '\r')
            ? new string(value.Where(c => c >= 0x20 || c is '\t' or '\n' or '\r').ToArray())
            : value;

    /// <summary>Excel sheet names: at most 31 characters, none of : \ / ? * [ ].</summary>
    private static string SheetName(string name)
    {
        var safe = new string(name.Where(c => ":\\/?*[]".IndexOf(c) < 0).ToArray()).Trim();
        return safe.Length == 0 ? "Sheet1" : safe[..Math.Min(31, safe.Length)];
    }

    private static string CellReference(int column, int row)
    {
        var letters = "";
        for (var c = column + 1; c > 0; c = (c - 1) / 26)
            letters = (char)('A' + (c - 1) % 26) + letters;
        return letters + row.ToString(CultureInfo.InvariantCulture);
    }

    // ---- Reading ----

    public static TabularData Read(byte[] content)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
            var workbookPath = FindRelationship(zip, "_rels/.rels", "", "/officeDocument") ?? "xl/workbook.xml";
            var workbook = LoadPart(zip, workbookPath) ?? throw new TabularFormatException("The Excel file has no workbook.");
            var workbookRels = RelationshipsPath(workbookPath);
            var baseDir = Path.GetDirectoryName(workbookPath)?.Replace('\\', '/') ?? "";

            var firstSheet = workbook.Descendants().FirstOrDefault(e => e.Name.LocalName == "sheet")
                ?? throw new TabularFormatException("The Excel file has no sheets.");
            var sheetRelId = firstSheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id" && a.Name.Namespace != XNamespace.None)?.Value;
            var sheetPath = (sheetRelId is null ? null : FindRelationshipById(zip, workbookRels, baseDir, sheetRelId))
                ?? throw new TabularFormatException("The first sheet of the Excel file could not be found.");
            var sheet = LoadPart(zip, sheetPath) ?? throw new TabularFormatException("The first sheet of the Excel file could not be found.");

            var sharedStringsPath = FindRelationship(zip, workbookRels, baseDir, "/sharedStrings");
            var sharedStrings = (sharedStringsPath is null ? null : LoadPart(zip, sharedStringsPath))?.Root?.Elements()
                .Where(e => e.Name.LocalName == "si").Select(StringItemText).ToList() ?? [];

            return TabularData.FromRows(ReadRows(sheet, sharedStrings));
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            throw new TabularFormatException("The file is not a valid Excel (.xlsx) file.", ex);
        }
    }

    private static IEnumerable<TabularRow> ReadRows(XDocument sheet, List<string> sharedStrings)
    {
        var sheetData = sheet.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "sheetData");
        if (sheetData is null)
            yield break;

        var nextRow = 1;
        foreach (var row in sheetData.Elements().Where(e => e.Name.LocalName == "row"))
        {
            var rowNumber = int.TryParse(row.Attribute("r")?.Value, CultureInfo.InvariantCulture, out var r) ? r : nextRow;
            nextRow = rowNumber + 1;

            var cells = new List<string>();
            foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
            {
                var column = ColumnIndex(cell.Attribute("r")?.Value) ?? cells.Count;
                while (cells.Count <= column)
                    cells.Add("");
                cells[column] = CellText(cell, sharedStrings);
            }
            yield return new TabularRow(rowNumber, cells);
        }
    }

    private static string CellText(XElement cell, List<string> sharedStrings)
    {
        var value = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value ?? "";
        switch (cell.Attribute("t")?.Value)
        {
            case "s":
                return int.TryParse(value, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < sharedStrings.Count ? sharedStrings[index] : "";
            case "inlineStr":
                var inline = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "is");
                return inline is null ? value : StringItemText(inline);
            case "b":
                return value == "1" ? "TRUE" : "FALSE";
            default:
                return value;
        }
    }

    /// <summary>The text of a shared or inline string: all text runs, without phonetic hints.</summary>
    private static string StringItemText(XElement item) =>
        string.Concat(item.Descendants()
            .Where(e => e.Name.LocalName == "t" && !e.Ancestors().Any(a => a.Name.LocalName == "rPh"))
            .Select(e => e.Value));

    private static int? ColumnIndex(string? reference)
    {
        if (string.IsNullOrEmpty(reference))
            return null;
        var column = 0;
        var letters = 0;
        foreach (var c in reference)
        {
            if (c is >= 'A' and <= 'Z')
                column = column * 26 + (c - 'A' + 1);
            else if (c is >= 'a' and <= 'z')
                column = column * 26 + (c - 'a' + 1);
            else
                break;
            letters++;
        }
        return letters == 0 ? null : column - 1;
    }

    private static XDocument? LoadPart(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path.TrimStart('/'));
        if (entry is null)
            return null;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string RelationshipsPath(string partPath)
    {
        var dir = Path.GetDirectoryName(partPath)?.Replace('\\', '/') ?? "";
        var rels = $"_rels/{Path.GetFileName(partPath)}.rels";
        return dir.Length == 0 ? rels : $"{dir}/{rels}";
    }

    private static string? FindRelationship(ZipArchive zip, string relsPath, string baseDir, string typeSuffix) =>
        Relationships(zip, relsPath).FirstOrDefault(r => r.Type.EndsWith(typeSuffix, StringComparison.Ordinal)) is { } rel
            ? ResolveTarget(baseDir, rel.Target)
            : null;

    private static string? FindRelationshipById(ZipArchive zip, string relsPath, string baseDir, string id) =>
        Relationships(zip, relsPath).FirstOrDefault(r => r.Id == id) is { } rel ? ResolveTarget(baseDir, rel.Target) : null;

    private static IEnumerable<Relationship> Relationships(ZipArchive zip, string relsPath) =>
        LoadPart(zip, relsPath)?.Root?.Elements(XName.Get("Relationship", PackageRelNs))
            .Select(e => new Relationship((string?)e.Attribute("Id") ?? "", (string?)e.Attribute("Type") ?? "", (string?)e.Attribute("Target") ?? ""))
        ?? [];

    private sealed record Relationship(string Id, string Type, string Target);

    private static string ResolveTarget(string baseDir, string target)
    {
        // Targets are relative to the folder of the part, or to the package root when they start with '/'.
        var path = target.StartsWith('/') ? target.TrimStart('/') : baseDir.Length == 0 ? target : $"{baseDir}/{target}";
        var parts = path.Split('/').ToList();
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] == "..")
            {
                parts.RemoveAt(i);
                if (i > 0)
                    parts.RemoveAt(--i);
                i--;
            }
            else if (parts[i] == ".")
                parts.RemoveAt(i--);
        }
        return string.Join('/', parts);
    }
}
