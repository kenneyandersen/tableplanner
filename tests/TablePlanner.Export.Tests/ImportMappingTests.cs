using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;
using TablePlanner.Export.Groups;

namespace TablePlanner.Export.Tests;

public class ImportMappingTests
{
    /// <summary>Reads an example the way the wizard does with its guesses: first sheet with rows, guessed header row and roles.</summary>
    private static (GroupFile File, List<ColumnRole> Roles, int HeaderRow) Read(string name)
    {
        var bytes = ImportExamples.All()[name];
        var sheets = ImportMapping.ReadSheets(name, bytes);
        var sheet = sheets.FirstOrDefault(s => s.Rows.Count > 3) ?? sheets[0];
        var headerRow = ImportMapping.GuessHeaderRow(sheet.Rows);
        var data = ImportMapping.WithHeaderAt(sheet.Rows, headerRow);
        var roles = ImportMapping.Guess(data.Header);
        return (ImportMapping.Build(data, roles), roles, headerRow);
    }

    private static EventProject Import(GroupFile file, EventProject? into = null)
    {
        var project = into ?? new EventProject();
        GroupImport.Apply(project, file, GroupImportMode.Merge);
        return project;
    }

    private static Group Named(EventProject project, string name) => project.Setup.Groups.Single(g => g.Name == name);

    [Fact]
    public void Danish_headers_are_recognised()
    {
        var roles = ImportMapping.Guess(["Afdeling", "Team", "Underteam", "Navn", "Kost", "Antal", "Telefon"]);

        Assert.Equal([ColumnRole.Level1, ColumnRole.Level2, ColumnRole.Level3, ColumnRole.PersonName, ColumnRole.DietaryInfo, ColumnRole.People, ColumnRole.Ignore], roles);
    }

    [Fact]
    public void Name_is_the_group_in_a_plain_group_list_and_a_person_next_to_a_group_column()
    {
        Assert.Equal([ColumnRole.GroupName, ColumnRole.People], ImportMapping.Guess(["Name", "People"]));
        Assert.Equal([ColumnRole.GroupName, ColumnRole.PersonName], ImportMapping.Guess(["Gruppe", "Navn"]));
    }

    [Fact]
    public void The_apps_own_export_is_read_as_before()
    {
        var (file, roles, headerRow) = Read("1 groups in the app's format.csv");

        Assert.Equal(1, headerRow);
        Assert.Equal(ColumnRole.GroupName, roles[0]);
        Assert.Empty(file.Errors);
        var project = Import(file);
        var example = Core.ExampleProject.Create();
        Assert.Equal(example.Setup.Groups.Select(g => (g.Name, g.PeopleCount)), project.Setup.Groups.Select(g => (g.Name, g.PeopleCount)));
    }

    [Fact]
    public void People_with_three_levels_make_a_tree_with_names_and_other_groups()
    {
        var (file, _, _) = Read("2 people with 3 levels.xlsx");

        Assert.Empty(file.Errors);
        var project = Import(file);
        var setup = project.Setup;
        Assert.Empty(SetupValidator.Validate(setup).Errors);
        var keyAccounts = Named(project, "Key accounts");
        Assert.Equal("Salg › Nord › Key accounts", setup.PathOf(keyAccounts));
        Assert.Equal(["Anna Holm", "Bo Jensen"], keyAccounts.Members.Select(m => m.Name));
        Assert.Equal("Bo Jensen: vegetar", keyAccounts.DietaryInfo);
        // Carl is in team Nord, which has a subteam, so he goes to "Nord – other"; Frida only in Salg goes to "Salg – other".
        Assert.Equal(["Carl Nielsen"], Named(project, "Nord – other").Members.Select(m => m.Name));
        Assert.Equal(["Frida Salgschef"], Named(project, "Salg – other").Members.Select(m => m.Name));
        // Økonomi has no teams, so it is a group of its own with 4 people.
        Assert.Equal(4, Named(project, "Økonomi").PeopleCount);
        Assert.Equal(13, setup.Groups.Sum(g => g.PeopleCount));
    }

    [Fact]
    public void Importing_the_updated_list_moves_adds_and_removes_people()
    {
        var project = Import(Read("2 people with 3 levels.xlsx").File);
        var mette = Named(project, "Økonomi").Members.Single(m => m.Name == "Mette Skov").Id;

        Import(Read("2b people with 3 levels, updated.xlsx").File, project);

        Assert.Equal(["Karen Dahl", "Lars Ager"], Named(project, "Økonomi").Members.Select(m => m.Name));
        // Lars brings a guest: 2 named + 1 guest. The group never shrinks by itself, so it stays at 4 (the unnamed seats stay).
        Assert.True(Named(project, "Økonomi").PeopleCount >= 3);
        Assert.Contains("Mette Skov", Named(project, "Event").Members.Select(m => m.Name));
        Assert.NotEqual(mette, Named(project, "Event").Members.Single(m => m.Name == "Mette Skov").Id);
        Assert.Equal(["Dorte Lund", "Erik Bach", "Sofie Lind", "Erik Bach Jr."], Named(project, "Syd").Members.Select(m => m.Name));
    }

