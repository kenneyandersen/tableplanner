using TablePlanner.Core;
using TablePlanner.Core.Model;
using TablePlanner.Export.Groups;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Tests;

public class GroupImportTests
{
    /// <summary>Groups A (4) and B (6), where A would like to sit with B, and a table with A pinned to it.</summary>
    private static EventProject Project()
    {
        var a = new Group { Name = "A", PeopleCount = 4, ContactPerson = "Anna", Colour = "#ff0000" };
        var b = new Group { Name = "B", PeopleCount = 6 };
        a.PreferredGroupIds = [b.Id];
        var table = new Table { Number = 101, SeatsPerSide = 5 };
        var project = new EventProject { Name = "Test" };
        project.Setup.Floors.Add(new Floor { Name = "Ground", Number = 1, Tables = [table] });
        project.Setup.Groups.AddRange([a, b]);
        project.Setup.Pins.Add(new Pin { GroupId = a.Id, Scope = PinScope.Table, TableId = table.Id });
        return project;
    }

    private static GroupFile Csv(string text) => GroupFile.Parse(CsvFile.Read(text));

    private static Group Named(EventProject project, string name) => project.Setup.Groups.Single(g => g.Name == name);

    [Fact]
    public void Merge_updates_by_name_and_keeps_ids()
    {
        var project = Project();
        var idOfA = Named(project, "A").Id;

        var result = GroupImport.Apply(project, Csv("Name;People\n a ;5"), GroupImportMode.Merge);

        var a = Named(project, "a");
        Assert.Equal(idOfA, a.Id);
        Assert.Equal(5, a.PeopleCount);
        var change = Assert.Single(result.Changes);
        Assert.Equal(GroupChangeKind.Updated, change.Kind);
        Assert.Equal(["Name 'A' → 'a'", "People 4 → 5"], change.Details);
    }

    [Fact]
    public void Merge_leaves_columns_that_are_not_in_the_file()
    {
        var project = Project();

        GroupImport.Apply(project, Csv("Name;People;Notes\nA;4;hello"), GroupImportMode.Merge);

        var a = Named(project, "A");
        Assert.Equal("hello", a.Notes);
        Assert.Equal("Anna", a.ContactPerson);
        Assert.Equal("#ff0000", a.Colour);
        Assert.Single(a.PreferredGroupIds);
    }

    [Fact]
    public void A_column_in_the_file_with_an_empty_cell_clears_the_value()
    {
        var project = Project();

        GroupImport.Apply(project, Csv("Name;People;Contact person;Colour\nA;4;;"), GroupImportMode.Merge);

        Assert.Null(Named(project, "A").ContactPerson);
        Assert.Null(Named(project, "A").Colour);
    }

    [Fact]
    public void Merge_adds_new_groups_and_keeps_groups_not_in_the_file()
    {
        var project = Project();

        var result = GroupImport.Apply(project, Csv("Name;People\nC;3"), GroupImportMode.Merge);

        Assert.Equal(["A", "B", "C"], project.Setup.Groups.Select(g => g.Name));
        Assert.Equal(GroupChangeKind.Added, Assert.Single(result.Changes).Kind);
        Assert.Single(project.Setup.Pins);
    }

    [Fact]
    public void Replace_removes_groups_not_in_the_file_with_their_pins_and_preferences()
    {
        var project = Project();

        var result = GroupImport.Apply(project, Csv("Name;People\nB;6\nC;3"), GroupImportMode.Replace);

        Assert.Equal(["B", "C"], project.Setup.Groups.Select(g => g.Name));
        Assert.Empty(project.Setup.Pins);
        Assert.Equal(1, result.Count(GroupChangeKind.Removed));
        Assert.Equal(1, result.Count(GroupChangeKind.Added));
        Assert.Equal(1, result.Count(GroupChangeKind.Unchanged));
    }

    [Fact]
    public void Replace_keeps_a_group_whose_row_has_errors()
    {
        var project = Project();

        var result = GroupImport.Apply(project, Csv("Name;People\nA;lots\nB;6"), GroupImportMode.Replace);

        Assert.Equal(4, Named(project, "A").PeopleCount);
        Assert.Single(result.Errors);
        Assert.Contains(result.Warnings, w => w.Text.Contains("'A' is kept"));
        Assert.Equal(0, result.Count(GroupChangeKind.Removed));
    }

