using TablePlanner.Core.Graph;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class SeatGraphTests
{
    private static EventSetup Setup(params Table[] tables) =>
        new() { Floors = [new Floor { Name = "Ground", Number = 1, Tables = [.. tables] }] };

    private static SeatRelation? Relation(SeatGraph graph, Table a, int seatA, Table b, int seatB)
    {
        var x = graph.IndexOf(new SeatId(a.Id, seatA))!.Value;
        var y = graph.IndexOf(new SeatId(b.Id, seatB))!.Value;
        return graph.EdgesOf(x).FirstOrDefault(e => e.A == y || e.B == y)?.Relation;
    }

    [Fact]
    public void Seats_on_one_table_are_next_to_across_and_diagonal()
    {
        var table = new Table { SeatsPerSide = 4 };
        var graph = SeatGraph.Build(Setup(table));

        Assert.Equal(SeatRelation.NextTo, Relation(graph, table, 0, table, 1));
        Assert.Equal(SeatRelation.NextTo, Relation(graph, table, 5, table, 6));
        Assert.Equal(SeatRelation.Across, Relation(graph, table, 1, table, 5));
        Assert.Equal(SeatRelation.Diagonal, Relation(graph, table, 1, table, 6));
        Assert.Null(Relation(graph, table, 0, table, 2));
        // 4 × 2 sides: 6 next-to, 4 across, 6 diagonal.
        Assert.Equal(16, graph.Edges.Count);
    }

    [Fact]
    public void End_seats_are_next_to_the_corner_seats()
    {
        var table = new Table { SeatsPerSide = 3, EndSeats = 2 };
        var graph = SeatGraph.Build(Setup(table));

        Assert.Equal(SeatRelation.NextTo, Relation(graph, table, 6, table, 0));
        Assert.Equal(SeatRelation.NextTo, Relation(graph, table, 6, table, 3));
        Assert.Equal(SeatRelation.NextTo, Relation(graph, table, 7, table, 2));
        Assert.Equal(SeatRelation.NextTo, Relation(graph, table, 7, table, 5));
        Assert.Null(Relation(graph, table, 6, table, 7));
    }

    [Fact]
    public void Disabled_seats_are_not_in_the_graph()
    {
        var table = new Table { SeatsPerSide = 3, DisabledSeats = [1] };
        var graph = SeatGraph.Build(Setup(table));

        Assert.Equal(5, graph.Seats.Count);
        Assert.Null(graph.IndexOf(new SeatId(table.Id, 1)));
        Assert.Null(Relation(graph, table, 0, table, 2));
    }

    [Fact]
    public void Rows_of_tables_sit_back_to_back()
    {
        // Two 3 m tables, 2.5 m apart (centre to centre): the bottom side of the first faces away from the top side of the second.
        var a = new Table { SeatsPerSide = 5, X = 0, Y = 0 };
        var b = new Table { SeatsPerSide = 5, X = 0, Y = 2.5 };
        var graph = SeatGraph.Build(Setup(a, b));

        Assert.True(graph.AreAdjacent(0, 1));
        Assert.Equal(SeatRelation.BackToBack, Relation(graph, a, 5, b, 0));
        Assert.Equal(SeatRelation.BackToBack, Relation(graph, a, 9, b, 4));
        Assert.Null(Relation(graph, a, 0, b, 0));
        Assert.Null(Relation(graph, a, 5, b, 5));
    }

    [Fact]
    public void Tables_end_to_end_continue_the_row()
    {
        var a = new Table { SeatsPerSide = 5, X = 0 };
        var b = new Table { SeatsPerSide = 5, X = 3.5 };
        var graph = SeatGraph.Build(Setup(a, b));

        Assert.Equal(SeatRelation.NextTo, Relation(graph, a, 4, b, 0));
        Assert.Equal(SeatRelation.NextTo, Relation(graph, a, 9, b, 5));
        Assert.Null(Relation(graph, a, 4, b, 5));
    }

    [Fact]
    public void End_seats_of_neighbouring_tables_are_back_to_back()
    {
        var a = new Table { SeatsPerSide = 8, EndSeats = 2, X = 4 };
        var b = new Table { SeatsPerSide = 8, EndSeats = 2, X = 10 };
        var graph = SeatGraph.Build(Setup(a, b));

        Assert.Equal(SeatRelation.BackToBack, Relation(graph, a, 17, b, 16));
    }

    [Fact]
    public void Rotated_tables_side_by_side_are_back_to_back()
    {
        var a = new Table { SeatsPerSide = 5, X = 0, Rotation = Rotation.R90 };
        var b = new Table { SeatsPerSide = 5, X = 2.5, Rotation = Rotation.R90 };
        var graph = SeatGraph.Build(Setup(a, b));

        Assert.True(graph.AreAdjacent(0, 1));
        Assert.Contains(graph.Edges, e => e.Relation == SeatRelation.BackToBack);
    }

    [Fact]
    public void Distant_tables_and_tables_on_other_floors_are_not_adjacent()
    {
        var a = new Table { SeatsPerSide = 5, X = 0 };
        var far = new Table { SeatsPerSide = 5, X = 10 };
        var upstairs = new Table { SeatsPerSide = 5, X = 0 };
        var setup = Setup(a, far);
        setup.Floors.Add(new Floor { Name = "First", Number = 2, Tables = [upstairs] });

        var graph = SeatGraph.Build(setup);

        Assert.False(graph.AreAdjacent(0, 1));
        Assert.False(graph.AreAdjacent(0, 2));
        Assert.All(graph.AdjacentTables, Assert.Empty);
        Assert.Equal(1, graph.Seats.First(s => s.Id.TableId == upstairs.Id).FloorIndex);
    }

    [Fact]
    public void Example_project_rows_are_adjacent_but_not_across_the_room()
    {
        var graph = SeatGraph.Build(ExampleProject.Create().Setup);

        // Ground floor: 3 × 3 tables; the middle one touches all 8 others, a corner one touches 3.
        Assert.Equal(8, graph.AdjacentTables[4].Count);
        Assert.Equal(3, graph.AdjacentTables[0].Count);
        // 1st floor: 2 × 2, wider apart, so the diagonal neighbour (2.1 m away) is not adjacent.
        Assert.All(Enumerable.Range(9, 4), t => Assert.Equal(2, graph.AdjacentTables[t].Count));
    }

    [Fact]
    public void Adjacent_table_pairs_match_the_graph()
    {
        var setup = ExampleProject.Create().Setup;
        var graph = SeatGraph.Build(setup);

        var pairs = setup.Floors.SelectMany(f => SeatGraph.AdjacentTablePairs(f)).Count();

        Assert.Equal(graph.AdjacentTables.Sum(a => a.Count) / 2, pairs);
    }
}