    [Fact]
    public void Mixed_rows_count_people_parties_and_groups()
    {
        var (file, roles, _) = Read("3 mixed people and groups.csv");

        Assert.Equal([ColumnRole.Level1, ColumnRole.Level2, ColumnRole.PersonName, ColumnRole.People], roles);
        var project = Import(file);
        Assert.Equal(3, Named(project, "Drift").PeopleCount); // Ole, and Pia with a guest
        Assert.Equal(7, Named(project, "Udvikling").PeopleCount); // Rasmus and 6 more
        Assert.Equal(["Rasmus Lynge"], Named(project, "Udvikling").Members.Select(m => m.Name));
        Assert.Equal(8, Named(project, "Leverandører").PeopleCount);
        Assert.Null(Named(project, "Leverandører").ParentId);
    }

    [Fact]
    public void A_messy_sheet_finds_its_header_and_joins_first_and_last_names()
    {
        var sheets = ImportMapping.ReadSheets("x.xlsx", ImportExamples.Messy());
        Assert.Equal(["Info", "Gæster"], sheets.Select(s => s.Name));

        var (file, roles, headerRow) = Read("4 messy sheet.xlsx");

        Assert.Equal(3, headerRow);
        Assert.Equal([ColumnRole.Level1, ColumnRole.Level2, ColumnRole.FirstName, ColumnRole.LastName, ColumnRole.People, ColumnRole.Ignore, ColumnRole.Notes], roles);
        var project = Import(file);
        Assert.Equal(["Ulla Bech", "Viggo Ahl"], Named(project, "Løn").Members.Select(m => m.Name));
        Assert.Equal(3, Named(project, "Løn").PeopleCount);
        Assert.Equal("Tim Krag: Kommer sent", Named(project, "Rekruttering").Notes);
        Assert.Equal(["Winnie Thor"], Named(project, "Ledelse").Members.Select(m => m.Name));
    }

    [Fact]
    public void A_Danish_Excel_csv_keeps_its_letters()
    {
        var (file, roles, _) = Read("5 Danish Excel CSV.csv");

        Assert.Equal([ColumnRole.GroupName, ColumnRole.People, ColumnRole.ContactPerson, ColumnRole.Preference], roles);
        var project = Import(file);
        Assert.Equal("Åse Øster", Named(project, "Søskende Ærø").ContactPerson);
        Assert.Equal([Named(project, "Fætre og kusiner").Id], Named(project, "Søskende Ærø").PreferredGroupIds);
    }

    [Fact]
    public void Errors_on_purpose_are_reported()
    {
        var (file, _, _) = Read("6 errors on purpose.csv");

        Assert.Contains(file.Errors, e => e.Text.Contains("missing"));
        Assert.Contains(file.Errors, e => e.Text.Contains("lots"));
        Assert.Contains(file.Errors, e => e.Text.Contains("already on row"));
        var result = GroupImport.Preview(new EventProject(), file, GroupImportMode.Merge);
        Assert.Contains(result.Warnings, w => w.Text.Contains("Orchestra"));
    }

    [Fact]
    public void A_large_list_of_700_people_is_read()
    {
        var (file, roles, _) = Read("7 large, 700 people.xlsx");

        Assert.Equal(ColumnRole.FirstName, roles[3]);
        Assert.Empty(file.Errors);
        var project = Import(file);
        Assert.Equal(700, project.Setup.Groups.Sum(g => g.PeopleCount));
        Assert.Equal(700, project.Setup.Groups.Sum(g => g.Members.Count));
        Assert.Empty(SetupValidator.Validate(project.Setup).Errors);
        Assert.Equal(5, project.Setup.Groups.Count(g => g.ParentId is null));
    }

    [Fact]
    public void A_person_without_any_group_is_an_error()
    {
        var data = ImportMapping.WithHeaderAt(Tabular.CsvFile.ReadRows("Team;Name\n;Ann\nA;Bo\n"u8.ToArray()), 1);

        var file = ImportMapping.Build(data, [ColumnRole.GroupName, ColumnRole.PersonName]);

        Assert.Contains(file.Errors, e => e.Text.Contains("Ann"));
        Assert.Single(file.Groups);
    }
}
