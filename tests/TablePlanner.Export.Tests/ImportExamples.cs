using System.Text;
using TablePlanner.Core;
using TablePlanner.Export.Groups;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Tests;

/// <summary>
/// Example guest lists for the import wizard, made here so tests and the files in tests/testdata/imports stay the same.
/// Write the files with: <c>$env:TABLEPLANNER_GENERATE=1; dotnet test tests/TablePlanner.Export.Tests --filter ImportExamples</c>
/// </summary>
public static class ImportExamples
{
    public static IReadOnlyDictionary<string, byte[]> All() => new Dictionary<string, byte[]>
    {
        ["1 groups in the app's format.csv"] = GroupExporter.ToCsv(ExampleProject.Create().Setup),
        ["2 people with 3 levels.xlsx"] = PeopleWithLevels(updated: false),
        ["2b people with 3 levels, updated.xlsx"] = PeopleWithLevels(updated: true),
        ["3 mixed people and groups.csv"] = Mixed(),
        ["4 messy sheet.xlsx"] = Messy(),
        ["5 Danish Excel CSV.csv"] = DanishCsv(),
        ["6 errors on purpose.csv"] = Errors(),
        ["7 large, 700 people.xlsx"] = Large(),
    };

    private static string[] Row(params string[] cells) => cells;

