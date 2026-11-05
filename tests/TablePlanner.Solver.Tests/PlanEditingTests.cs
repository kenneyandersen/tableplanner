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
    public void Move_takes_free_seats_along_the_fill_order_from_the_clicked_seat()
    {
        var (project, problem, table, a, _) = Setup();

        var seats = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 3));

        Assert.Equal([3, 8, 4], seats!.Select(s => s.Index));
    }

    [Fact]
    public void Move_skips_seats_of_other_groups_and_may_reuse_its_own()
    {
        var (project, problem, table, a, _) = Setup();

        // Starting at bottom 5 (A's own): 5, then 1 (own); 6, 2, 7 are B's; then 3.
        var seats = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 5));

        Assert.Equal([5, 1, 3], seats!.Select(s => s.Index));
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
    public void Move_stays_on_the_clicked_floor_and_uses_seats_before_the_click_when_needed()
    {
        var (project, _, table, a, _) = Setup();
        var upstairs = new Table { Number = 201, SeatsPerSide = 5 };
        project.Setup.Floors.Add(new Floor { Name = "First", Number = 2, Tables = [upstairs] });
        var problem = SeatingProblem.Create(project.Setup, project.Priorities);

        // Clicking the last seat in fill order (bottom 9): 9 is free, then back to 4, 8, 3.
        var nearEnd = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 9));
        a.PeopleCount = 8;
        problem = SeatingProblem.Create(project.Setup, project.Priorities);
        var tooBig = PlanEditing.SeatsForMove(problem, project.CurrentAssignment, a.Id, new SeatId(table.Id, 9));

        Assert.Equal([9, 4, 8], nearEnd!.Select(s => s.Index));
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
}
