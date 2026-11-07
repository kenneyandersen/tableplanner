using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;
using Xunit.Abstractions;

namespace TablePlanner.Solver.Tests;

/// <summary>
/// Runs the solver on the real projects in tests/testplans and prints the warnings, for tuning. Run with:
/// dotnet test tests/TablePlanner.Solver.Tests -c Release --filter TestPlanDiagnostics --logger "console;verbosity=detailed"
/// </summary>
public class TestPlanDiagnostics(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Plans() =>
        TestPlans.Files().Select(f => new object[] { Path.GetFileName(f) }).DefaultIfEmpty(["(none)"]);

    [Theory]
    [MemberData(nameof(Plans))]
    public async Task Report(string file)
    {
        if (file == "(none)")
            return;
        var project = TestPlans.Load(file);
        var problem = SeatingProblem.Create(project.Setup, project.Priorities);
        output.WriteLine($"{file}: {problem.PeopleCount} people, {problem.Graph.Seats.Count} seats, {problem.Groups.Count} groups");
        foreach (var t in problem.Graph.Tables)
            output.WriteLine($"  table {t.Table.Number} ({t.Seats.Count}) next to {string.Join(",", problem.Graph.Tables.Where(o => problem.Graph.AreAdjacent(t.Index, o.Index)).Select(o => o.Table.Number))}");

        var evaluator = new PlanEvaluator(problem);
        Print("saved plan", evaluator.Summarize(problem.ToPlan(project.CurrentAssignment)), problem, problem.ToPlan(project.CurrentAssignment));

        // Seeded (repeatable) runs of the search at three lengths and of the beam (widths 4, 16, 64); then runs limited
        // by time only, as in the app (a native second is several in the browser).
        var runs = new (string Name, SolverMethod Method, int Iterations, double Seconds, bool Seeded, bool ThenSearch)[]
        {
            ("search 20k", SolverMethod.Search, 20_000, 60, true, true),
            ("search 100k", SolverMethod.Search, 100_000, 60, true, true),
            ("search 500k", SolverMethod.Search, 500_000, 60, true, true),
            ("beam only", SolverMethod.Beam, 5_000_000, 60, true, false),
            ("beam+search 100k", SolverMethod.Beam, 100_000, 60, true, true),
            ("search 2s", SolverMethod.Search, 5_000_000, 2, false, true),
            ("beam only 2s", SolverMethod.Beam, 5_000_000, 2, false, false),
            ("beam+search 2s", SolverMethod.Beam, 5_000_000, 2, false, true),
        };
        foreach (var run in runs)
        {
            var (warnings, apart, score) = (0, 0, 0.0);
            for (var seed = 1; seed <= 5; seed++)
            {
                var result = await SeatingSolver.SolveAsync(project, new SolverOptions
                {
                    TimeBudget = TimeSpan.FromSeconds(run.Seconds), MaxIterations = run.Iterations, Seed = run.Seeded ? seed : null,
                    Method = run.Method, BeamOnly = !run.ThenSearch, Yield = _ => Task.CompletedTask,
                });
                var s = result.Summary!;
                output.WriteLine($"   {run.Name} #{seed}: {s.Score:0}, {s.Issues.Count} warnings, {result.Iterations} it, {result.Elapsed.TotalSeconds:0.0}s, {result.StoppedBecause}");
                (warnings, apart, score) = (warnings + s.Issues.Count, apart + s.Groups.Sum(g => g.PeopleApart), score + s.Score);
                if (seed == 1)
                    Print($"{run.Name} #1", s, problem, problem.ToPlan(result.Assignment!));
            }
            output.WriteLine($"== {run.Name}, 5 runs: avg score {score / 5:0}, {warnings} warnings, {apart} people apart");
        }
    }

    private void Print(string title, PlanSummary s, SeatingProblem problem, int[] plan)
    {
        output.WriteLine($"{title}: score {s.Score:0}, {s.PeoplePlaced}/{s.PeopleTotal} placed, {s.SplitGroups} split, {s.Issues.Count} warnings, "
            + $"{s.Groups.Sum(g => g.PeopleApart)} people apart, choices met {string.Join(" ", s.PreferenceStats.Select(p => $"{p.Met}/{p.Wanted}"))}");
        foreach (var issue in s.Issues)
        {
            var numbers = s.Groups.First(g => g.GroupId == issue.GroupId).TableNumbers;
            output.WriteLine($"  ! {issue.Message}");
            foreach (var t in problem.Graph.Tables.Where(t => numbers.Contains(t.Table.Number)))
            {
                // The table's seats in fill order, as runs of groups.
                var order = SeatSequence.FillOrder(problem.Graph).Where(seat => problem.Graph.Seats[seat].TableIndex == t.Index).ToList();
                var runs = new List<string>();
                for (var i = 0; i < order.Count;)
                {
                    var j = i;
                    while (j < order.Count && plan[order[j]] == plan[order[i]])
                        j++;
                    runs.Add($"{(plan[order[i]] < 0 ? "empty" : problem.Groups[plan[order[i]]].Name)}×{j - i}");
                    i = j;
                }
                output.WriteLine($"      table {t.Table.Number} ({t.Seats.Count}): {string.Join(", ", runs)}");
            }
        }
    }
}

internal static class TestPlans
{
    private static string? Folder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "tests", "testplans");
            if (Directory.Exists(path))
                return path;
        }
        return null;
    }

    public static IEnumerable<string> Files() =>
        Folder() is { } folder ? Directory.GetFiles(folder, "*.tableplanner.json", SearchOption.AllDirectories) : [];

    public static EventProject Load(string fileName) =>
        ProjectSerializer.Deserialize(File.ReadAllText(Files().First(f => Path.GetFileName(f) == fileName))).Project!;
}
