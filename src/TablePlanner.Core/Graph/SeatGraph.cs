using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Graph;

/// <summary>How two seats relate. Only the close relations are edges; "same table" and "adjacent table" follow from the tables.</summary>
public enum SeatRelation
{
    /// <summary>Side by side on the same side of a table, around a corner to an end seat, or across the joint of two tables placed end to end.</summary>
    NextTo,

    /// <summary>Facing each other across the table.</summary>
    Across,

    /// <summary>Across the table, one seat along.</summary>
    Diagonal,

    /// <summary>Backs to each other, at two tables next to each other.</summary>
    BackToBack,
}

/// <summary>A usable (not disabled) seat in the graph. <see cref="Index"/> is its position in <see cref="SeatGraph.Seats"/>.</summary>
public sealed record SeatNode(int Index, SeatId Id, int TableIndex, int FloorIndex, SeatSide Side, int PositionAlongSide, Point Position, Point Facing);

/// <summary>A table in the graph, with its usable seats.</summary>
public sealed record TableNode(int Index, Table Table, int FloorIndex, IReadOnlyList<int> Seats);

public sealed record SeatEdge(int A, int B, SeatRelation Relation);

/// <summary>Distances that decide which seats and tables count as close. In metres.</summary>
public sealed record SeatGraphOptions
{
    public static SeatGraphOptions Default { get; } = new();

    /// <summary>Tables whose tops are at most this far apart are adjacent.</summary>
    public double AdjacentTableGap { get; init; } = 2.0;

    /// <summary>Seats at different tables that face the same way and are at most this far apart are next to each other (tables end to end).</summary>
    public double NextToAcrossTablesDistance { get; init; } = 1.3;

    /// <summary>Seats at different tables whose backs face each other and are at most this far apart are back to back.</summary>
    public double BackToBackDistance { get; init; } = 1.5;
}

/// <summary>
/// The seats of a setup and how close they are to each other, derived from the table geometry.
/// The solver works only on this graph and the groups, never on coordinates.
/// </summary>
public sealed class SeatGraph
{
    private readonly Dictionary<SeatId, int> _indexById;
    private readonly bool[] _adjacent;
    private readonly IReadOnlyList<SeatEdge>[] _edgesBySeat;

    private SeatGraph(IReadOnlyList<Floor> floors, List<TableNode> tables, List<SeatNode> seats, List<SeatEdge> edges, bool[] adjacent)
    {
        Floors = floors;
        Tables = tables;
        Seats = seats;
        Edges = edges;
        _adjacent = adjacent;
        _indexById = seats.ToDictionary(s => s.Id, s => s.Index);

        var bySeat = seats.Select(_ => new List<SeatEdge>()).ToArray();
        foreach (var edge in edges)
        {
            bySeat[edge.A].Add(edge);
            bySeat[edge.B].Add(edge);
        }
        _edgesBySeat = bySeat;

        AdjacentTables = tables
            .Select(t => (IReadOnlyList<int>)tables.Where(o => o.Index != t.Index && AreAdjacent(t.Index, o.Index)).Select(o => o.Index).ToList())
            .ToList();
    }

    /// <summary>Floors in the order of the setup. Seat and table nodes refer to them by index.</summary>
    public IReadOnlyList<Floor> Floors { get; }

    public IReadOnlyList<TableNode> Tables { get; }

    public IReadOnlyList<SeatNode> Seats { get; }

    /// <summary>Each pair of related seats once.</summary>
    public IReadOnlyList<SeatEdge> Edges { get; }

    /// <summary>For each table, the tables adjacent to it (same floor, close enough).</summary>
    public IReadOnlyList<IReadOnlyList<int>> AdjacentTables { get; }

    public int? IndexOf(SeatId id) => _indexById.TryGetValue(id, out var index) ? index : null;

    public IReadOnlyList<SeatEdge> EdgesOf(int seat) => _edgesBySeat[seat];

    public bool AreAdjacent(int tableA, int tableB) => _adjacent[tableA * Tables.Count + tableB];

    public static SeatGraph Build(EventSetup setup, SeatGraphOptions? options = null)
    {
        options ??= SeatGraphOptions.Default;
        var tables = new List<TableNode>();
        var seats = new List<SeatNode>();
        var edges = new List<SeatEdge>();

        for (var f = 0; f < setup.Floors.Count; f++)
        {
            foreach (var table in setup.Floors[f].Tables)
            {
                var tableIndex = tables.Count;
                var layouts = TableGeometry.GetSeats(table).Where(s => !s.IsDisabled).ToList();
                var indexOnTable = new Dictionary<int, int>();
                foreach (var layout in layouts)
                {
                    indexOnTable[layout.Id.Index] = seats.Count;
                    seats.Add(new SeatNode(seats.Count, layout.Id, tableIndex, f, layout.Side, layout.PositionAlongSide, layout.Position,
                        Facing(table, layout)));
                }
                tables.Add(new TableNode(tableIndex, table, f, layouts.Select(l => indexOnTable[l.Id.Index]).ToList()));
                AddTableEdges(table, indexOnTable, edges);
            }
        }

        var adjacent = new bool[tables.Count * tables.Count];
        for (var a = 0; a < tables.Count; a++)
            for (var b = a + 1; b < tables.Count; b++)
                if (tables[a].FloorIndex == tables[b].FloorIndex && AreClose(tables[a].Table, tables[b].Table, options))
                {
                    adjacent[a * tables.Count + b] = adjacent[b * tables.Count + a] = true;
                    AddCrossTableEdges(tables[a], tables[b], seats, options, edges);
                }

        return new SeatGraph(setup.Floors, tables, seats, edges, adjacent);
    }

