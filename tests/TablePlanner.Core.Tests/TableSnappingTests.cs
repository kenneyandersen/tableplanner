using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class TableSnappingTests
{
    [Fact]
    public void A_shorter_table_in_the_next_row_lines_its_seats_up_with_the_longer_one()
    {
        // 10 seats a side (6 m) at x 0–6, and a table of 7 a side (4.2 m) dragged into the row below, a little off.
        var row = new Table { Number = 1, SeatsPerSide = 10, X = 3, Y = 0 };
        var moving = new Table { Number = 2, SeatsPerSide = 7, X = 10, Y = 2.5 };

        // Dragged so its left end is at 0.1 (centre 2.2): 0.1 m off the long table's left end and its seats.
        var (dx, dy, guides) = TableSnapping.Snap(moving, 2.2 - 10, 0, [row]);

        Assert.NotNull(dx);
        var left = moving.X - moving.Length / 2 + dx.Value; // the left end after snapping
        Assert.Equal(0, left, 6);
        var seatX = TableGeometry.GetSeats(moving).First().Position.X + dx.Value;
        Assert.Contains(TableGeometry.GetSeats(row), s => Math.Abs(s.Position.X - seatX) < 1e-6);
        Assert.Null(dy);
        Assert.Contains(guides, g => g.Vertical);
    }

    [Fact]
    public void A_table_snaps_end_to_end_onto_its_neighbours_line()
    {
        var neighbour = new Table { Number = 1, SeatsPerSide = 5, X = 0, Y = 0 };
        var moving = new Table { Number = 2, SeatsPerSide = 5, X = 10, Y = 3 };

        // Dragged beside it, 0.15 m too low: snaps to the same line.
        var (_, dy, guides) = TableSnapping.Snap(moving, -6.6, -2.85, [neighbour]);

        Assert.Equal(-3, dy!.Value, 6);
        Assert.Contains(guides, g => !g.Vertical && Math.Abs(g.At - neighbour.Y) <= 1); // its middle, an edge or its seat row
    }

    [Fact]
    public void Tables_far_away_or_out_of_reach_do_not_pull()
    {
        var far = new Table { Number = 1, SeatsPerSide = 5, X = 0, Y = 20 };
        var moving = new Table { Number = 2, SeatsPerSide = 5, X = 10, Y = 0 };

        var (dx, dy, guides) = TableSnapping.Snap(moving, -9.95, 0, [far]);

        Assert.Null(dx);
        Assert.Null(dy);
        Assert.Empty(guides);
    }
}
