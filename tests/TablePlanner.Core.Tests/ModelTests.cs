using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class ModelTests
{
    [Theory]
    [InlineData(1.24, 1.0)]
    [InlineData(1.26, 1.5)]
    [InlineData(0.75, 1.0)]
    [InlineData(-0.75, -1.0)]
    [InlineData(3.0, 3.0)]
    public void Snap_rounds_to_the_nearest_grid_step(double value, double expected)
    {
        Assert.Equal(expected, Grid.Snap(value), 1e-9);
    }

    [Fact]
    public void Snap_with_no_step_leaves_the_value_unchanged()
    {
        Assert.Equal(1.234, Grid.Snap(1.234, 0));
    }

    [Fact]
    public void First_table_number_on_a_floor_starts_at_floor_number_times_100_plus_1()
    {
        Assert.Equal(201, new Floor { Number = 2 }.NextTableNumber());
    }

    [Fact]
    public void Next_table_number_follows_the_highest_number_on_the_floor()
    {
        var floor = new Floor { Number = 1, Tables = [new Table { Number = 101 }, new Table { Number = 105 }] };

        Assert.Equal(106, floor.NextTableNumber());
    }

    [Fact]
    public void Next_table_number_ignores_numbers_below_the_floor_range()
    {
        var floor = new Floor { Number = 3, Tables = [new Table { Number = 7 }] };

        Assert.Equal(301, floor.NextTableNumber());
    }

    [Fact]
    public void Assignment_lookups_find_groups_and_seats()
    {
        var tableId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var assignment = new SeatingAssignment
        {
            Seats = [new(new SeatId(tableId, 0), groupId), new(new SeatId(tableId, 3), groupId)],
        };

        Assert.Equal(groupId, assignment.GroupAt(new SeatId(tableId, 3)));
        Assert.Null(assignment.GroupAt(new SeatId(tableId, 1)));
        Assert.Equal([new SeatId(tableId, 0), new SeatId(tableId, 3)], assignment.SeatsOf(groupId));
    }
}
