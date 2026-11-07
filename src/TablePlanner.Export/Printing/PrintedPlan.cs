using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Export.Printing;

/// <summary>A group's part of one table: what a table card is printed for.</summary>
/// <param name="People">How many of the group's people sit at this table.</param>
/// <param name="SeatNumbers">The seats the group has here, numbered around the table (see <see cref="PrintedPlan.SeatNumber"/>), in that order.</param>
public sealed record TablePart(Floor Floor, Table Table, Group Group, int People, IReadOnlyList<int> SeatNumbers);

/// <summary>
/// A seating plan read for printing: who sits where, by floor and table. Seats that no longer exist and groups
/// that were deleted are left out, so an outdated plan still prints what is valid.
/// </summary>
public sealed class PrintedPlan
{
    public PrintedPlan(EventSetup setup, SeatingAssignment assignment)
    {
        Setup = setup;
        var seats = setup.AllTables.SelectMany(TableGeometry.GetSeats).ToDictionary(s => s.Id);
        GroupAt = assignment.Seats
            .Where(s => seats.ContainsKey(s.Seat) && setup.FindGroup(s.GroupId) is not null)
            .GroupBy(s => s.Seat)
            .ToDictionary(g => g.Key, g => g.First().GroupId);

        var parts = new List<TablePart>();
        foreach (var floor in setup.Floors)
        {
            foreach (var table in floor.Tables.OrderBy(t => t.Number))
            {
                // Groups in the order they sit along the table, from the left end.
                var atTable = TableGeometry.GetSeats(table)
                    .Where(s => GroupAt.ContainsKey(s.Id))
                    .GroupBy(s => GroupAt[s.Id])
                    .OrderBy(g => g.Min(SeatOrder))
                    .ThenBy(g => g.Min(s => s.Id.Index));
                foreach (var part in atTable)
                {
                    var numbers = part.Select(s => SeatNumber(table, s)).Order().ToList();
                    parts.Add(new TablePart(floor, table, setup.FindGroup(part.Key)!, part.Count(), numbers));
                }
            }
        }
        Parts = parts;
    }

    public EventSetup Setup { get; }

    /// <summary>The group in each taken seat.</summary>
    public IReadOnlyDictionary<SeatId, Guid> GroupAt { get; }

    /// <summary>Every group's part of every table, by floor, then table number, then along the table.</summary>
    public IReadOnlyList<TablePart> Parts { get; }

    /// <summary>How many of the group's people have a seat.</summary>
    public int Seated(Group group) => Parts.Where(p => p.Group == group).Sum(p => p.People);

    /// <summary>
    /// A seat's number for staff placing cards, counted around the table from 1 (in the table's own frame): the top
    /// side left to right, the right end, the bottom side right to left, the left end. Every seat counts, taken or not,
    /// so the numbers do not change with the plan.
    /// </summary>
    public static int SeatNumber(Table table, SeatLayout seat)
    {
        // Around a round table: clockwise from the top.
        if (seat.Side == SeatSide.Round)
            return seat.PositionAlongSide + 1;
        var n = table.SeatsPerSide;
        var hasRightEnd = table.EndSeats >= 2;
        return seat.Side switch
        {
            SeatSide.Top => seat.PositionAlongSide + 1,
            SeatSide.RightEnd => n + 1,
            SeatSide.Bottom => n + (hasRightEnd ? 1 : 0) + (n - seat.PositionAlongSide),
            _ => 2 * n + (hasRightEnd ? 1 : 0) + 1,
        };
    }

    private static int SeatOrder(SeatLayout seat) => seat.Side switch
    {
        SeatSide.LeftEnd => -1,
        SeatSide.RightEnd => int.MaxValue,
        _ => seat.PositionAlongSide,
    };
}
