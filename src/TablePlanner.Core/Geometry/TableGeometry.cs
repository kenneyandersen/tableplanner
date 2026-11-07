using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>Which side of the table a seat is on, in the table's own (unrotated) frame.</summary>
public enum SeatSide
{
    Top,
    Bottom,
    LeftEnd,
    RightEnd,

    /// <summary>Around a round table; the position along it counts clockwise from the top.</summary>
    Round,
}

/// <summary>A generated seat: where it is and where it sits on its table.</summary>
public sealed record SeatLayout(SeatId Id, SeatSide Side, int PositionAlongSide, Point Position, bool IsDisabled);

/// <summary>Generates seats and outline for one table shape. Add an implementation per shape (e.g. round tables).</summary>
public interface ITableShapeGeometry
{
    TableShape Shape { get; }

    IReadOnlyList<SeatLayout> GetSeats(Table table);

    /// <summary>The table top's outline in floor coordinates.</summary>
    Rect GetBounds(Table table);
}

/// <summary>Entry point for table geometry; dispatches to the geometry of the table's shape.</summary>
public static class TableGeometry
{
    /// <summary>Distance from the table edge to the centre of a chair, in metres.</summary>
    public const double ChairOffset = 0.3;

    private static readonly Dictionary<TableShape, ITableShapeGeometry> Shapes = new()
    {
        [TableShape.Long] = new LongTableGeometry(),
        [TableShape.Round] = new RoundTableGeometry(),
    };

    public static IReadOnlyList<SeatLayout> GetSeats(Table table) => For(table).GetSeats(table);

    public static Rect GetBounds(Table table) => For(table).GetBounds(table);

    /// <summary>Corner radius for drawing the table top as a rectangle over <see cref="GetBounds"/>: half the size makes a circle.</summary>
    public static double CornerRadius(Table table) => table.IsRound ? table.Diameter / 2 : 0.05;

    /// <summary>The shortest distance between two table tops (circles for round tables); 0 when they touch or overlap.</summary>
    public static double Distance(Table a, Table b) => Math.Max(0, SignedDistance(a, b));

    /// <summary>True when two table tops share an area (touching does not count).</summary>
    public static bool Overlap(Table a, Table b) =>
        a.IsRound || b.IsRound ? SignedDistance(a, b) < -1e-6 : GetBounds(a).Overlaps(GetBounds(b));

    /// <summary>Like <see cref="Distance"/>, but negative when a round table overlaps the other table.</summary>
    private static double SignedDistance(Table a, Table b)
    {
        if (a.IsRound && b.IsRound)
        {
            var (dx, dy) = (a.X - b.X, a.Y - b.Y);
            return Math.Sqrt(dx * dx + dy * dy) - a.Diameter / 2 - b.Diameter / 2;
        }
        if (a.IsRound || b.IsRound)
        {
            var (round, other) = a.IsRound ? (a, b) : (b, a);
            var rect = GetBounds(other);
            var nx = Math.Clamp(round.X, rect.Left, rect.Right);
            var ny = Math.Clamp(round.Y, rect.Top, rect.Bottom);
            var (dx, dy) = (round.X - nx, round.Y - ny);
            return Math.Sqrt(dx * dx + dy * dy) - round.Diameter / 2;
        }
        return GetBounds(a).DistanceTo(GetBounds(b));
    }

    private static ITableShapeGeometry For(Table table) =>
        Shapes.TryGetValue(table.Shape, out var geometry)
            ? geometry
            : throw new NotSupportedException($"Table shape {table.Shape} is not supported.");
}

/// <summary>
/// Seat numbering on a long table with n seats per side:
/// top side 0 … n-1 (left to right), bottom side n … 2n-1 (left to right, so seat i is across from n+i),
/// then the left end seat (2n) and the right end seat (2n+1) when present.
/// "Left", "top" etc. refer to the unrotated table; rotation turns the whole table around its centre.
/// </summary>
public sealed class LongTableGeometry : ITableShapeGeometry
{
    public TableShape Shape => TableShape.Long;