    /// <summary>
    /// Afdeling | Team | Underteam | Navn | Kost: one row per person, some only in a department or a team. The updated
    /// version moves Mette to Marketing, removes Jonas, adds Sofie and a Jr. guest, and gives Lars a +1.
    /// </summary>
    public static byte[] PeopleWithLevels(bool updated)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            Row("Afdeling", "Team", "Underteam", "Navn", "Kost", "Antal"),
            Row("Salg", "Nord", "Key accounts", "Anna Holm", "", ""),
            Row("Salg", "Nord", "Key accounts", "Bo Jensen", "vegetar", ""),
            Row("Salg", "Nord", "", "Carl Nielsen", "", ""),
            Row("Salg", "Syd", "", "Dorte Lund", "glutenfri", ""),
            Row("Salg", "Syd", "", "Erik Bach", "", ""),
            Row("Salg", "", "", "Frida Salgschef", "", ""),
            Row("Marketing", "Web", "", "Gustav Ravn", "", ""),
            Row("Marketing", "Web", "", "Hanne Mø", "", ""),
            Row("Marketing", "Event", "", "Ida Berg", "", ""),
            Row("Økonomi", "", "", "Jonas Krog", "", ""),
            Row("Økonomi", "", "", "Karen Dahl", "laktosefri", ""),
            Row("Økonomi", "", "", "Lars Ager", "", updated ? "2" : ""),
            Row("Økonomi", "", "", "Mette Skov", "", ""),
        };
        if (updated)
        {
            rows.RemoveAll(r => r[3] is "Jonas Krog" or "Mette Skov");
            rows.Add(Row("Marketing", "Event", "", "Mette Skov", "", ""));
            rows.Add(Row("Salg", "Syd", "", "Sofie Lind", "", ""));
            rows.Add(Row("Salg", "Syd", "", "Erik Bach Jr.", "", ""));
        }
        return XlsxFile.Write("Gæsteliste", rows);
    }

    /// <summary>Afdeling;Team;Navn;Antal: people (one with a guest), and whole groups with only a number.</summary>
    public static byte[] Mixed() => CsvFile.Write(
    [
        Row("Afdeling", "Team", "Navn", "Antal"),
        Row("IT", "Drift", "Ole Hansen", ""),
        Row("IT", "Drift", "Pia Friis", "2"),
        Row("IT", "Udvikling", "Rasmus Lynge", ""),
        Row("IT", "Udvikling", "", "6"),
        Row("Leverandører", "", "", "8"),
        Row("Bestyrelsen", "", "", "5"),
    ]);

    /// <summary>Two sheets; the guest list has a title, a blank row and the header on row 3, odd column names and a column to ignore.</summary>
    public static byte[] Messy() => XlsxFile.Write(
    [
        new XlsxFile.SheetToWrite("Info", [Row("Julefrokost 2026"), Row("Spørgsmål: kontakt Lise")]),
        new XlsxFile.SheetToWrite("Gæster",
        [
            Row("Gæsteliste julefrokost 2026"),
            Row(""),
            Row("Dept.", "Team", "Fornavn", "Efternavn", "Antal pers.", "Telefon", "Bemærkninger"),
            Row("HR", "Rekruttering", "Sara", "Holm", "1", "12345678", ""),
            Row("HR", "Rekruttering", "Tim", "Krag", "1", "", "Kommer sent"),
            Row("HR", "Løn", "Ulla", "Bech", "1", "", ""),
            Row("HR", "Løn", "Viggo", "Ahl", "2", "", "Med ægtefælle"),
            Row("", "", "", "", "", "", ""),
            Row("Ledelse", "", "Winnie", "Thor", "1", "", ""),
        ]),
    ]);

    /// <summary>Gruppe;Antal;Kontaktperson;Ønske 1 with æøå, saved in Windows-1252 (Latin-1) as Danish Excel does.</summary>
    public static byte[] DanishCsv()
    {
        var text = string.Join("\r\n",
            "Gruppe;Antal;Kontaktperson;Ønske 1",
            "Søskende Ærø;6;Åse Øster;Fætre og kusiner",
            "Fætre og kusiner;9;Bjørn;",
            "Naboer på Møllevej;4;;Søskende Ærø",
            "Kollegaer;11;Jørgen;") + "\r\n";
        return Encoding.Latin1.GetBytes(text);
    }

    /// <summary>A missing number, a bad number, a duplicate name and a preference for an unknown group.</summary>
    public static byte[] Errors() => CsvFile.Write(
    [
        Row("Group", "People", "Preference 1"),
        Row("Rowing club", "", ""),
        Row("Book club", "lots", ""),
        Row("Choir", "12", "Orchestra"),
        Row("Choir", "8", ""),
        Row("Neighbours", "6", "Choir"),
    ]);

    /// <summary>About 700 people, one row each, in 5 departments with 3–5 teams, some teams with subteams.</summary>
    public static byte[] Large()
    {
        var random = new Random(707);
        string[] first = ["Anna", "Bo", "Carl", "Dorte", "Erik", "Frida", "Gustav", "Hanne", "Ida", "Jonas", "Karen", "Lars", "Mette", "Niels", "Olga", "Peter", "Rikke", "Søren", "Tina", "Ulrik"];
        string[] last = ["Hansen", "Jensen", "Nielsen", "Pedersen", "Andersen", "Larsen", "Sørensen", "Rasmussen", "Holm", "Bach", "Lund", "Krog"];
        var rows = new List<IReadOnlyList<string>> { Row("Department", "Team", "Subteam", "First name", "Last name", "Dietary") };
        var departments = new[] { "Sales", "Engineering", "Operations", "Finance", "People" };
        var people = 0;
        foreach (var department in departments)
            for (var t = 1; t <= 4 + random.Next(2); t++)
            {
                var subteams = random.NextDouble() < 0.4 ? new[] { "A", "B" } : new[] { "" };
                foreach (var sub in subteams)
                    for (var p = 0; p < 20 + random.Next(16) && people < 700; p++, people++)
                        rows.Add(Row(department, $"{department} {t}", sub.Length == 0 ? "" : $"{department} {t}{sub}",
                            first[random.Next(first.Length)], $"{last[random.Next(last.Length)]} {people}",
                            random.NextDouble() < 0.1 ? "vegetarian" : ""));
            }
        return XlsxFile.Write("Staff", rows);
    }
}

public class ImportExamplesFiles
{
    [Fact]
    public void Write_the_example_files_when_asked()
    {
        Assert.Equal(8, ImportExamples.All().Count);
        if (Environment.GetEnvironmentVariable("TABLEPLANNER_GENERATE") != "1")
            return;
        var folder = FindFolder();
        Directory.CreateDirectory(folder);
        foreach (var (name, bytes) in ImportExamples.All())
            File.WriteAllBytes(Path.Combine(folder, name), bytes);
    }

    private static string FindFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "tests")) && File.Exists(Path.Combine(dir.FullName, "TablePlanner.slnx")))
                return Path.Combine(dir.FullName, "tests", "testdata", "imports");
        throw new InvalidOperationException("Repository root not found.");
    }
}
