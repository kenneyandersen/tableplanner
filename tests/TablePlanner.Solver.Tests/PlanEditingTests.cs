using TablePlanner.Core.Model;

namespace TablePlanner.Solver.Tests;

public class PlanEditingTests
{
    /// <summary>One table, 5 per side; A (3) and B (3) seated by the solver's fill order (columns: top, then bottom).</summary>
    private static (EventProject Project, SeatingProblem Problem, Table Table, Group A, Group B) Setup()
    {
        var table = new Table { Number = 101, SeatsPerSide = 5 };
        var a = new Group { Name = "A", PeopleCount = 3 };
        var b = new Group { Name = "B", PeopleCount = 3 };
        var project = new EventProject();
        project.Setup.Floors.Add(new Floor { Name = "Ground", Number = 1, Tables = [table] });
        project.Setup.Groups.AddRange([a, b]);
        // A: top 0, bottom 5, top 1. B: bottom 6, top 2, bottom 7.
        foreach (var (i, g) in new[] { (0, a), (5, a), (1, a), (6, b), (2, b), (7, b) })
            project.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(table.Id, i), g.Id));
        return (project, SeatingProblem.Create(project.Setup, project.Priorities), table, a, b);
    }

    [Fact]
    public void Move_takes_the_free_seats_nearest_the_dropped_seat()
    {
        var (project, problem, table, a, _) = Setup();

        var seats = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 3));

        // Top 3 (dropped), top 4 next to it, bottom 8 across from it.
        Assert.Equal([3, 4, 8], seats!.Select(s => s.Index).Order());
    }

    [Fact]
    public void Move_skips_seats_of_other_groups_and_may_reuse_its_own()
    {
        var (project, problem, table, a, _) = Setup();

        // Dropped on its own seat (bottom 5): it stays where it is (5, top 0 across, top 1 diagonal); B's seats are skipped.
        var seats = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 5));

        Assert.Equal([0, 1, 5], seats!.Select(s => s.Index).Order());
    }

    [Fact]
    public void Move_fails_when_there_are_not_enough_free_seats()
    {
        var (project, problem, table, a, _) = Setup();
        a.PeopleCount = 8;
        problem = SeatingProblem.Create(project.Setup, project.Priorities);

        Assert.Null(PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 3)));
    }

    [Fact]
    public void Move_stays_on_the_clicked_floor()
    {
        var (project, _, table, a, _) = Setup();
        var upstairs = new Table { Number = 201, SeatsPerSide = 5 };
        project.Setup.Floors.Add(new Floor { Name = "First", Number = 2, Tables = [upstairs] });
        var problem = SeatingProblem.Create(project.Setup, project.Priorities);

        // Dropped on bottom 9 (a corner): 9, top 4 across, bottom 8 next to it; never the free table upstairs.
        var nearEnd = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 9));
        a.PeopleCount = 8;
        problem = SeatingProblem.Create(project.Setup, project.Priorities);
        var tooBig = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 9));

        Assert.Equal([4, 8, 9], nearEnd!.Select(s => s.Index).Order());
        Assert.Null(tooBig);
    }

    [Fact]
    public void Swapping_groups_of_the_same_size_trades_their_seats()
    {
        var (project, problem, _, a, b) = Setup();
        var seatsOfA = project.CurrentAssignment.SeatsOf(a.Id).ToHashSet();
        var seatsOfB = project.CurrentAssignment.SeatsOf(b.Id).ToHashSet();

        var swap = PlanEditing.SeatsForSwap(problem, project.CurrentAssignment, a.Id, b.Id);

        Assert.Equal(seatsOfB, swap[a.Id].ToHashSet());
        Assert.Equal(seatsOfA, swap[b.Id].ToHashSet());
    }

    [Fact]
    public void Swapping_a_smaller_group_into_a_larger_ones_place_seats_everyone()
    {
        var (project, problem, table, a, b) = Setup();
        project.CurrentAssignment.Seats.RemoveAll(s => s.GroupId == a.Id && s.Seat.Index == 1);
        a.PeopleCount = 2;
        problem = SeatingProblem.Create(project.Setup, project.Priorities);

        var swap = PlanEditing.SeatsForSwap(problem, project.CurrentAssignment, a.Id, b.Id);

        Assert.Equal([6, 2], swap[a.Id].Select(s => s.Index));
        Assert.Equal([7, 0, 5], swap[b.Id].Select(s => s.Index));
    }

    [Fact]
    public void A_small_group_dropped_at_the_end_of_a_table_stays_at_that_table()
    {
        // The user's case: 4 people dropped on the last seat of a table, with another table just past its end: they all
        // sit at the dropped table (before, the fill order put 2 of them at the next table).
        var first = new Table { Number = 101, SeatsPerSide = 8, X = 3, Y = 3 };
        var second = new Table { Number = 102, SeatsPerSide = 8, X = 3, Y = 5.5 };
        var family = new Group { Name = "Andersen", PeopleCount = 4 };
        var project = new EventProject();
        project.Setup.Floors.Add(new Floor { Name = "Ground", Number = 1, Tables = [first, second] });
        project.Setup.Groups.Add(family);
        var problem = SeatingProblem.Create(project.Setup, project.Priorities);

        var seats = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, family.Id, new SeatId(first.Id, 15));

        Assert.Equal(4, seats!.Count);
        Assert.All(seats, s => Assert.Equal(first.Id, s.TableId));
    }
}