    public IReadOnlyList<SeatLayout> GetSeats(Table table)
    {
        var n = table.SeatsPerSide;
        var halfLength = table.Length / 2;
        var sideOffset = table.Width / 2 + TableGeometry.ChairOffset;
        var seats = new List<SeatLayout>(table.SeatCount);

        for (var i = 0; i < n; i++)
            seats.Add(Seat(table, i, SeatSide.Top, i, new Point(-halfLength + table.SeatSpacing * (i + 0.5), -sideOffset)));
        for (var i = 0; i < n; i++)
            seats.Add(Seat(table, n + i, SeatSide.Bottom, i, new Point(-halfLength + table.SeatSpacing * (i + 0.5), sideOffset)));

        var endOffset = halfLength + TableGeometry.ChairOffset;
        if (table.EndSeats >= 1)
            seats.Add(Seat(table, 2 * n, SeatSide.LeftEnd, 0, new Point(-endOffset, 0)));
        if (table.EndSeats >= 2)
            seats.Add(Seat(table, 2 * n + 1, SeatSide.RightEnd, 0, new Point(endOffset, 0)));

        return seats;
    }

    public Rect GetBounds(Table table)
    {
        var quarterTurn = table.Rotation is Rotation.R90 or Rotation.R270;
        return quarterTurn
            ? Rect.Centered(table.X, table.Y, table.Width, table.Length)
            : Rect.Centered(table.X, table.Y, table.Length, table.Width);
    }

    /// <summary>
    /// Where a seat ends up when the table changes from <paramref name="oldPerSide"/>/<paramref name="oldEnds"/>
    /// to <paramref name="newPerSide"/>/<paramref name="newEnds"/>. A side seat keeps its side; its position from the
    /// left moves by <paramref name="shiftFromLeft"/> (seats added at the left end; negative when seats are removed
    /// there). Returns null when the seat no longer exists.
    /// </summary>
    public static int? RemapSeatIndex(int index, int oldPerSide, int oldEnds, int newPerSide, int newEnds, int shiftFromLeft = 0)
    {
        if (index < 0)
            return null;
        if (index < 2 * oldPerSide)
        {
            var bottom = index >= oldPerSide;
            var along = (bottom ? index - oldPerSide : index) + shiftFromLeft;
            if (along < 0 || along >= newPerSide)
                return null;
            return bottom ? newPerSide + along : along;
        }

        var end = index - 2 * oldPerSide; // 0 = left end, 1 = right end
        return end < oldEnds && end < newEnds ? 2 * newPerSide + end : null;
    }

    private static SeatLayout Seat(Table table, int index, SeatSide side, int along, Point local) =>
        new(new SeatId(table.Id, index), side, along,
            local.Rotate(table.Rotation).Offset(table.X, table.Y),
            table.DisabledSeats.Contains(index));
}

/// <summary>
/// Seats around a round table, numbered 0 … n-1 clockwise from the top (in the table's own frame; rotation turns
/// where seat 0 is). The outline is the square around the circle.
/// </summary>
public sealed class RoundTableGeometry : ITableShapeGeometry
{
    public TableShape Shape => TableShape.Round;

    public IReadOnlyList<SeatLayout> GetSeats(Table table)
    {
        var n = table.RoundSeats;
        var radius = table.Diameter / 2 + TableGeometry.ChairOffset;
        var seats = new List<SeatLayout>(n);
        for (var i = 0; i < n; i++)
        {
            var angle = 2 * Math.PI * i / n;
            var local = new Point(radius * Math.Sin(angle), -radius * Math.Cos(angle));
            seats.Add(new SeatLayout(new SeatId(table.Id, i), SeatSide.Round, i,
                local.Rotate(table.Rotation).Offset(table.X, table.Y), table.DisabledSeats.Contains(i)));
        }
        return seats;
    }

    public Rect GetBounds(Table table) => Rect.Centered(table.X, table.Y, table.Diameter, table.Diameter);
}
