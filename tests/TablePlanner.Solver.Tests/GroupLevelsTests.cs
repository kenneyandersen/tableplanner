using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;
using Xunit.Abstractions;

namespace TablePlanner.Solver.Tests;

public class GroupLevelsTests(ITestOutputHelper output)
{
    private static SolverOptions Options(int seed = 1, int iterations = 40_000) => new()
    {
        Seed = seed,
        MaxIterations = iterations,
        TimeBudget = TimeSpan.FromMinutes(1),
        Yield = _ => Task.CompletedTask,
    };

    private static int DepartmentPeopleApart(PlanSummary summary) => summary.ParentGroups.Where(p => p.Level == 1).Sum(p => p.PeopleApart);

    [Fact]
    public void The_departments_benchmark_is_valid()
    {
        var project = Benchmarks.Departments();

        Assert.Empty(SetupValidator.Validate(project.Setup).Errors);
        Assert.True(project.Setup.HasLevels());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Departments_and_teams_are_kept_together(int seed)
    {
        var project = Benchmarks.Departments(seed);

        var result = await SeatingSolver.SolveAsync(project, Options(seed));

        var summary = result.Summary!;
        Assert.Equal(SolveStatus.Success, result.Status);
        Assert.Equal(summary.PeopleTotal, summary.PeoplePlaced);
        Assert.All(summary.ParentGroups, p => Assert.Equal(1, p.FloorCount));
        Assert.All(summary.ParentGroups, p => Assert.Equal(0, p.PeopleApart));
        Assert.DoesNotContain(summary.Groups, g => g.PeopleApart > 0);
        output.WriteLine($"seed {seed}: {summary.ParentGroups.Count} parent groups, score {summary.Score:0}");
    }

    [Fact]
    public async Task Without_the_parent_penalty_departments_spread_more()
    {
        var (withLevels, without) = (0, 0);
        for (var seed = 1; seed <= 3; seed++)
        {
            var project = Benchmarks.Departments(seed);
            withLevels += DepartmentPeopleApart((await SeatingSolver.SolveAsync(project, Options(seed))).Summary!);
            project.Priorities.ParentApartPenalty = 0;
            without += DepartmentPeopleApart((await SeatingSolver.SolveAsync(project, Options(seed))).Summary!);
        }
        output.WriteLine($"department people apart over 3 seeds: {withLevels} with the penalty, {without} without");
        Assert.True(withLevels < without, $"{withLevels} apart with the penalty, {without} without");
    }

    [Fact]
    public async Task A_department_larger_than_a_floor_splits_between_whole_teams()
    {
        // 8 teams of 20 (160 people) in one department; a floor has 152 seats.
        var project = Benchmarks.Departments();
        project.Setup.Groups.Clear();
        var department = new Group { Name = "Big", PeopleCount = 0 };
        project.Setup.Groups.Add(department);
        for (var t = 1; t <= 8; t++)
            project.Setup.Groups.Add(new Group { Name = $"Team {t}", ParentId = department.Id, PeopleCount = 20 });

        var result = await SeatingSolver.SolveAsync(project, Options());

        var summary = result.Summary!;
        Assert.Equal(160, summary.PeoplePlaced);
        Assert.All(summary.Groups, g => Assert.Equal(1, g.FloorCount));
        var big = Assert.Single(summary.ParentGroups);
        Assert.Equal(2, big.FloorCount);
        // Counted in people: 160 placed, and at least the 8 who do not fit on one floor are away from the rest.
        Assert.Equal(160, big.Placed);
        Assert.InRange(big.PeopleApart, 8, 80);
        Assert.Contains(summary.Issues, i => i.GroupId == department.Id && i.Message.Contains("floors"));
    }

    [Fact]
    public async Task A_preference_for_a_department_is_met_by_sitting_with_any_of_its_teams()
    {
        var project = Benchmarks.Departments();
        var department = project.Setup.Groups.First(g => g.Name == "Dept 3");
        var guests = project.Setup.Groups.First(g => g.Name == "Guests 1");
        guests.PreferredGroupIds = [department.Id];

        var result = await SeatingSolver.SolveAsync(project, Options());

        var outcome = Assert.Single(result.Summary!.Groups.Single(g => g.GroupId == guests.Id).Preferences);
        Assert.Equal(department.Id, outcome.PreferredGroupId);
        Assert.True(outcome.IsMet);
        Assert.Contains(result.Summary.PreferenceStats, s => s.Rank == 1 && s.Wanted == 1 && s.Met == 1);
    }

    [Fact]
    public async Task A_department_can_prefer_another_group()
    {
        var project = Benchmarks.Departments();
        var department = project.Setup.Groups.First(g => g.Name == "Dept 2");
        var guests = project.Setup.Groups.First(g => g.Name == "Guests 2");
        department.PreferredGroupIds = [guests.Id];

        var result = await SeatingSolver.SolveAsync(project, Options());

        var outcome = Assert.Single(result.Summary!.ParentGroups.Single(p => p.GroupId == department.Id).Preferences);
        Assert.True(outcome.IsMet);
    }

    [Fact]
    public async Task A_floor_pin_on_a_department_holds_its_teams()
    {
        var project = Benchmarks.Departments();
        var department = project.Setup.Groups.First(g => g.Name == "Dept 1");
        var upstairs = project.Setup.Floors[1];
        PlanOperations.PinToFloor(project, department.Id, upstairs.Id);

        var result = await SeatingSolver.SolveAsync(project, Options());

        var upstairsTables = upstairs.Tables.Select(t => t.Number).ToHashSet();
        foreach (var team in project.Setup.BottomGroupsOf(department))
            Assert.All(result.Summary!.Groups.Single(g => g.GroupId == team.Id).TableNumbers, n => Assert.Contains(n, upstairsTables));
    }

    [Fact]
    public async Task Beam_search_also_keeps_departments_together()
    {
        var project = Benchmarks.Departments();

        var result = await SeatingSolver.SolveAsync(project, Options() with { Method = SolverMethod.Beam });

        Assert.Equal(result.Summary!.PeopleTotal, result.Summary.PeoplePlaced);
        Assert.All(result.Summary.ParentGroups, p => Assert.Equal(1, p.FloorCount));
    }
}
