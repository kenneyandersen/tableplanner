using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Solver.Tests;

public class PlanPolisherTests
{
    [Fact]
    public async Task Swaps_a_part_of_a_group_back_to_the_rest_of_it()
    {
        var (problem, sequence) = SeatSequenceTests.TwoAreasFull();
        var evaluator = new PlanEvaluator(problem);
        // A (4) and 2 of B at the first table; the other 4 of B and C (2) at the far table.
        var plan = problem.EmptyPlan();
        int[] groups = [0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 2, 2];
        for (var i = 0; i < sequence.Length; i++)
            plan[sequence.Seats[i]] = groups[i];
        var before = evaluator.Score(plan);

        var swaps = await new PlanPolisher(problem, sequence, evaluator).PolishAsync(plan, () => false, () => Task.CompletedTask);

        Assert.True(swaps > 0);
        Assert.True(evaluator.Score(plan) > before);
        var summary = evaluator.Summarize(plan);
        Assert.All(summary.Groups, g => Assert.False(g.IsSplit, $"{g.Name} is split"));
    }

    [Fact]
    public async Task Leaves_a_plan_alone_when_no_swap_helps()
    {
        var (problem, sequence) = SeatSequenceTests.TwoAreasFull();
        var evaluator = new PlanEvaluator(problem);
        var plan = problem.EmptyPlan();
        sequence.Decode([0, 1, 2], plan);
        var copy = (int[])plan.Clone();

        var swaps = await new PlanPolisher(problem, sequence, evaluator).PolishAsync(plan, () => false, () => Task.CompletedTask);

        Assert.Equal(0, swaps);
        Assert.Equal(copy, plan);
    }

    [Fact]
    public async Task Frees_a_taken_end_seat_by_moving_its_person_to_the_rest_of_the_group_at_the_next_table()
    {
        // The user's case: table A (4 a side + a left end seat) full, its end seat held by one of Beti, the rest of Beti
        // at table B next to it, then Mobile, and 2 free seats at the far end of B. Better: that person sits with Beti
        // at B (Mobile moves one seat along) and A's end seat is free.
        var project = new EventProject();
        var a = new Table { Number = 1, SeatsPerSide = 4, EndSeats = 1, X = 3, Y = 2 };
        var b = new Table { Number = 2, SeatsPerSide = 10, X = 4.8, Y = 4 };
        project.Setup.Floors.Add(new Floor { Name = "Hall", Number = 1, Tables = [a, b] });
        project.Setup.Groups.Add(new Group { Name = "Group 40", PeopleCount = 8 });
        project.Setup.Groups.Add(new Group { Name = "Beti", PeopleCount = 5 });
        project.Setup.Groups.Add(new Group { Name = "Mobile", PeopleCount = 14 });
        var problem = SeatingProblem.Create(project.Setup, project.Priorities);
        var sequence = new SeatSequence(problem, problem.EmptyPlan());
        var evaluator = new PlanEvaluator(problem);

        var plan = problem.EmptyPlan();
        var seats = problem.Graph.Seats;
        var endSeat = seats.Single(s => s.Side == SeatSide.LeftEnd).Index;
        foreach (var s in seats.Where(s => s.TableIndex == 0 && s.Index != endSeat))
            plan[s.Index] = 0;
        plan[endSeat] = 1;
        var atB = sequence.Seats.Where(s => seats[s].TableIndex == 1).ToList();
        for (var i = 0; i < 18; i++)
            plan[atB[i]] = i < 4 ? 1 : 2;

        var swaps = await new PlanPolisher(problem, sequence, evaluator).PolishAsync(plan, () => false, () => Task.CompletedTask);

        Assert.True(swaps > 0);
        Assert.Equal(-1, plan[endSeat]);
        Assert.Equal(27, plan.Count(g => g >= 0));
        Assert.All(evaluator.Summarize(plan).Groups, g => Assert.Single(g.TableNumbers));
        // Not just on the same table: each of Beti sits next to (or across from) another of Beti.
        var beti = Enumerable.Range(0, plan.Length).Where(s => plan[s] == 1).ToList();
        Assert.All(beti, s => Assert.Contains(problem.Graph.Edges, e => (e.A == s && plan[e.B] == 1) || (e.B == s && plan[e.A] == 1)));
    }
}
