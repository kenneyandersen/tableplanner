using System.Text;
using TablePlanner.Export.Groups;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Tests;

public class GroupFileTests
{
    private static GroupFile Csv(string text) => GroupFile.Parse(CsvFile.Read(text));

    [Fact]
    public void Reads_all_columns()
    {
        var file = Csv("""
            Name;People;Contact person;Dietary info;Colour;Notes;Preference 1;Preference 2
            Smith family;6;Anna Smith;2 vegetarian;#3366CC;Near the exit;Jones;Brown
            """);

        var group = Assert.Single(file.Groups);
        Assert.Empty(file.Errors);
        Assert.Empty(file.Warnings);
        Assert.Equal(new ImportedGroup(2, "Smith family", 6, "Anna Smith", "2 vegetarian", "#3366cc", "Near the exit", group.Preferences),
            group);
        Assert.Equal(["Jones", "Brown"], group.Preferences);
        Assert.True(file.HasPreferences);
    }

    [Fact]
    public void Only_name_and_people_are_required()
    {
        var file = Csv("Name;People\nA;4");

        Assert.Empty(file.Errors);
        Assert.Equal([GroupColumn.Name, GroupColumn.People], file.Columns.Order());
        Assert.False(file.HasPreferences);
    }

    [Theory]
    [InlineData("group name;SIZE", "A;4")]
    [InlineData("Group,Persons,Color,Pref 1", "A,4,#fff,B")]
    [InlineData(" name ; people_count ; dietary ; sit-with-1", "A;4;none;B")]
    public void Accepts_common_header_variants(string header, string row)
    {
        var file = Csv($"{header}\n{row}");

        Assert.Empty(file.Errors);
        Assert.Equal(4, Assert.Single(file.Groups).PeopleCount);
    }

    [Fact]
    public void Missing_required_columns_is_a_file_error()
    {
        var file = Csv("Group;Notes\nA;x");

        var error = Assert.Single(file.Errors);
        Assert.Null(error.RowNumber);
        Assert.Contains("'People'", error.Text);
        Assert.Empty(file.Groups);
    }

    [Fact]
    public void Unknown_and_duplicate_columns_are_warnings()
    {
        var file = Csv("Name;People;Table;Name\nA;4;12;B");

        Assert.Equal("A", Assert.Single(file.Groups).Name);
        Assert.Contains(file.Warnings, w => w.Text.Contains("'Table'"));
        Assert.Contains(file.Warnings, w => w.Text.Contains("more than once"));
    }

    [Theory]
    [InlineData("4", 4)]
    [InlineData(" 12 ", 12)]
    [InlineData("12.0", 12)]
    [InlineData("12,0", 12)]
    public void Reads_whole_numbers_of_people(string text, int expected)
    {
        Assert.Equal(expected, Assert.Single(Csv($"Name;People\nA;\"{text}\"").Groups).PeopleCount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("2.5")]
    [InlineData("many")]
    [InlineData("")]
    public void Invalid_people_count_skips_the_row(string text)
    {
        var file = Csv($"Name;People\nA;{text}\nB;2");

        Assert.Equal("B", Assert.Single(file.Groups).Name);
        Assert.Equal(2, Assert.Single(file.Errors).RowNumber);
        Assert.Equal(["A"], file.SkippedNames);
    }

    [Fact]
    public void Row_without_name_is_an_error_but_blank_rows_are_ignored()
    {
        var file = Csv("Name;People\n;4\n;\nB;2");

        var error = Assert.Single(file.Errors);
        Assert.Equal(2, error.RowNumber);
        Assert.Equal("Row 2: The name is missing.", error.ToString());
        Assert.Empty(file.SkippedNames);
    }

    [Fact]
    public void A_name_used_twice_skips_the_later_row()
    {
        var file = Csv("Name;People\nSmith;4\n smith ;2");

        Assert.Equal(4, Assert.Single(file.Groups).PeopleCount);
        Assert.Contains("already on row 2", Assert.Single(file.Errors).Text);
    }

    [Theory]
    [InlineData("#AABBCC", "#aabbcc")]
    [InlineData("aabbcc", "#aabbcc")]
    [InlineData("#abc", "#aabbcc")]
    public void Normalizes_colours(string text, string expected)
    {
        Assert.Equal(expected, Assert.Single(Csv($"Name;People;Colour\nA;4;{text}").Groups).Colour);
    }

    [Fact]
    public void Invalid_colour_is_a_warning_and_uses_an_automatic_colour()
    {
        var file = Csv("Name;People;Colour\nA;4;reddish");

        Assert.Null(Assert.Single(file.Groups).Colour);
        Assert.Equal(2, Assert.Single(file.Warnings).RowNumber);
    }

    [Fact]
    public void Blank_optional_cells_are_null_and_blank_preferences_are_skipped()
    {
        var group = Assert.Single(Csv("Name;People;Notes;Preference 1;Preference 2;Preference 3\nA;4; ;;B;").Groups);

        Assert.Null(group.Notes);
        Assert.Equal(["B"], group.Preferences);
    }

    [Fact]
    public void Empty_file_and_header_only_file_are_errors()
    {
        Assert.Contains("empty", Assert.Single(Csv("").Errors).Text);
        Assert.Contains("no groups", Assert.Single(Csv("Name;People\n").Errors).Text);
    }

    [Fact]
    public void Read_picks_the_format_from_the_file_name()
    {
        var xlsx = XlsxFile.Write("Groups", [["Name", "People"], ["A", "4"]]);
        var csv = Encoding.UTF8.GetBytes("Name;People\nB;2");

        Assert.Equal("A", Assert.Single(GroupFile.Read("guests.XLSX", xlsx).Groups).Name);
        Assert.Equal("B", Assert.Single(GroupFile.Read("guests.csv", csv).Groups).Name);
        Assert.Equal("B", Assert.Single(GroupFile.Read("guests.txt", csv).Groups).Name);
    }

    [Fact]
    public void Read_reports_unreadable_files_instead_of_throwing()
    {
        Assert.Contains(".xls", Assert.Single(GroupFile.Read("old.xls", [1, 2, 3]).Errors).Text);
        Assert.Contains("not a valid Excel", Assert.Single(GroupFile.Read("broken.xlsx", [1, 2, 3]).Errors).Text);
    }
}
