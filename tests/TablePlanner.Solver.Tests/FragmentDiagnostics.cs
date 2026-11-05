using TablePlanner.Core.Model;
using Xunit.Abstractions;

namespace TablePlanner.Solver.Tests;

/// <summary>
/// Prints how many small parts (2–4 people of a group alone at a table) the solver leaves, for tuning.
/// Run with: dotnet test --filter FragmentDiagnostics --logger "console;verbosity=detailed"
/// </summary>
public class FragmentDiagnostics(ITestOutputHelper output)
{
    [Theory]
    [InlineData("example")]
    [InlineData("large")]
    [InlineData("nearly-full")]
    public async Task Report(string name)
    {
        var total = 0;
        var firstMet = 0;
        var firstWanted = 0;
        for (var seed = 1; seed <= 5; seed++)
        {
            var project = name switch { "example" => Benchmarks.Example(), "large" => Benchmarks.Large(), _ => Benchmarks.NearlyFull() };
            var result = await SeatingSolver.SolveAsync(project, new SolverOptions { TimeBudget = TimeSpan.FromSeconds(2), Seed = seed, Yield = _ => Task.CompletedTask });
            var parts = SmallParts(project, result.Assignment!);
            total += parts.Count;
            var first = result.Summary!.PreferenceStats.FirstOrDefault(s => s.Rank == 1);
            firstMet += first?.Met ?? 0;
            firstWanted += first?.Wanted ?? 0;
            output.WriteLine($"{name} seed {seed}: {parts.Count} small parts, {result.Summary.SplitGroups} split groups, 1st choices {first?.Met}/{first?.Wanted}"
                + (parts.Count > 0 ? ": " + string.Join("; ", parts) : ""));
        }
        output.WriteLine($"{name}: {total} small parts in 5 runs, 1st choices {firstMet}/{firstWanted}");
    }

    /// <summary>Parts of 2–4 people of a group that sits at more than one table.</summary>
    internal static List<string> SmallParts(EventProject project, SeatingAssignment assignment)
    {
        var tables = project.Setup.AllTables.ToDictionary(t => t.Id);
        var list = new List<string>();
        foreach (var group in assignment.Seats.GroupBy(s => s.GroupId))
        {
            var perTable = group.GroupBy(s => s.Seat.TableId).Select(t => (Table: tables[t.Key].Number, Count: t.Count())).ToList();
            if (perTable.Count < 2)
                continue;
            var name = project.Setup.FindGroup(group.Key)?.Name;
            list.AddRange(perTable.Where(t => t.Count is >= 1 and <= 4)
                .Select(t => $"{name} ({group.Count()}): {t.Count} at {t.Table} of {string.Join("+", perTable.Select(p => p.Count))}"));
        }
        return list;
    }
}