    /// <summary>The pairs of tables on a floor that count as adjacent. Cheap: it looks only at the table tops, not at seats.</summary>
    public static IEnumerable<(Table A, Table B)> AdjacentTablePairs(Floor floor, SeatGraphOptions? options = null)
    {
        options ??= SeatGraphOptions.Default;
        for (var a = 0; a < floor.Tables.Count; a++)
            for (var b = a + 1; b < floor.Tables.Count; b++)
                if (AreClose(floor.Tables[a], floor.Tables[b], options))
                    yield return (floor.Tables[a], floor.Tables[b]);
    }

    private static bool AreClose(Table a, Table b, SeatGraphOptions options) =>
        TableGeometry.Distance(a, b) <= options.AdjacentTableGap;

    /// <summary>Edges between seats of one table, by seat numbering (see <see cref="LongTableGeometry"/> and <see cref="RoundTableGeometry"/>).</summary>
    private static void AddTableEdges(Table table, Dictionary<int, int> seat, List<SeatEdge> edges)
    {
        void Add(int a, int b, SeatRelation relation)
        {
            if (seat.TryGetValue(a, out var x) && seat.TryGetValue(b, out var y))
                edges.Add(new SeatEdge(x, y, relation));
        }

        if (table.IsRound)
        {
            // Around a round table only the neighbours on either side are close; across is too far to talk.
            // The rest of the table counts through "same table".
            var count = table.RoundSeats;
            for (var i = 0; i < count && count > 1; i++)
                if (count > 2 || i == 0)
                    Add(i, (i + 1) % count, SeatRelation.NextTo);
            return;
        }

        var n = table.SeatsPerSide;

        for (var i = 0; i < n; i++)
        {
            if (i + 1 < n)
            {
                Add(i, i + 1, SeatRelation.NextTo);
                Add(n + i, n + i + 1, SeatRelation.NextTo);
                Add(i, n + i + 1, SeatRelation.Diagonal);
                Add(n + i, i + 1, SeatRelation.Diagonal);
            }
            Add(i, n + i, SeatRelation.Across);
        }

        // End seats are next to the corner seats on both sides; with both ends taken they face each other down the table, which is not close.
        if (table.EndSeats >= 1)
        {
            Add(2 * n, 0, SeatRelation.NextTo);
            Add(2 * n, n, SeatRelation.NextTo);
        }
        if (table.EndSeats >= 2)
        {
            Add(2 * n + 1, n - 1, SeatRelation.NextTo);
            Add(2 * n + 1, 2 * n - 1, SeatRelation.NextTo);
        }
    }

    private static void AddCrossTableEdges(TableNode a, TableNode b, List<SeatNode> seats, SeatGraphOptions options, List<SeatEdge> edges)
    {
        foreach (var i in a.Seats)
            foreach (var j in b.Seats)
            {
                var (s, t) = (seats[i], seats[j]);
                var dx = t.Position.X - s.Position.X;
                var dy = t.Position.Y - s.Position.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var facing = s.Facing.X * t.Facing.X + s.Facing.Y * t.Facing.Y;
                if (facing > 0.7 && distance <= options.NextToAcrossTablesDistance
                    && Math.Abs(dx * s.Facing.X + dy * s.Facing.Y) < 0.5 * distance)
                    edges.Add(new SeatEdge(i, j, SeatRelation.NextTo));
                else if (facing < -0.7 && distance <= options.BackToBackDistance
                    && dx * s.Facing.X + dy * s.Facing.Y < 0)
                    edges.Add(new SeatEdge(i, j, SeatRelation.BackToBack));
            }
    }

    /// <summary>
    /// The unit direction a seat faces: towards the table, straight across for side seats, along the table for end
    /// seats, and towards the centre at a round table.
    /// </summary>
    private static Point Facing(Table table, SeatLayout seat)
    {
        if (seat.Side == SeatSide.Round)
        {
            var (dx, dy) = (table.X - seat.Position.X, table.Y - seat.Position.Y);
            var length = Math.Sqrt(dx * dx + dy * dy);
            return length > 0 ? new Point(dx / length, dy / length) : new Point(0, 1);
        }
        var local = seat.Side switch
        {
            SeatSide.Top => new Point(0, 1),
            SeatSide.Bottom => new Point(0, -1),
            SeatSide.LeftEnd => new Point(1, 0),
            _ => new Point(-1, 0),
        };
        return local.Rotate(table.Rotation);
    }
}
