using Xunit.Abstractions;

namespace TablePlanner.Solver.Tests;

/// <summary>
/// Prints how many people the solver seats away from the main part of their group (at tables that are not next to
/// it), for tuning. Run with: dotnet test -c Release --filter ApartDiagnostics --logger "console;verbosity=detailed"
/// </summary>
public class ApartDiagnostics(ITestOutputHelper output)
{
    [Theory]
    [InlineData("scattered", -1, 5000)]
    [InlineData("scattered", 0, 5000)]
    [InlineData("scattered-full", -1, 5000)]
    [InlineData("scattered-full", 0, 5000)]
    [InlineData("scattered", -1)]
    [InlineData("scattered", 0)]
    [InlineData("example", -1)]
    [InlineData("large", -1)]
    public async Task Report(string name, double apartPenalty, int maxIterations = 5_000_000)
    {
        var (apart, groupsApart, firstMet, firstWanted, unplaced) = (0, 0, 0, 0, 0);
        for (var seed = 1; seed <= 5; seed++)
        {
            var project = name switch
            {
                "scattered" => Benchmarks.Scattered(seed),
                "scattered-full" => Benchmarks.Scattered(seed, 0.99),
                "example" => Benchmarks.Example(),
                _ => Benchmarks.Large(seed),
            };
            if (apartPenalty >= 0)
                project.Priorities.ApartPersonPenalty = apartPenalty;
            var result = await SeatingSolver.SolveAsync(project, new SolverOptions { TimeBudget = TimeSpan.FromSeconds(2), MaxIterations = maxIterations, Seed = seed, Yield = _ => Task.CompletedTask });
            var summary = result.Summary!;
            var away = summary.Groups.Where(g => g.PeopleApart > 0).ToList();
            apart += away.Sum(g => g.PeopleApart);
            groupsApart += away.Count;
            unplaced += summary.PeopleTotal - summary.PeoplePlaced;
            var first = summary.PreferenceStats.FirstOrDefault(s => s.Rank == 1);
            firstMet += first?.Met ?? 0;
            firstWanted += first?.Wanted ?? 0;
            output.WriteLine($"{name} seed {seed}: {away.Sum(g => g.PeopleApart)} people apart in {away.Count} groups, 1st choices {first?.Met}/{first?.Wanted}"
                + (away.Count > 0 ? ": " + string.Join("; ", away.Select(g => $"{g.Name} ({g.PeopleCount}) {g.PeopleApart} apart at {string.Join("+", g.TableNumbers)}")) : ""));
        }
        output.WriteLine($"{name} (apart penalty {(apartPenalty < 0 ? "default" : apartPenalty)}, max {maxIterations} iterations): {apart} people apart in {groupsApart} groups over 5 runs, "
            + $"1st choices {firstMet}/{firstWanted}, {unplaced} unplaced");
    }
}
