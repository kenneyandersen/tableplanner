using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class TableGeometryTests
{
    private const double Tolerance = 1e-9;

    private static Table SmallTable(Rotation rotation = Rotation.R0, int endSeats = 0) => new()
    {
        X = 10,
        Y = 5,
        SeatsPerSide = 2,
        EndSeats = endSeats,
        SeatSpacing = 0.6,
        Width = 0.8,
        Rotation = rotation,
    };

    [Fact]
    public void Seats_are_numbered_top_then_bottom_then_ends()
    {
        var seats = TableGeometry.GetSeats(SmallTable(endSeats: 2));

        Assert.Equal([0, 1, 2, 3, 4, 5], seats.Select(s => s.Id.Index));
        Assert.Equal(
            [SeatSide.Top, SeatSide.Top, SeatSide.Bottom, SeatSide.Bottom, SeatSide.LeftEnd, SeatSide.RightEnd],
            seats.Select(s => s.Side));
        Assert.Equal([0, 1, 0, 1, 0, 0], seats.Select(s => s.PositionAlongSide));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 5)]
    [InlineData(2, 6)]
    public void Seat_count_includes_end_seats(int endSeats, int expected)
    {
        var table = SmallTable(endSeats: endSeats);

        Assert.Equal(expected, table.SeatCount);
        Assert.Equal(expected, TableGeometry.GetSeats(table).Count);
    }

    [Fact]
    public void One_end_seat_is_at_the_left_end()
    {
        var seats = TableGeometry.GetSeats(SmallTable(endSeats: 1));

        var end = Assert.Single(seats, s => s.Side is SeatSide.LeftEnd or SeatSide.RightEnd);
        Assert.Equal(SeatSide.LeftEnd, end.Side);
        AssertPoint(new Point(10 - 0.6 - 0.3, 5), end.Position);
    }

    [Fact]
    public void Unrotated_seat_positions_are_beside_the_table_edges()
    {
        var seats = TableGeometry.GetSeats(SmallTable());

        // Length 1.2 m, width 0.8 m; chairs sit 0.3 m outside the edge.
        AssertPoint(new Point(9.7, 4.3), seats[0].Position);
        AssertPoint(new Point(10.3, 4.3), seats[1].Position);
        AssertPoint(new Point(9.7, 5.7), seats[2].Position);
        AssertPoint(new Point(10.3, 5.7), seats[3].Position);
    }

    [Fact]
    public void Seat_i_is_across_from_seat_n_plus_i()
    {
        var table = new Table { SeatsPerSide = 5, X = 3, Y = 3 };
        var seats = TableGeometry.GetSeats(table);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(seats[i].Position.X, seats[5 + i].Position.X, Tolerance);
            Assert.Equal(table.Y - seats[i].Position.Y, seats[5 + i].Position.Y - table.Y, Tolerance);
        }
    }

    [Fact]
    public void Rotating_90_degrees_turns_the_seats_clockwise_around_the_centre()
    {
        var seats = TableGeometry.GetSeats(SmallTable(Rotation.R90));

        // Seat 0 is at (-0.3, -0.7) relative to the centre; clockwise 90° gives (0.7, -0.3).
        AssertPoint(new Point(10.7, 4.7), seats[0].Position);
    }

    [Theory]
    [InlineData(Rotation.R90)]
    [InlineData(Rotation.R180)]
    [InlineData(Rotation.R270)]
    public void Rotation_keeps_each_seat_at_the_same_distance_from_the_centre(Rotation rotation)
    {
        var unrotated = TableGeometry.GetSeats(SmallTable(endSeats: 2));
        var rotated = TableGeometry.GetSeats(SmallTable(rotation, endSeats: 2));

        for (var i = 0; i < unrotated.Count; i++)
            Assert.Equal(DistanceFromCentre(unrotated[i].Position), DistanceFromCentre(rotated[i].Position), Tolerance);
    }

    [Fact]
    public void Rotating_180_degrees_mirrors_through_the_centre()
    {
        var unrotated = TableGeometry.GetSeats(SmallTable());
        var rotated = TableGeometry.GetSeats(SmallTable(Rotation.R180));

        AssertPoint(new Point(20 - unrotated[0].Position.X, 10 - unrotated[0].Position.Y), rotated[0].Position);
    }

    [Theory]
    [InlineData(Rotation.R0, 1.2, 0.8)]
    [InlineData(Rotation.R90, 0.8, 1.2)]
    [InlineData(Rotation.R180, 1.2, 0.8)]
    [InlineData(Rotation.R270, 0.8, 1.2)]
    public void Bounds_swap_width_and_height_on_quarter_turns(Rotation rotation, double width, double height)
    {
        var bounds = TableGeometry.GetBounds(SmallTable(rotation));

        Assert.Equal(width, bounds.Width, Tolerance);
        Assert.Equal(height, bounds.Height, Tolerance);
        Assert.Equal(10, bounds.Left + bounds.Width / 2, Tolerance);
        Assert.Equal(5, bounds.Top + bounds.Height / 2, Tolerance);
    }

    [Fact]
    public void Disabled_seats_are_flagged_and_not_counted_as_available()
    {
        var table = SmallTable();
        table.DisabledSeats = [1, 99];

        var seats = TableGeometry.GetSeats(table);

        Assert.True(seats[1].IsDisabled);
        Assert.Equal(1, seats.Count(s => s.IsDisabled));
        Assert.Equal(3, table.AvailableSeatCount);
    }

    [Fact]
    public void Seat_ids_refer_to_their_table()
    {
        var table = SmallTable();

        Assert.All(TableGeometry.GetSeats(table), s => Assert.Equal(table.Id, s.Id.TableId));
    }

    private static double DistanceFromCentre(Point p) => Math.Sqrt(Math.Pow(p.X - 10, 2) + Math.Pow(p.Y - 5, 2));

    private static void AssertPoint(Point expected, Point actual)
    {
        Assert.Equal(expected.X, actual.X, Tolerance);
        Assert.Equal(expected.Y, actual.Y, Tolerance);
    }
}
