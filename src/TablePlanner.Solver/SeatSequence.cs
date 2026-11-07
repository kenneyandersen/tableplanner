using TablePlanner.Core.Geometry;
using TablePlanner.Core.Graph;

namespace TablePlanner.Solver;

/// <summary>
/// All free seats in one "snake" order, so that seats close in the order are close in the room:
/// floor by floor; within a floor, table to nearest table; within a table, column by column
/// (top seat, then the seat across), with the end seats first and last.
/// A plan is then a group order: <see cref="Decode"/> fills the seats in this order, group after group,
/// which keeps every group in a compact block. Tables are filled in alternating directions where that
/// keeps the joint between two tables close (back-to-back rows), so a group spanning two tables sits together.
/// </summary>
internal sealed class SeatSequence
{
    private readonly SeatingProblem _problem;
    private readonly int[] _fixed;
    private readonly int[] _seats;
    private readonly int[] _tableEnd;
    private readonly int[] _floorEnd;

    /// <param name="fixedPlan">Seats already taken (pins); they are left out of the order and copied into every decoded plan.</param>
    public SeatSequence(SeatingProblem problem, int[] fixedPlan)
    {
        _problem = problem;
        _fixed = fixedPlan;
        _minimumPart = problem.Priorities.SmallFragmentPenalty > 0 ? problem.Priorities.MinimumFragmentSize : 0;
        _seats = FillOrder(problem.Graph).Where(s => fixedPlan[s] < 0).ToArray();

        // Tables and floors are contiguous runs in the order; store where each run ends.
        _tableEnd = new int[_seats.Length];
        _floorEnd = new int[_seats.Length];
        _areaEnd = new int[_seats.Length];
        var seats = problem.Graph.Seats;
        for (var i = _seats.Length - 1; i >= 0; i--)
        {
            var last = i == _seats.Length - 1;
            var (here, next) = (seats[_seats[i]], last ? null : seats[_seats[i + 1]]);
            _tableEnd[i] = next is not null && next.TableIndex == here.TableIndex ? _tableEnd[i + 1] : i + 1;
            _floorEnd[i] = next is not null && next.FloorIndex == here.FloorIndex ? _floorEnd[i + 1] : i + 1;
            var sameArea = next is not null && next.FloorIndex == here.FloorIndex
                && (next.TableIndex == here.TableIndex || problem.Graph.AreAdjacent(next.TableIndex, here.TableIndex));
            _areaEnd[i] = sameArea ? _areaEnd[i + 1] : i + 1;
        }
    }

    // Where the run of seats at tables next to each other ends: the fill order jumps to a table that is not next to
    // the previous one there (another part of the room).
    private readonly int[] _areaEnd;

    // Decode's work buffer: which positions of the order have been seated (or skipped).
    private bool[] _done = [];

    private readonly int _minimumPart;

    public int Length => _seats.Length;

    /// <summary>The free seats in fill order.</summary>
    public IReadOnlyList<int> Seats => _seats;

    /// <summary>
    /// Fills the plan from the group order. A group that does not fit in the rest of the current table
    /// (or area of tables next to each other, or floor) starts at the next one, leaving the seats empty, as long as
    /// there are seats to spare and it then fits entirely. Groups that do not fit in the remaining seats at all are skipped.
    /// </summary>
    public void Decode(IReadOnlyList<int> order, int[] plan)
    {
        Array.Copy(_fixed, plan, plan.Length);
        if (_done.Length < order.Count)
            _done = new bool[order.Count];
        var length = _seats.Length;
        var remainingPeople = 0;
        foreach (var g in order)
            remainingPeople += _problem.Size[g];

        Array.Clear(_done, 0, order.Count);
        var pos = 0;
        var first = 0;
        while (true)
        {
            while (first < order.Count && _done[first])
                first++;
            if (first == order.Count)
                break;

            var i = first;
            var g = order[i];
            var size = _problem.Size[g];
            if (size > length - pos)
            {
                _done[i] = true;
                remainingPeople -= size;
                continue;
            }

            // A group that would run past the end of this area (into another part of the room): first seat a later
            // group that fits in what is left of the area, so that areas fill up with whole groups.
            // Likewise for a group that would run on to the next table with only a few of its people on one of the two.
            var areaLeft = _areaEnd[pos] - pos;
            var tableLeft = _tableEnd[pos] - pos;
            var minimumPart = Math.Min(_minimumPart, size);
            var gapLeft = size > areaLeft ? areaLeft
                : size > tableLeft && (tableLeft < minimumPart || size - tableLeft < minimumPart) ? tableLeft
                : 0;
            if (gapLeft > 0)
                for (var j = i + 1; j < order.Count; j++)
                    if (!_done[j] && _problem.Size[order[j]] <= gapLeft)
                    {
                        (i, g, size) = (j, order[j], _problem.Size[order[j]]);
                        break;
                    }
            _done[i] = true;

            var spare = length - pos - remainingPeople;
            var floorEnd = _floorEnd[pos];
            var areaEnd = _areaEnd[pos];
            var tableEnd = _tableEnd[pos];
            if (size > floorEnd - pos && floorEnd - pos <= spare && length - floorEnd >= size)
                pos = floorEnd;
            else if (size > areaEnd - pos && areaEnd - pos <= spare && areaEnd < length && size <= _areaEnd[areaEnd] - areaEnd)
                pos = areaEnd;
            else if (size > tableEnd - pos && tableEnd - pos <= spare && tableEnd < length && size <= _tableEnd[tableEnd] - tableEnd)
                pos = tableEnd;

            for (var k = 0; k < size; k++)
                plan[_seats[pos++]] = g;
            remainingPeople -= size;
        }
    }

