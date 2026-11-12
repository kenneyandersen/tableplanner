namespace TablePlanner.Solver.Tests;

public class LevelOrderTests
{
    [Fact]
    public void Every_parent_group_stays_one_run_of_the_order_however_it_is_changed()
    {
        var project = TestPlans.Folder() is null ? null : TestPlans.Load("Company levels 440.tableplanner.json");
        if (project is null)
            return;
        var problem = SeatingProblem.Create(project.Setup, project.Priorities);
        var free = Enumerable.Range(0, problem.Groups.Count).ToArray();
        var levels = new LevelOrder(problem, free);
        var order = levels.Initial(free);
        var random = new Random(3);

        for (var step = 0; step <= 2000; step++)
        {
            Assert.Equal(free.Order(), order.Order());
            for (var c = 0; c < problem.ParentGroups.Count; c++)
            {
                var members = problem.ParentMembers[c].ToHashSet();
                var at = Enumerable.Range(0, order.Length).Where(i => members.Contains(order[i])).ToList();
                Assert.True(at[^1] - at[0] == at.Count - 1, $"'{problem.ParentGroups[c].Name}' is not one run after {step} changes");
            }
            levels.Mutate(order, random);
        }
    }
}
