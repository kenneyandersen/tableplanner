using Xunit.Abstractions;

namespace TablePlanner.Solver.Tests;

/// <summary>Prints solver results for the benchmark setups, for tuning. Run with: dotnet test --filter SolverDiagnostics --logger "console;verbosity=detailed"</summary>
public class SolverDiagnostics(ITestOutputHelper output)
{
    [Theory]
    [InlineData("example")]
    [InlineData("large")]
    [InlineData("nearly-full")]
    [InlineData("too-few")]
    public async Task Report(string name)
    {
        var project = name switch
        {
            "example" => Benchmarks.Example(),
            "large" => Benchmarks.Large(),
            "nearly-full" => Benchmarks.NearlyFull(),
            _ => Benchmarks.TooFewSeats(),
        };

        var result = await SeatingSolver.SolveAsync(project, new SolverOptions { TimeBudget = TimeSpan.FromSeconds(1), Seed = 1, Yield = _ => Task.CompletedTask });
        var s = result.Summary!;
        output.WriteLine($"{name}: {result.Status}, score {s.Score:0}, {s.PeoplePlaced}/{s.PeopleTotal} placed, {s.SplitGroups} split, " +
            $"{result.Iterations} iterations in {result.Elapsed.TotalSeconds:0.0}s ({result.Iterations / result.Elapsed.TotalSeconds:0}/s), stopped: {result.StoppedBecause}");
        foreach (var stat in s.PreferenceStats)
            output.WriteLine($"  choice {stat.Rank}: {stat.Met}/{stat.Wanted} met");
        foreach (var g in s.Groups.OrderByDescending(g => g.PeopleCount).Take(20))
            output.WriteLine($"  {g.Name,-16} {g.Placed,3}/{g.PeopleCount,-3} tables {string.Join(",", g.TableNumbers)}  prefs {string.Join(" ", g.Preferences.Select(p => p.Closeness))}");
        foreach (var issue in s.Issues)
            output.WriteLine($"  ! {issue.Message}");
    }
}