    /// <summary>Every seat of the graph in fill order.</summary>
    internal static List<int> FillOrder(SeatGraph graph)
    {
        var order = new List<int>(graph.Seats.Count);
        var floors = Enumerable.Range(0, graph.Floors.Count).OrderBy(f => graph.Floors[f].Number).ThenBy(f => f);
        foreach (var floor in floors)
        {
            var tables = graph.Tables.Where(t => t.FloorIndex == floor && t.Seats.Count > 0).ToList();
            Point? lastSeat = null;
            foreach (var table in ChainTables(graph, tables))
            {
                // Of the four ways to walk the table (either end first, either side of a column first),
                // take the one that starts nearest to where the previous table ended.
                var variants = table.Table.IsRound
                    ? RoundTableOrders(graph, table)
                    : new[] { (false, false), (true, false), (false, true), (true, true) }
                        .Select(v => TableOrder(graph, table, v.Item1, v.Item2))
                        .ToList();
                if (variants.Count == 0)
                    continue;
                var seats = lastSeat is { } p
                    ? variants.MinBy(v => Distance(graph.Seats[v[0]].Position, p))!
                    : variants[0];
                order.AddRange(seats);
                lastSeat = graph.Seats[seats[^1]].Position;
            }
        }
        return order;
    }

    /// <summary>Starts at the top-left table and goes on to the nearest table not yet visited.</summary>
    private static IEnumerable<TableNode> ChainTables(SeatGraph graph, List<TableNode> tables)
    {
        var left = tables.ToList();
        var current = left.OrderBy(t => t.Table.Y + t.Table.X).ThenBy(t => t.Index).FirstOrDefault();
        while (current is not null)
        {
            yield return current;
            left.Remove(current);
            var from = current.Table;
            current = left
                .OrderBy(t => TableGeometry.Distance(from, t.Table))
                .ThenBy(t => Distance(new Point(t.Table.X, t.Table.Y), new Point(current.Table.X, current.Table.Y)))
                .ThenBy(t => t.Index)
                .FirstOrDefault();
        }
    }

    /// <summary>Left end seat, then columns left to right (top, then bottom), then the right end seat; or mirrored.</summary>
    private static List<int> TableOrder(SeatGraph graph, TableNode table, bool rightToLeft, bool bottomFirst)
    {
        var order = table.Seats
            .OrderBy(s => graph.Seats[s].Side switch
            {
                SeatSide.LeftEnd => -1,
                SeatSide.RightEnd => int.MaxValue,
                _ => graph.Seats[s].PositionAlongSide,
            })
            .ThenBy(s => (graph.Seats[s].Side == SeatSide.Top) == bottomFirst ? 1 : 0)
            .ToList();
        if (rightToLeft)
        {
            // Reverse the columns but keep the side order within each column.
            order = order
                .GroupBy(s => graph.Seats[s].Side switch { SeatSide.LeftEnd => -1, SeatSide.RightEnd => int.MaxValue, _ => graph.Seats[s].PositionAlongSide })
                .Reverse()
                .SelectMany(column => column)
                .ToList();
        }
        return order;
    }

    /// <summary>
    /// Around a round table, from any seat, clockwise or anticlockwise. Disabled seats are left out, so a start just
    /// after a gap keeps the gap at the end, where empty seats belong.
    /// </summary>
    private static List<List<int>> RoundTableOrders(SeatGraph graph, TableNode table)
    {
        var ring = table.Seats.OrderBy(s => graph.Seats[s].PositionAlongSide).ToList();
        var orders = new List<List<int>>(2 * ring.Count);
        for (var start = 0; start < ring.Count; start++)
        {
            orders.Add([.. ring.Skip(start), .. ring.Take(start)]);
            var reversed = ring.AsEnumerable().Reverse().ToList();
            orders.Add([.. reversed.Skip(start), .. reversed.Take(start)]);
        }
        return orders;
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
