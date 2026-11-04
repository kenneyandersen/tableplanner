using TablePlanner.Core.Model;

namespace TablePlanner.Solver.Tests;

public class PlanEvaluatorTests
{
    /// <summary>Tables 101 and 102 back to back (adjacent), 103 far away, 201 upstairs; 6 seats per side each.</summary>
    private static (EventSetup Setup, Table[] Tables) Room(params Group[] groups)
    {
        var tables = new[]
        {
            new Table { Number = 101, SeatsPerSide = 6, X = 0, Y = 0 },
            new Table { Number = 102, SeatsPerSide = 6, X = 0, Y = 2.5 },
            new Table { Number = 103, SeatsPerSide = 6, X = 20, Y = 0 },
            new Table { Number = 201, SeatsPerSide = 6 },
        };
        var setup = new EventSetup
        {
            Floors =
            [
                new Floor { Name = "Ground", Number = 1, Tables = [tables[0], tables[1], tables[2]] },
                new Floor { Name = "First", Number = 2, Tables = [tables[3]] },
            ],
            Groups = [.. groups],
        };
        return (setup, tables);
    }

    /// <summary>Scores the plan where each group gets the given seats (table, seat index).</summary>
    private static double Score(EventSetup setup, params (Group Group, Table Table, int[] Seats)[] placements) =>
        Summary(setup, placements).Score;

    private static PlanSummary Summary(EventSetup setup, params (Group Group, Table Table, int[] Seats)[] placements)
    {
        var problem = SeatingProblem.Create(setup, new SeatingPriorities());
        var assignment = new SeatingAssignment();
        foreach (var (group, table, seats) in placements)
            assignment.Seats.AddRange(seats.Select(i => new SeatAssignment(new SeatId(table.Id, i), group.Id)));
        return new PlanEvaluator(problem).Summarize(problem.ToPlan(assignment));
    }

    [Fact]
    public void A_compact_block_beats_a_spread_out_row()
    {
        var group = new Group { Name = "A", PeopleCount = 4 };
        var (setup, t) = Room(group);

        // Seats 0, 1 and across them 6, 7 versus four in a row along one side.
        var block = Score(setup, (group, t[0], [0, 1, 6, 7]));
        var row = Score(setup, (group, t[0], [0, 1, 2, 3]));

        Assert.True(block > row);
    }

    [Fact]
    public void Splitting_over_adjacent_tables_beats_distant_tables_beats_floors()
    {
        var group = new Group { Name = "A", PeopleCount = 8 };
        var (setup, t) = Room(group);

        var together = Score(setup, (group, t[0], [0, 1, 2, 3, 6, 7, 8, 9]));
        var adjacent = Score(setup, (group, t[0], [6, 7, 8, 9]), (group, t[1], [0, 1, 2, 3]));
        var distant = Score(setup, (group, t[0], [0, 1, 6, 7]), (group, t[2], [0, 1, 6, 7]));
        var floors = Score(setup, (group, t[0], [0, 1, 6, 7]), (group, t[3], [0, 1, 6, 7]));

        Assert.True(together > adjacent);
        Assert.True(adjacent > distant);
        Assert.True(distant > floors);
    }

    [Fact]
    public void Back_to_back_seats_count_as_close()
    {
        var group = new Group { Name = "A", PeopleCount = 2 };
        var (setup, t) = Room(group);

        // Bottom seat 6 at 101 is back to back with top seat 0 at 102; top seat 0 at 101 is not.
        var backToBack = Score(setup, (group, t[0], [6]), (group, t[1], [0]));
        var notClose = Score(setup, (group, t[0], [0]), (group, t[1], [0]));

        Assert.True(backToBack > notClose);
    }

