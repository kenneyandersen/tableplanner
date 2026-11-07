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
}
