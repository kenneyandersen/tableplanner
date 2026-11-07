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

        foreach (var iterations in new[] { 20_000, 100_000, 500_000 })
        {
            var (warnings, apart, score) = (0, 0, 0.0);
            for (var seed = 1; seed <= 5; seed++)
            {
                var result = await SeatingSolver.SolveAsync(project, new SolverOptions
                {
                    TimeBudget = TimeSpan.FromSeconds(60), MaxIterations = iterations, Seed = seed, Yield = _ => Task.CompletedTask,
                });
                var s = result.Summary!;
                output.WriteLine($"   seed {seed}: {s.Score:0}, {s.Issues.Count} warnings, {result.Iterations} it, {result.StoppedBecause}");
                (warnings, apart, score) = (warnings + s.Issues.Count, apart + s.Groups.Sum(g => g.PeopleApart), score + s.Score);
                if (seed == 1)
                    Print($"{iterations} iterations, seed 1 ({result.Elapsed.TotalSeconds:0.0}s)", s, problem, problem.ToPlan(result.Assignment!));
            }
            output.WriteLine($"== {iterations} iterations, 5 seeds: avg score {score / 5:0}, {warnings} warnings, {apart} people apart");
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