    [Fact]
    public void Preferences_score_by_how_close_the_groups_meet()
    {
        var b = new Group { Name = "B", PeopleCount = 2 };
        var a = new Group { Name = "A", PeopleCount = 2, PreferredGroupIds = [b.Id] };
        var (setup, t) = Room(a, b);

        var levels = new[] { t[0], t[1], t[2], t[3] }
            .Select(other => Summary(setup, (a, t[0], [0, 6]), (b, other, other == t[0] ? [2, 8] : [0, 6])))
            .ToList();

        Assert.Equal(
            [PreferenceCloseness.SameTable, PreferenceCloseness.AdjacentTable, PreferenceCloseness.SameFloor, PreferenceCloseness.Apart],
            levels.Select(s => s.Groups[0].Preferences.Single().Closeness));
        Assert.True(levels[0].Score > levels[1].Score && levels[1].Score > levels[2].Score && levels[2].Score > levels[3].Score);
        Assert.Equal(1, levels[1].PreferenceStats.Single().Met);
        Assert.Equal(0, levels[2].PreferenceStats.Single().Met);
    }

    [Fact]
    public void First_choices_weigh_more_than_second_choices()
    {
        var first = new Group { Name = "First", PeopleCount = 2 };
        var second = new Group { Name = "Second", PeopleCount = 2 };
        var a = new Group { Name = "A", PeopleCount = 2, PreferredGroupIds = [first.Id, second.Id] };
        var (setup, t) = Room(a, first, second);

        var withFirst = Score(setup, (a, t[0], [0, 6]), (first, t[0], [1, 7]), (second, t[2], [0, 6]));
        var withSecond = Score(setup, (a, t[0], [0, 6]), (second, t[0], [1, 7]), (first, t[2], [0, 6]));

        Assert.True(withFirst > withSecond);
    }

    [Fact]
    public void Mutual_preferences_earn_a_bonus()
    {
        var b = new Group { Name = "B", PeopleCount = 2 };
        var a = new Group { Name = "A", PeopleCount = 2, PreferredGroupIds = [b.Id] };
        var (setup, t) = Room(a, b);
        var oneWay = Score(setup, (a, t[0], [0, 6]), (b, t[0], [1, 7]));

        b.PreferredGroupIds = [a.Id];
        var mutual = Score(setup, (a, t[0], [0, 6]), (b, t[0], [1, 7]));

        var defaults = new SeatingPriorities();
        Assert.Equal(oneWay + defaults.PreferenceRankWeights[0] + defaults.MutualPreferenceBonus, mutual);
    }

    [Fact]
    public void Unplaced_people_cost_more_than_anything_else()
    {
        var a = new Group { Name = "A", PeopleCount = 4 };
        var (setup, t) = Room(a);

        var placedBadly = Summary(setup, (a, t[0], [0]), (a, t[2], [0]), (a, t[3], [0, 5]));
        var unplaced = Summary(setup);

        Assert.True(placedBadly.Score > unplaced.Score);
        Assert.Contains(unplaced.Issues, i => i.Message.Contains("has no seats"));
        Assert.Contains(placedBadly.Issues, i => i.Message.Contains("2 floors"));
    }

    [Fact]
    public void An_empty_seat_between_people_is_a_gap()
    {
        var a = new Group { Name = "A", PeopleCount = 3 };
        var (setup, t) = Room(a);

        var gap = Score(setup, (a, t[0], [0, 2, 6]));
        var noGap = Score(setup, (a, t[0], [0, 1, 6]));

        Assert.True(noGap > gap);
    }

    [Fact]
    public void A_broken_pin_is_an_error()
    {
        var a = new Group { Name = "A", PeopleCount = 2 };
        var (setup, t) = Room(a);
        setup.Pins.Add(new Pin { GroupId = a.Id, Scope = PinScope.Table, TableId = t[1].Id });

        var summary = Summary(setup, (a, t[0], [0, 6]));

        Assert.True(summary.Groups[0].BreaksHardRule);
        Assert.True(summary.Score < -PlanEvaluator.HardRulePenalty);
    }
}
