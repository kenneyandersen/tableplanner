using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;

namespace TablePlanner.Solver.Tests;

public class SeatingSolverTests
{
    /// <summary>Repeatable and quick: a fixed seed, an iteration limit well inside a generous time budget, no real yielding.</summary>
    private static SolverOptions Options(int iterations = 30_000, SolveMode mode = SolveMode.Fresh) => new()
    {
        Seed = 7,
        MaxIterations = iterations,
        TimeBudget = TimeSpan.FromMinutes(1),
        Mode = mode,
        Yield = _ => Task.CompletedTask,
    };

    private static Task<SolveResult> Solve(EventProject project, SolverOptions? options = null) =>
        SeatingSolver.SolveAsync(project, options ?? Options());

    private static GroupPlacement Placement(SolveResult result, string name) => result.Summary!.Groups.Single(g => g.Name == name);

    /// <summary>Checks that an assignment only uses real, enabled seats, each once, and seats whole groups.</summary>
    private static void AssertValid(EventProject project, SolveResult result)
    {
        var seats = result.Assignment!.Seats;
        Assert.Equal(seats.Count, seats.Select(s => s.Seat).Distinct().Count());
        foreach (var seat in seats)
        {
            var table = project.Setup.FindTable(seat.Seat.TableId);
            Assert.NotNull(table);
            Assert.True(table.IsValidSeatIndex(seat.Seat.Index));
            Assert.DoesNotContain(seat.Seat.Index, table.DisabledSeats);
        }
        foreach (var group in seats.GroupBy(s => s.GroupId))
            Assert.Equal(project.Setup.FindGroup(group.Key)!.PeopleCount, group.Count());
    }

    [Fact]
    public async Task Seats_everyone_in_the_example_with_all_first_choices_met()
    {
        var project = Benchmarks.Example();

        var result = await Solve(project);

        Assert.Equal(SolveStatus.Success, result.Status);
        AssertValid(project, result);
        var summary = result.Summary!;
        Assert.Equal(231, summary.PeoplePlaced);
        var first = summary.PreferenceStats.Single(s => s.Rank == 1);
        Assert.Equal(first.Wanted, first.Met);
        Assert.All(summary.Groups, g => Assert.Equal(1, g.FloorCount));
        Assert.DoesNotContain(summary.Groups, g => g.HasDistantParts);
    }

