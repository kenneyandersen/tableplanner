using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>Which side of the table a seat is on, in the table's own (unrotated) frame.</summary>
public enum SeatSide
{
    Top,
    Bottom,
    LeftEnd,
    RightEnd,
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
    };

    public static IReadOnlyList<SeatLayout> GetSeats(Table table) => For(table).GetSeats(table);

    public static Rect GetBounds(Table table) => For(table).GetBounds(table);

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
    /// to <paramref name="newPerSide"/>/<paramref name="newEnds"/>, keeping its side and position from the left.
    /// Returns null when the seat no longer exists.
    /// </summary>
    public static int? RemapSeatIndex(int index, int oldPerSide, int oldEnds, int newPerSide, int newEnds)
    {
        if (index < 0)
            return null;
        if (index < oldPerSide)
            return index < newPerSide ? index : null;
        if (index < 2 * oldPerSide)
        {
            var along = index - oldPerSide;
            return along < newPerSide ? newPerSide + along : null;
        }

        var end = index - 2 * oldPerSide; // 0 = left end, 1 = right end
        return end < oldEnds && end < newEnds ? 2 * newPerSide + end : null;
    }

    private static SeatLayout Seat(Table table, int index, SeatSide side, int along, Point local) =>
        new(new SeatId(table.Id, index), side, along,
            local.Rotate(table.Rotation).Offset(table.X, table.Y),
            table.DisabledSeats.Contains(index));
}
