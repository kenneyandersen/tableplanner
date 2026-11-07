using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;
using Xunit.Abstractions;

namespace TablePlanner.Solver.Tests;

/// <summary>
/// Runs the solver on the projects in tests/testplans (real ones and those from <see cref="TestPlanGenerator"/>).
/// Normally a quick check only; the comparison of methods and time limits (a few minutes), for tuning, with:
/// <c>$env:TABLEPLANNER_DIAGNOSTICS=1; dotnet test tests/TablePlanner.Solver.Tests -c Release --filter TestPlanDiagnostics --logger "console;verbosity=detailed"</c>
/// (TABLEPLANNER_DETAILS=1 also lists which tables are adjacent.)
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
        if (Environment.GetEnvironmentVariable("TABLEPLANNER_DIAGNOSTICS") != "1")
        {
            // In normal test runs, only a quick check: both methods seat everyone without anyone away from their group.
            foreach (var method in new[] { SolverMethod.Search, SolverMethod.Beam })
            {
                var quick = await SeatingSolver.SolveAsync(project, new SolverOptions { Method = method, Seed = 1, MaxIterations = 20_000, Yield = _ => Task.CompletedTask });
                Assert.Equal(quick.Summary!.PeopleTotal, quick.Summary.PeoplePlaced);
                Assert.All(quick.Summary.Groups, g => Assert.Equal(0, g.PeopleApart));
            }
            return;
        }
        output.WriteLine($"{file}: {problem.PeopleCount} people, {problem.Graph.Seats.Count} seats, {problem.Groups.Count} groups");
        if (Environment.GetEnvironmentVariable("TABLEPLANNER_DETAILS") == "1")
            foreach (var t in problem.Graph.Tables)
                output.WriteLine($"  table {t.Table.Number} ({t.Seats.Count}) next to {string.Join(",", problem.Graph.Tables.Where(o => problem.Graph.AreAdjacent(t.Index, o.Index)).Select(o => o.Table.Number))}");

        // Limited by time only, as in the app (a native second is several in the browser), so not repeatable.
        var runs = new (string Name, SolverMethod Method, double Seconds)[]
        {
            ("search 0.5s", SolverMethod.Search, 0.5),
            ("beam 0.5s", SolverMethod.Beam, 0.5),
            ("search 2s", SolverMethod.Search, 2),
            ("beam 2s", SolverMethod.Beam, 2),
        };
        const int Repeats = 5;
        foreach (var run in runs)
        {
            var (warnings, apart, small, split, unplaced, firstMet, firstWanted, score) = (0, 0, 0, 0, 0, 0, 0, 0.0);
            PlanSummary? worst = null;
            int[]? worstPlan = null;
            for (var i = 0; i < Repeats; i++)
            {
                var result = await SeatingSolver.SolveAsync(project, new SolverOptions
                {
                    TimeBudget = TimeSpan.FromSeconds(run.Seconds), Method = run.Method, Yield = _ => Task.CompletedTask,
                });
                var s = result.Summary!;
                var first = s.PreferenceStats.FirstOrDefault(p => p.Rank == 1);
                warnings += s.Issues.Count;
                apart += s.Groups.Sum(g => g.PeopleApart);
                small += s.Groups.Count(g => g.SmallParts > 0);
                split += s.SplitGroups;
                unplaced += s.PeopleTotal - s.PeoplePlaced;
                (firstMet, firstWanted) = (firstMet + (first?.Met ?? 0), firstWanted + (first?.Wanted ?? 0));
                score += s.Score;
                if (worst is null || s.Issues.Count > worst.Issues.Count)
                    (worst, worstPlan) = (s, problem.ToPlan(result.Assignment!));
            }
            output.WriteLine($"== {file} | {run.Name,-11} | score {score / Repeats,7:0} | warnings {warnings,2} | people apart {apart,3} | "
                + $"small parts {small,2} | split groups {split / (double)Repeats,4:0.0} | 1st choices {firstMet}/{firstWanted} | unplaced {unplaced}");
            if (worst!.Issues.Count > 0)
                Print($"   worst of {Repeats}", worst, problem, worstPlan!);
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
    internal static string? Folder()
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