    [Fact]
    public async Task Seats_groups_at_round_tables_in_compact_arcs()
    {
        // 6 round tables of 10 in two rows, 2.5 m apart edge to edge (not adjacent); groups of 2–10.
        var floor = new Floor { Name = "Hall", Number = 1 };
        for (var i = 0; i < 6; i++)
            floor.Tables.Add(new Table { Number = 101 + i, Shape = TableShape.Round, RoundSeats = 10, X = 3 + (i % 3) * 4.5, Y = 3 + (i / 3) * 4.5 });
        var sizes = new[] { 10, 8, 7, 6, 6, 5, 5, 4, 3, 2 };
        var project = new EventProject
        {
            Setup = new EventSetup { Floors = [floor], Groups = sizes.Select((s, i) => new Group { Name = $"G{i}", PeopleCount = s }).ToList() },
        };

        var result = await Solve(project);

        Assert.Equal(SolveStatus.Success, result.Status);
        AssertValid(project, result);
        // Every group fits at one table, and sits in one unbroken arc there.
        Assert.All(result.Summary!.Groups, g => Assert.Single(g.TableNumbers));
        foreach (var group in result.Assignment!.Seats.GroupBy(s => s.GroupId))
        {
            var taken = group.Select(s => s.Seat.Index).ToHashSet();
            var starts = taken.Count(i => !taken.Contains((i + 9) % 10));
            Assert.True(taken.Count == 10 || starts == 1, $"group of {taken.Count} sits in {starts} arcs");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Keeps_split_groups_in_one_area_of_a_scattered_venue_even_with_a_short_search(int seed)
    {
        // A nearly full venue with separate areas; few iterations, as in a slow browser.
        var project = Benchmarks.Scattered(seed, 0.99);

        var result = await SeatingSolver.SolveAsync(project, new SolverOptions { MaxIterations = 5000, Seed = seed, Yield = _ => Task.CompletedTask });

        AssertValid(project, result);
        Assert.All(result.Summary!.Groups, g => Assert.Equal(0, g.PeopleApart));
    }

    [Theory]
    [InlineData("example", 1)]
    [InlineData("example", 2)]
    [InlineData("large", 1)]
    [InlineData("large", 2)]
    public async Task A_split_group_leaves_no_small_part_alone_at_a_table(string name, int seed)
    {
        // Before, Marketing (24) became 20 + 4 at 20-seat tables, and a group of 41 even 20 + 20 + 1.
        var project = name == "example" ? Benchmarks.Example() : Benchmarks.Large();

        var result = await SeatingSolver.SolveAsync(project, new SolverOptions { TimeBudget = TimeSpan.FromSeconds(2), Seed = seed, Yield = _ => Task.CompletedTask });

        Assert.Empty(FragmentDiagnostics.SmallParts(project, result.Assignment!));
        Assert.All(result.Summary!.Groups, g => Assert.Equal(0, g.SmallParts));
    }

    [Theory]
    [InlineData("example")]
    [InlineData("large")]
    [InlineData("nearly-full")]
    [InlineData("scattered-full")]
    public async Task Beam_search_seats_everyone_and_keeps_groups_together(string name)
    {
        var project = name switch
        {
            "example" => Benchmarks.Example(),
            "large" => Benchmarks.Large(),
            "nearly-full" => Benchmarks.NearlyFull(),
            _ => Benchmarks.Scattered(1, 0.99),
        };

        var result = await Solve(project, Options() with { Method = SolverMethod.Beam });

        Assert.Equal(SolveStatus.Success, result.Status);
        AssertValid(project, result);
        Assert.All(result.Summary!.Groups, g => Assert.Equal(0, g.PeopleApart));
        Assert.All(result.Summary.Groups, g => Assert.Equal(0, g.SmallParts));
    }

    [Fact]
    public async Task Beam_search_with_too_few_seats_places_as_many_people_as_possible()
    {
        var project = Benchmarks.TooFewSeats();
        var seats = project.Setup.AllTables.Sum(t => t.AvailableSeatCount);

        var result = await Solve(project, Options() with { Method = SolverMethod.Beam });

        Assert.Equal(SolveStatus.Partial, result.Status);
        AssertValid(project, result);
        Assert.InRange(result.Summary!.PeoplePlaced, seats - 3, seats);
    }

    [Fact]
    public async Task Beam_search_with_a_seed_gives_the_same_plan()
    {
        var a = await Solve(Benchmarks.Example(), Options(5_000) with { Method = SolverMethod.Beam });
        var b = await Solve(Benchmarks.Example(), Options(5_000) with { Method = SolverMethod.Beam });

        Assert.Equal(a.Summary!.Score, b.Summary!.Score);
        Assert.Equal(a.Summary.Groups.Select(g => g.TableNumbers), b.Summary!.Groups.Select(g => g.TableNumbers));
    }

    [Fact]
    public async Task Several_attempts_keep_the_best_and_still_repeat_with_a_seed()
    {
        var one = await Solve(Benchmarks.Scattered(2, 0.99), Options(5_000));
        var three = await Solve(Benchmarks.Scattered(2, 0.99), Options(5_000) with { Attempts = 3 });
        var again = await Solve(Benchmarks.Scattered(2, 0.99), Options(5_000) with { Attempts = 3 });

        Assert.Equal(1, one.Attempts);
        Assert.Equal(3, three.Attempts);
        Assert.Equal(three.Summary!.Score, again.Summary!.Score);
        // The first of the three attempts is the single attempt, so the best of three is at least as good (or has fewer warnings).
        Assert.True(three.Summary.Score >= one.Summary!.Score * (1 - SeatingSolver.CloseScoreShare) - 1e-6);
        Assert.True(three.Summary.Issues.Count <= one.Summary.Issues.Count || three.Summary.Score > one.Summary.Score);
    }

    [Fact]
    public void Choosing_between_attempts_prefers_fewer_warnings_when_the_scores_are_close()
    {
        SeatingSolver.Attempt Attempt(double score, int warnings) => new([], score, warnings, StopReason.TimeBudget, false);

        // Within 1 % of the best score, fewer warnings win.
        Assert.Equal(9_950, SeatingSolver.Choose([Attempt(10_000, 2), Attempt(9_950, 0), Attempt(9_990, 1)]).Score);
        // Further behind, the better score wins despite more warnings.
        Assert.Equal(10_000, SeatingSolver.Choose([Attempt(10_000, 2), Attempt(9_500, 0)]).Score);
    }

    [Fact]
    public async Task Groups_that_fit_at_one_table_are_not_split()
    {
        var result = await Solve(Benchmarks.Example());

        Assert.All(result.Summary!.Groups.Where(g => g.PeopleCount <= 10), g => Assert.Single(g.TableNumbers));
    }

    [Fact]
    public async Task Large_groups_use_adjacent_tables()
    {
        var result = await Solve(Benchmarks.Example());

        var sales = Placement(result, "Sales");
        Assert.InRange(sales.TableNumbers.Count, 3, 4);
        Assert.False(sales.HasDistantParts);
    }

    [Fact]
    public async Task Empty_seats_are_at_the_table_ends_not_between_people()
    {
        var project = Benchmarks.Example();
        var result = await Solve(project);

        var taken = result.Assignment!.Seats.Select(s => s.Seat).ToHashSet();
        foreach (var table in project.Setup.AllTables)
            for (var side = 0; side < 2; side++)
            {
                var occupied = Enumerable.Range(0, table.SeatsPerSide)
                    .Select(i => taken.Contains(new SeatId(table.Id, side * table.SeatsPerSide + i)))
                    .ToList();
                var first = occupied.IndexOf(true);
                var last = occupied.LastIndexOf(true);
                if (first >= 0)
                    Assert.All(occupied[first..(last + 1)], Assert.True);
            }
    }

    [Fact]
    public async Task With_too_few_seats_places_as_many_people_as_possible()
    {
        var project = Benchmarks.TooFewSeats();
        var seats = project.Setup.AllTables.Sum(t => t.AvailableSeatCount);

        var result = await Solve(project);

        Assert.Equal(SolveStatus.Partial, result.Status);
        AssertValid(project, result);
        Assert.InRange(result.Summary!.PeoplePlaced, seats - 3, seats);
        Assert.Contains(result.Issues, i => i.Message.Contains("has no seats"));
    }

    [Fact]
    public async Task Nearly_full_setup_seats_everyone()
    {
        var project = Benchmarks.NearlyFull();

        var result = await Solve(project);

        Assert.Equal(SolveStatus.Success, result.Status);
        Assert.Equal(result.Summary!.PeopleTotal, result.Summary.PeoplePlaced);
    }

    [Theory]
    [InlineData(SolverMethod.Search)]
    [InlineData(SolverMethod.Beam)]
    public async Task Keeps_seat_table_and_floor_pins(SolverMethod method)
    {
        var project = Benchmarks.Example();
        var setup = project.Setup;
        var upstairs = setup.Floors[1];
        var table = setup.Floors[0].Tables[8];
        var seatTable = setup.Floors[0].Tables[4];
        var book = setup.Groups.Single(g => g.Name == "Book club");
        var seatPin = Enumerable.Range(0, 5).Select(i => new SeatId(seatTable.Id, i)).ToList();
        setup.Pins.Add(new Pin { GroupId = book.Id, Scope = PinScope.Seats, Seats = seatPin });
        setup.Pins.Add(new Pin { GroupId = setup.Groups.Single(g => g.Name == "Board").Id, Scope = PinScope.Table, TableId = table.Id });
        setup.Pins.Add(new Pin { GroupId = setup.Groups.Single(g => g.Name == "Finance").Id, Scope = PinScope.Floor, FloorId = upstairs.Id });

        var result = await Solve(project, Options() with { Method = method });

        Assert.Equal(SolveStatus.Success, result.Status);
        Assert.Equal(seatPin.ToHashSet(), result.Assignment!.SeatsOf(book.Id).ToHashSet());
        Assert.Equal([table.Number], Placement(result, "Board").TableNumbers);
        Assert.All(Placement(result, "Finance").TableNumbers, n => Assert.Contains(upstairs.Tables, t => t.Number == n));
    }

    [Fact]
    public async Task Floor_split_penalty_keeps_groups_on_one_floor()
    {
        var result = await Solve(Benchmarks.Large(), Options(60_000));

        Assert.All(result.Summary!.Groups, g => Assert.Equal(1, g.FloorCount));
    }

    [Fact]
    public async Task Never_split_across_floors_fails_when_a_group_is_larger_than_any_floor()
    {
        var project = Benchmarks.Example();
        project.Priorities.NeverSplitAcrossFloors = true;
        project.Setup.Groups[0].PeopleCount = 200;

        var result = await Solve(project);

        Assert.Equal(SolveStatus.Failed, result.Status);
        Assert.Null(result.Assignment);
        Assert.Contains(result.Issues, i => i.Message.Contains("larger than any floor"));
    }

    [Fact]
    public async Task Setup_errors_stop_the_solver()
    {
        var project = Benchmarks.Example();
        project.Setup.Groups[1].Name = project.Setup.Groups[0].Name;

        var result = await Solve(project);

        Assert.Equal(SolveStatus.Failed, result.Status);
        Assert.Equal(StopReason.NotRun, result.StoppedBecause);
        Assert.All(result.Issues, i => Assert.Equal(IssueSeverity.Error, i.Severity));
    }

    [Fact]
    public async Task No_seats_or_no_groups_fail_with_a_message()
    {
        var noGroups = Benchmarks.Example();
        noGroups.Setup.Groups.Clear();
        var noTables = Benchmarks.Example();
        foreach (var floor in noTables.Setup.Floors)
            floor.Tables.Clear();

        Assert.Contains("no groups", Assert.Single((await Solve(noGroups)).Issues).Message);
        Assert.Contains("no seats", (await Solve(noTables)).Issues.First(i => i.Message.Contains("seats")).Message);
    }

    [Fact]
    public async Task Stops_within_the_time_budget_and_reports_progress()
    {
        var reports = new List<SolveProgress>();
        var options = new SolverOptions
        {
            TimeBudget = TimeSpan.FromMilliseconds(500),
            StallIterations = int.MaxValue,
            YieldInterval = TimeSpan.FromMilliseconds(20),
            Yield = _ => Task.CompletedTask,
        };

        var result = await SeatingSolver.SolveAsync(Benchmarks.Large(), options, new SyncProgress(reports.Add));

        Assert.Equal(StopReason.TimeBudget, result.StoppedBecause);
        // The order search stops at 85% of the budget; polishing may finish before the rest is used.
        Assert.InRange(result.Elapsed, TimeSpan.FromMilliseconds(400), TimeSpan.FromSeconds(3));
        Assert.True(reports.Count > 2);
        Assert.Equal(result.Summary!.Score, reports[^1].BestScore);
    }

    [Fact]
    public async Task Cancelling_returns_the_best_plan_so_far()
    {
        using var cancel = new CancellationTokenSource();
        var options = Options(int.MaxValue) with { StallIterations = int.MaxValue, YieldInterval = TimeSpan.Zero };
        var task = SeatingSolver.SolveAsync(Benchmarks.Large(), options with
        {
            Yield = _ =>
            {
                cancel.Cancel();
                return Task.CompletedTask;
            },
        }, cancellationToken: cancel.Token);

        var result = await task;

        Assert.Equal(StopReason.Cancelled, result.StoppedBecause);
        Assert.NotNull(result.Assignment);
        Assert.Equal(result.Summary!.PeopleTotal, result.Summary.PeoplePlaced);
    }

    [Fact]
    public async Task Re_optimizing_after_a_small_change_moves_few_people()
    {
        var project = Benchmarks.Example();
        var first = await Solve(project);
        project.CurrentAssignment = first.Assignment!;
        var before = TablesByPerson(project, first.Assignment!);

        project.Setup.Groups.Single(g => g.Name == "Book club").PeopleCount = 7;
        var second = await Solve(project, Options(mode: SolveMode.KeepCloseToCurrent));

        Assert.Equal(SolveStatus.Success, second.Status);
        var after = TablesByPerson(project, second.Assignment!);
        var moved = before.Sum(kv => Math.Max(0, kv.Value - after.GetValueOrDefault(kv.Key)));
        Assert.True(moved <= 20, $"{moved} people moved to another table");
    }

    [Fact]
    public async Task Same_seed_and_iteration_limit_give_the_same_plan()
    {
        var a = await Solve(Benchmarks.Example(), Options(5_000));
        var b = await Solve(Benchmarks.Example(), Options(5_000));

        Assert.Equal(a.Summary!.Score, b.Summary!.Score);
    }

    /// <summary>People per (group, table).</summary>
    private static Dictionary<(Guid, Guid), int> TablesByPerson(EventProject project, SeatingAssignment assignment) =>
        assignment.Seats.GroupBy(s => (s.GroupId, s.Seat.TableId)).ToDictionary(g => g.Key, g => g.Count());

    /// <summary>Reports synchronously; <see cref="Progress{T}"/> would post to the thread pool.</summary>
    private sealed class SyncProgress(Action<SolveProgress> report) : IProgress<SolveProgress>
    {
        public void Report(SolveProgress value) => report(value);
    }
}
