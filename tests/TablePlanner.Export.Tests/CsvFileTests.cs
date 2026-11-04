using System.Text;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Tests;

public class CsvFileTests
{
    [Fact]
    public void Writes_semicolons_with_a_utf8_byte_order_mark()
    {
        var bytes = CsvFile.Write([["Name", "People"], ["Søren", "4"]]);

        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        Assert.Equal("Name;People\r\nSøren;4\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void Quotes_cells_that_need_it()
    {
        var bytes = CsvFile.Write([["a;b", "say \"hi\"", "two\nlines", " padded", "plain"]]);

        Assert.Equal("\"a;b\";\"say \"\"hi\"\"\";\"two\nlines\";\" padded\";plain\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void Round_trips_awkward_cells()
    {
        string[][] rows = [["Name", "Notes"], ["Æble; \"Ø\"", "line 1\r\nline 2"], [" x ", ""]];

        var data = CsvFile.Read(CsvFile.Write(rows));

        Assert.Equal(rows[0], data.Header);
        Assert.Equal(rows[1], data.Rows[0].Cells);
        Assert.Equal(rows[2], data.Rows[1].Cells);
    }

    [Theory]
    [InlineData("Name;People\nA;4")]
    [InlineData("Name,People\nA,4")]
    [InlineData("Name\tPeople\nA\t4")]
    [InlineData("\"Name, with comma\";People\nA;4")]
    public void Detects_the_separator_from_the_header(string text)
    {
        var data = CsvFile.Read(text);

        Assert.Equal(2, data.Header.Count);
        Assert.Equal(["A", "4"], data.Rows[0].Cells);
    }

    [Fact]
    public void Row_numbers_match_the_spreadsheet_and_blank_rows_are_left_out()
    {
        var data = CsvFile.Read("Name;People\r\nA;4\r\n;\r\n\r\nB;2");

        Assert.Equal([2, 5], data.Rows.Select(r => r.RowNumber));
    }

    [Fact]
    public void Reads_a_header_that_starts_after_blank_rows()
    {
        var data = CsvFile.Read("\n\nName;People\nA;4\n");

        Assert.Equal(["Name", "People"], data.Header);
        Assert.Single(data.Rows);
    }

    [Fact]
    public void Reads_utf8_without_byte_order_mark()
    {
        var data = CsvFile.Read(Encoding.UTF8.GetBytes("Name\nÅse"));

        Assert.Equal("Åse", data.Rows[0].Cell(0));
    }

    [Fact]
    public void Falls_back_to_latin1_for_files_saved_in_the_windows_code_page()
    {
        var data = CsvFile.Read(Encoding.Latin1.GetBytes("Name\nSøren Æblø"));

        Assert.Equal("Søren Æblø", data.Rows[0].Cell(0));
    }

    [Fact]
    public void Missing_cells_read_as_empty()
    {
        var data = CsvFile.Read("A;B;C\n1");

        Assert.Equal("", data.Rows[0].Cell(2));
    }
}
