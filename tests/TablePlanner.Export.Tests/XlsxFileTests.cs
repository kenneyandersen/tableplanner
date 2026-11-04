using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Tests;

public class XlsxFileTests
{
    [Fact]
    public void Round_trips_text_and_numbers()
    {
        string[][] rows = [["Name", "People", "Notes"], ["Søren & Co <3", "12", " spaced "], ["B", "4", ""]];

        var data = XlsxFile.Read(XlsxFile.Write("Groups", rows, new HashSet<int> { 1 }));

        Assert.Equal(rows[0], data.Header);
        Assert.Equal(["Søren & Co <3", "12", " spaced "], data.Rows[0].Cells);
        Assert.Equal(["B", "4"], data.Rows[1].Cells);
        Assert.Equal([2, 3], data.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void Writes_numeric_columns_as_numbers_and_the_rest_as_text()
    {
        var bytes = XlsxFile.Write("Groups", [["Name", "People"], ["007", "12"]], new HashSet<int> { 1 });

        var cells = Sheet(bytes).Descendants().Where(e => e.Name.LocalName == "c").ToList();
        Assert.Equal("inlineStr", (string?)cells.Single(c => (string?)c.Attribute("r") == "A2").Attribute("t"));
        Assert.Null(cells.Single(c => (string?)c.Attribute("r") == "B2").Attribute("t"));
    }

    [Fact]
    public void Removes_characters_xml_cannot_hold()
    {
        var data = XlsxFile.Read(XlsxFile.Write("Groups", [["Name"], ["a\u0001b"]]));

        Assert.Equal("ab", data.Rows[0].Cell(0));
    }

    [Fact]
    public void Reads_a_file_laid_out_like_excel_writes_it()
    {
        // Shared strings with rich text and phonetic hints, a sheet in a non-default place, gaps between
        // cells and rows, formulas with cached values and booleans.
        var bytes = Zip(new()
        {
            ["_rels/.rels"] = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""",
            ["xl/workbook.xml"] = """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Guests" sheetId="3" r:id="rId7"/><sheet name="Other" sheetId="1" r:id="rId1"/></sheets></workbook>""",
            ["xl/_rels/workbook.xml.rels"] = """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId7" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="/xl/worksheets/../worksheets/guests.xml"/><Relationship Id="rId9" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings" Target="sharedStrings.xml"/></Relationships>""",
            ["xl/sharedStrings.xml"] = """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Name</t></si><si><t>People</t></si><si><r><t>Søren </t></r><r><rPr><b/></rPr><t>&amp; Co</t></r><rPh><t>ignored</t></rPh></si></sst>""",
            ["xl/worksheets/sheet1.xml"] = """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="inlineStr"><is><t>Wrong sheet</t></is></c></row></sheetData></worksheet>""",
            ["xl/worksheets/guests.xml"] = """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                <row r="2"><c r="B2" t="s"><v>0</v></c><c r="D2" t="s"><v>1</v></c></row>
                <row r="5"><c r="B5" t="s"><v>2</v></c><c r="D5"><f>2*3</f><v>6</v></c><c r="E5" t="b"><v>1</v></c></row>
                </sheetData></worksheet>
                """,
        });

        var data = XlsxFile.Read(bytes);

        Assert.Equal(["", "Name", "", "People"], data.Header);
        var row = Assert.Single(data.Rows);
        Assert.Equal(5, row.RowNumber);
        Assert.Equal(["", "Søren & Co", "", "6", "TRUE"], row.Cells);
    }

    [Fact]
    public void A_file_that_is_not_xlsx_gives_a_readable_error()
    {
        var ex = Assert.Throws<TabularFormatException>(() => XlsxFile.Read(Encoding.UTF8.GetBytes("Name;People")));

        Assert.Contains("not a valid Excel", ex.Message);
    }

    [Fact]
    public void A_zip_without_a_workbook_gives_a_readable_error()
    {
        Assert.Throws<TabularFormatException>(() => XlsxFile.Read(Zip(new() { ["hello.txt"] = "hi" })));
    }

    private static XDocument Sheet(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        return XDocument.Load(stream);
    }

    private static byte[] Zip(Dictionary<string, string> parts)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, content) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content.Trim());
            }
        return output.ToArray();
    }
}
