using TablePlanner.Core.Graph;
using TablePlanner.Core.Model;

namespace TablePlanner.Solver.Tests;

public class SeatSequenceTests
{
    private static SeatingProblem Problem(EventProject project) => SeatingProblem.Create(project.Setup, project.Priorities);

    [Fact]
    public void Fill_order_covers_every_seat_once()
    {
        var problem = Problem(Benchmarks.Example());

        var order = SeatSequence.FillOrder(problem.Graph);

        Assert.Equal(problem.SeatCount, order.Count);
        Assert.Equal(problem.SeatCount, order.Distinct().Count());
    }

    [Fact]
    public void Fill_order_goes_floor_by_floor_and_on_to_adjacent_tables()
    {
        var graph = Problem(Benchmarks.Example()).Graph;

        var tables = SeatSequence.FillOrder(graph).Select(s => graph.Seats[s].TableIndex).Distinct().ToList();

        Assert.Equal(graph.Tables.Count, tables.Count);
        for (var i = 1; i < tables.Count; i++)
            if (graph.Tables[tables[i]].FloorIndex == graph.Tables[tables[i - 1]].FloorIndex)
                Assert.True(graph.AreAdjacent(tables[i - 1], tables[i]), $"Table {graph.Tables[tables[i]].Table.Number} follows a table that is not adjacent");
        Assert.True(tables.Select(t => graph.Tables[t].FloorIndex).SequenceEqual(tables.Select(t => graph.Tables[t].FloorIndex).Order()));
    }

    [Fact]
    public void Consecutive_seats_are_close_together()
    {
        var graph = Problem(Benchmarks.Example()).Graph;
        var order = SeatSequence.FillOrder(graph);

        for (var i = 1; i < order.Count; i++)
        {
            var (a, b) = (graph.Seats[order[i - 1]], graph.Seats[order[i]]);
            if (a.FloorIndex != b.FloorIndex)
                continue;
            var distance = Math.Sqrt(Math.Pow(a.Position.X - b.Position.X, 2) + Math.Pow(a.Position.Y - b.Position.Y, 2));
            // Rows are 2.5 m apart, so turning from one row to the next can be that far.
            Assert.True(distance <= 2.5 + 1e-9, $"Seat {i} is {distance:0.0} m from the one before");
        }
    }

    [Fact]
    public void A_small_group_starts_at_the_next_table_rather_than_splitting_when_seats_are_spare()
    {
        var project = new EventProject();
        project.Setup.Floors.Add(new Floor
        {
            Name = "Ground",
            Number = 1,
            Tables = [new Table { Number = 101, SeatsPerSide = 3 }, new Table { Number = 102, SeatsPerSide = 3, X = 2.5 }],
        });
        project.Setup.Groups = [new Group { Name = "A", PeopleCount = 4 }, new Group { Name = "B", PeopleCount = 4 }];
        var problem = Problem(project);
        var sequence = new SeatSequence(problem, problem.EmptyPlan());
        var plan = problem.EmptyPlan();

        sequence.Decode([0, 1], plan);

        var tablesOfB = Enumerable.Range(0, plan.Length).Where(s => plan[s] == 1).Select(s => problem.Graph.Seats[s].TableIndex).Distinct();
        Assert.Single(tablesOfB);
    }

    [Fact]
    public void Groups_that_do_not_fit_are_skipped_and_later_ones_still_placed()
    {
        var project = new EventProject();
        project.Setup.Floors.Add(new Floor { Name = "Ground", Number = 1, Tables = [new Table { Number = 101, SeatsPerSide = 3 }] });
        project.Setup.Groups = [new Group { Name = "A", PeopleCount = 4 }, new Group { Name = "Big", PeopleCount = 5 }, new Group { Name = "C", PeopleCount = 2 }];
        var problem = Problem(project);
        var plan = problem.EmptyPlan();

        new SeatSequence(problem, problem.EmptyPlan()).Decode([0, 1, 2], plan);

        Assert.Equal(4, plan.Count(g => g == 0));
        Assert.Equal(0, plan.Count(g => g == 1));
        Assert.Equal(2, plan.Count(g => g == 2));
    }
}
