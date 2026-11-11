using TablePlanner.Core.Model;
using Xunit.Abstractions;

namespace TablePlanner.Solver.Tests;

/// <summary>
/// How compactly parent groups are seated: per plan, the seat neighbours (next to, across, back to back …) that belong to
/// different top-level groups, and the tables of each top-level group. Prints only with TABLEPLANNER_DIAGNOSTICS=1.
/// </summary>
public class ParentShapeDiagnostics(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Example party levels.tableplanner.json")]
    [InlineData("Company levels 440.tableplanner.json")]
    public async Task Report(string file)
    {
        if (Environment.GetEnvironmentVariable("TABLEPLANNER_DIAGNOSTICS") != "1")
            return;
        var project = TestPlans.Load(file);
        var setup = project.Setup;
        var problem = SeatingProblem.Create(setup, project.Priorities);
        var top = problem.Groups.Select(g => setup.TopOf(g).Id).ToArray();
        for (var seed = 1; seed <= 4; seed++)
        {
            var result = await SeatingSolver.SolveAsync(project, new SolverOptions { Seed = seed, TimeBudget = TimeSpan.FromSeconds(2), Yield = _ => Task.CompletedTask });
            var plan = problem.ToPlan(result.Assignment!);
            var mixed = problem.Graph.Edges.Count(e => plan[e.A] >= 0 && plan[e.B] >= 0 && top[plan[e.A]] != top[plan[e.B]]);
            output.WriteLine($"{file} seed {seed}: score {result.Summary!.Score:0}, edges between top-level groups {mixed}, warnings {result.Summary.Issues.Count}");
            foreach (var parent in result.Summary.ParentGroups.Where(p => p.Level == 1))
                output.WriteLine($"   {parent.Name}: tables {TableNumberText.Format(parent.TableNumbers)}, apart {parent.PeopleApart}");
            if (seed == 1 && Environment.GetEnvironmentVariable("TABLEPLANNER_DETAILS") == "1")
                foreach (var table in problem.Graph.Tables)
                {
                    var counts = table.Seats.Where(s => plan[s] >= 0).GroupBy(s => problem.Groups[plan[s]].Name).Select(x => $"{x.Key} {x.Count()}");
                    output.WriteLine($"     {table.Table.Number}: {string.Join(", ", counts)} (free {table.Seats.Count(s => plan[s] < 0)})");
                }
        }
    }
}