    [Fact]
    public void Preferences_are_resolved_by_name_including_groups_later_in_the_file()
    {
        var project = Project();

        GroupImport.Apply(project, Csv("Name;People;Preference 1;Preference 2\nC;3;d;b\nD;2;C;"), GroupImportMode.Merge);

        Assert.Equal([Named(project, "D").Id, Named(project, "B").Id], Named(project, "C").PreferredGroupIds);
        Assert.Equal([Named(project, "C").Id], Named(project, "D").PreferredGroupIds);
    }

    [Fact]
    public void Preference_columns_replace_the_preferences_of_existing_groups()
    {
        var project = Project();

        var result = GroupImport.Apply(project, Csv("Name;People;Preference 1\nA;4;"), GroupImportMode.Merge);

        Assert.Empty(Named(project, "A").PreferredGroupIds);
        Assert.Equal(["Preferences"], Assert.Single(result.Changes).Details);
    }

    [Fact]
    public void Unknown_self_and_duplicate_preferences_are_left_out()
    {
        var project = Project();

        var result = GroupImport.Apply(project, Csv("Name;People;Preference 1;Preference 2;Preference 3;Preference 4\nA;4;Nobody;A;B;b"), GroupImportMode.Merge);

        Assert.Equal([Named(project, "B").Id], Named(project, "A").PreferredGroupIds);
        Assert.Equal(2, result.Warnings.Count);
        Assert.All(result.Warnings, w => Assert.Equal(2, w.RowNumber));
    }

    [Fact]
    public void Preview_does_not_change_the_project()
    {
        var project = Project();

        var result = GroupImport.Preview(project, Csv("Name;People\nA;9\nC;3"), GroupImportMode.Replace);

        Assert.Equal(1, result.Count(GroupChangeKind.Updated));
        Assert.Equal(1, result.Count(GroupChangeKind.Added));
        Assert.Equal(1, result.Count(GroupChangeKind.Removed));
        Assert.Equal(["A", "B"], project.Setup.Groups.Select(g => g.Name));
        Assert.Equal(4, Named(project, "A").PeopleCount);
    }

    [Fact]
    public void Importing_an_export_into_the_same_project_changes_nothing()
    {
        var project = ExampleProject.Create();
        var file = GroupFile.Read("groups.xlsx", GroupExporter.ToXlsx(project.Setup));

        var result = GroupImport.Apply(project, file, GroupImportMode.Replace);

        Assert.False(result.HasChanges);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("xlsx")]
    public void Export_then_import_into_an_empty_project_recreates_the_groups(string format)
    {
        var source = ExampleProject.Create();
        source.Setup.Groups[0].Notes = "Line 1\nLine 2; \"quoted\"";
        var bytes = format == "csv" ? GroupExporter.ToCsv(source.Setup) : GroupExporter.ToXlsx(source.Setup);
        var target = new EventProject();

        var result = GroupImport.Apply(target, GroupFile.Read($"groups.{format}", bytes), GroupImportMode.Merge);

        Assert.Equal(source.Setup.Groups.Count, result.Count(GroupChangeKind.Added));
        foreach (var (expected, actual) in source.Setup.Groups.Zip(target.Setup.Groups))
        {
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.PeopleCount, actual.PeopleCount);
            Assert.Equal(expected.ContactPerson, actual.ContactPerson);
            Assert.Equal(expected.DietaryInfo, actual.DietaryInfo);
            Assert.Equal(expected.Colour, actual.Colour);
            Assert.Equal(expected.Notes, actual.Notes);
            Assert.Equal(
                expected.PreferredGroupIds.Select(id => source.Setup.FindGroup(id)!.Name),
                actual.PreferredGroupIds.Select(id => target.Setup.FindGroup(id)!.Name));
        }
    }

    [Fact]
    public void Export_writes_a_header_and_preferences_as_names()
    {
        var project = Project();

        var rows = GroupExporter.ToRows(project.Setup);

        Assert.Equal(["Name", "People", "Contact person", "Dietary info", "Colour", "Notes", "Preference 1", "Preference 2", "Preference 3", "Preference 4", "Preference 5"], rows[0]);
        Assert.Equal(["A", "4", "Anna", "", "#ff0000", "", "B", "", "", "", ""], rows[1]);
    }
}
