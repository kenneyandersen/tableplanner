using TablePlanner.Core;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;
using TablePlanner.Core.Validation;

namespace TablePlanner.Solver.Tests;

/// <summary>
/// Makes the generated projects in tests/testplans (next to real ones), for <see cref="TestPlanDiagnostics"/>.
/// Writes the files only when TABLEPLANNER_GENERATE=1, so the committed files stay as they are:
/// <c>$env:TABLEPLANNER_GENERATE=1; dotnet test tests/TablePlanner.Solver.Tests --filter TestPlanGenerator</c>
/// Without it, only checks that every generated setup is valid.
/// </summary>
public class TestPlanGenerator
{
    public static IEnumerable<object[]> Names() => Generators.Keys.Select(k => new object[] { k });

    private static readonly Dictionary<string, Func<EventProject>> Generators = new()
    {
        // The venues are in Core (TestProjects, also behind "Create test projects" in the app); a fixed seed and fill
        // for each makes them repeatable.
        ["Wedding 176"] = () => TestProjects.Wedding(new Random(101), 176.0 / 178),
        ["Company dinner 430"] = () => TestProjects.CompanyDinner(new Random(202), 430.0 / 458),
        ["Club party 220 full"] = () => TestProjects.ClubParty(new Random(303), 1.0),
        ["Conference lunch 96"] = () => TestProjects.ConferenceLunch(new Random(404), 96.0 / 110),
        ["Festival 1080"] = () => TestProjects.Festival(new Random(505), 1080.0 / 1165),
        ["Company levels 440"] = () => TestProjects.CompanyLevels(new Random(606), 440.0 / 467),
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void Generate(string name)
    {
        var project = Generators[name]();
        project.Name = name;

        var validation = SetupValidator.Validate(project.Setup);
        Assert.Empty(validation.Errors);
        Assert.DoesNotContain(validation.Issues, i => i.Code == IssueCode.TablesOverlap);
        var seats = project.Setup.AllTables.Sum(t => t.AvailableSeatCount);
        Assert.InRange(project.Setup.Groups.Sum(g => g.PeopleCount), 1, seats);

        if (Environment.GetEnvironmentVariable("TABLEPLANNER_GENERATE") != "1")
            return;
        var folder = Path.Combine(TestPlans.Folder() ?? throw new InvalidOperationException("tests/testplans not found"), name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, $"{name}.tableplanner.json"), ProjectSerializer.SerializeFile(project, []));
    }

}
