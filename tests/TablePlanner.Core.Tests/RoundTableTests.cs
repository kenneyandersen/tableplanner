using TablePlanner.Core.Editing;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Graph;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Core.Tests;

public class RoundTableTests
{
    private const double Tolerance = 1e-9;

    private static Table Round(int seats, double x = 0, double y = 0) =>
        new() { Shape = TableShape.Round, RoundSeats = seats, X = x, Y = y };

    private static EventSetup Setup(params Table[] tables) =>
        new() { Floors = [new Floor { Name = "Ground", Number = 1, Tables = [.. tables] }] };

    [Fact]
    public void Seats_go_clockwise_from_the_top_around_the_table()
    {
        var table = Round(4, 10, 10);

        var seats = TableGeometry.GetSeats(table);

        var r = table.Diameter / 2 + TableGeometry.ChairOffset;
        Assert.Equal(4, seats.Count);
        Assert.All(seats, s => Assert.Equal(SeatSide.Round, s.Side));
        Assert.Equal(10, seats[0].Position.X, Tolerance);
        Assert.Equal(10 - r, seats[0].Position.Y, Tolerance);
        Assert.Equal(10 + r, seats[1].Position.X, Tolerance);
        Assert.Equal(10 + r, seats[2].Position.Y, Tolerance);
        Assert.Equal(10 - r, seats[3].Position.X, Tolerance);
    }

    [Fact]
    public void Diameter_grows_with_the_seats()
    {
        Assert.Equal(8 * 0.6 / Math.PI, Round(8).Diameter, Tolerance);
        Assert.True(Round(10).Diameter > Round(8).Diameter);
        Assert.Equal(0.9, Round(2).Diameter);
        Assert.Equal(new Rect(-Round(8).Diameter / 2, -Round(8).Diameter / 2, Round(8).Diameter, Round(8).Diameter), TableGeometry.GetBounds(Round(8)));
    }

    [Fact]
    public void Neighbours_around_the_table_are_next_to_each_other_including_the_last_and_first()
    {
        var table = Round(6);
        var graph = SeatGraph.Build(Setup(table));

        Assert.Equal(6, graph.Edges.Count);
        Assert.All(graph.Edges, e => Assert.Equal(SeatRelation.NextTo, e.Relation));
        var first = graph.IndexOf(new SeatId(table.Id, 0))!.Value;
        var last = graph.IndexOf(new SeatId(table.Id, 5))!.Value;
        Assert.Contains(graph.EdgesOf(first), e => e.A == last || e.B == last);
    }

    [Fact]
    public void Round_tables_are_adjacent_by_the_distance_between_their_edges()
    {
        var a = Round(8);
        var near = Round(8, a.Diameter + 1.9);
        // Diagonally: the square outlines are 1.5 m apart, but the circles about 2.3 m.
        var diagonal = Round(8, a.Diameter + 1.5, a.Diameter + 1.5);

        Assert.True(TableGeometry.Distance(a, near) <= 2.0);
        Assert.True(TableGeometry.Distance(a, diagonal) > 2.0);
        var graph = SeatGraph.Build(Setup(a, near, diagonal));
        Assert.True(graph.AreAdjacent(0, 1));
        Assert.False(graph.AreAdjacent(0, 2));
    }

    [Fact]
    public void Round_and_long_tables_measure_distance_and_overlap_from_the_circle()
    {
        var round = Round(8);
        // A long table of 4 per side is 2.4 m long: its left end touches the circle.
        var touching = new Table { SeatsPerSide = 4, X = round.Diameter / 2 + 1.2, Y = 0 };
        var overlapping = new Table { SeatsPerSide = 4, X = round.Diameter / 2 + 1.0, Y = 0 };

        Assert.Equal(0, TableGeometry.Distance(round, touching), 1e-6);
        Assert.False(TableGeometry.Overlap(round, touching));
        Assert.True(TableGeometry.Overlap(round, overlapping));
    }

    [Fact]
    public void Resizing_keeps_seat_numbers_and_drops_the_last_seats()
    {
        var project = new EventProject();
        var floor = SetupOperations.AddFloor(project);
        var table = SetupOperations.AddRoundTable(project, floor.Id, 5, 5, 8);
        var group = SetupOperations.AddGroup(project, "A", 2);
        table.DisabledSeats = [1, 7];
        project.CurrentAssignment.Seats.AddRange([new(new(table.Id, 2), group.Id), new(new(table.Id, 6), group.Id)]);

        SetupOperations.ResizeRoundTable(project, table.Id, 6);

        Assert.Equal(6, table.SeatCount);
        Assert.Equal([1], table.DisabledSeats);
        Assert.Equal([new SeatId(table.Id, 2)], project.CurrentAssignment.SeatsOf(group.Id));
        Assert.Equal((5.0, 5.0), (table.X, table.Y));

        SetupOperations.ResizeRoundTable(project, table.Id, 1);
        Assert.Equal(Table.MinRoundSeats, table.RoundSeats);
    }

    [Fact]
    public void Changing_the_shape_keeps_the_seat_count_and_clears_the_tables_seats()
    {
        var project = new EventProject();
        var floor = SetupOperations.AddFloor(project);
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0, seatsPerSide: 5, endSeats: 0);
        var group = SetupOperations.AddGroup(project, "A", 1);
        table.DisabledSeats = [3];
        project.CurrentAssignment.Seats.Add(new(new(table.Id, 0), group.Id));

        SetupOperations.SetTableShape(project, table.Id, TableShape.Round);

        Assert.True(table.IsRound);
        Assert.Equal(10, table.SeatCount);
        Assert.Empty(table.DisabledSeats);
        Assert.Empty(project.CurrentAssignment.Seats);

        SetupOperations.SetTableShape(project, table.Id, TableShape.Long);
        Assert.Equal((5, 0), (table.SeatsPerSide, table.EndSeats));
    }

    [Fact]
    public void Round_tables_survive_saving_and_loading()
    {
        var project = new EventProject();
        var floor = SetupOperations.AddFloor(project);
        SetupOperations.AddRoundTable(project, floor.Id, 3, 4, 10);

        var loaded = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)).Project!;

        var table = Assert.Single(loaded.Setup.AllTables);
        Assert.Equal((TableShape.Round, 10), (table.Shape, table.RoundSeats));
    }

    [Fact]
    public void Group_labels_on_a_round_table_sit_towards_the_groups_seats()
    {
        var table = Round(8, 10, 10);
        var a = new Group { Name = "A", PeopleCount = 2 };
        var setup = Setup(table);
        setup.Groups.Add(a);
        var seats = TableGeometry.GetSeats(table);
        // Seats 1 and 2 are to the right of the centre.
        var groupAt = new Dictionary<SeatId, Guid> { [seats[1].Id] = a.Id, [seats[2].Id] = a.Id };

        var label = Assert.Single(GroupLabels.For(table, seats, groupAt, setup));

        Assert.True(label.Position.X > 10);
        Assert.True(label.Position.X < 10 + table.Diameter / 2);
    }
}
