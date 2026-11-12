using TablePlanner.Core.Model;

namespace TablePlanner.Solver;

/// <summary>
/// Works out which seats a group gets when it is moved or swapped by hand. Seats are taken along the solver's
/// fill order (<see cref="SeatSequence"/>), so a moved group gets a compact block just like in a solved plan.
/// The result is applied with <see cref="Core.Editing.PlanOperations.SeatGroups"/>.
/// </summary>
public static class PlanEditing
{
    /// <summary>
    /// Seats for moving a whole group to <paramref name="start"/>: the free seats (or its own) nearest the dropped seat,
    /// those at the same table first, then the nearest at other tables on the same floor; so a small group stays at the
    /// table it is dropped on when it fits there, around the drop point. The group never ends up on another floor than
    /// the one clicked. Null when that floor does not have enough free seats.
    /// </summary>
    public static IReadOnlyList<SeatId>? SeatsForMove(SeatingProblem problem, SeatingAssignment assignment, Guid groupId, SeatId start)
    {
        if (problem.IndexOfGroup(groupId) is not { } g || problem.Graph.IndexOf(start) is not { } startSeat)
            return null;
        var plan = problem.ToPlan(assignment);
        var seats = problem.Graph.Seats;
        var origin = seats[startSeat];
        var size = problem.Size[g];

        bool Usable(int s) => seats[s].FloorIndex == origin.FloorIndex && (plan[s] < 0 || plan[s] == g);
        static double Distance(Core.Geometry.Point a, Core.Geometry.Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        if (!Usable(startSeat) && plan[startSeat] >= 0)
            return null;

        // Grows from the dropped seat to its neighbours (next to, across, diagonal, back to back), cheapest first, never
        // through another group's seats; going on to another table costs extra, so the group stays at the dropped table
        // while it fits there. If that runs out, it goes on from the nearest usable seat left (same table first).
        var neighbours = new List<int>[seats.Count];
        foreach (var e in problem.Graph.Edges)
        {
            (neighbours[e.A] ??= []).Add(e.B);
            (neighbours[e.B] ??= []).Add(e.A);
        }
        var cost = new Dictionary<int, double> { [startSeat] = 0 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(startSeat, 0);
        var chosen = new List<int>(size);
        var taken = new HashSet<int>();
        while (chosen.Count < size)
        {
            if (queue.Count == 0)
            {
                var next = seats.Where(s => !taken.Contains(s.Index) && Usable(s.Index))
                    .OrderBy(s => s.TableIndex != origin.TableIndex).ThenBy(s => Distance(s.Position, origin.Position)).ThenBy(s => s.Index)
                    .Select(s => (int?)s.Index).FirstOrDefault();
                if (next is not { } seed)
                    break;
                cost[seed] = 1000 + Distance(seats[seed].Position, origin.Position);
                queue.Enqueue(seed, cost[seed]);
            }
            var s = queue.Dequeue();
            if (!taken.Add(s))
                continue;
            chosen.Add(s);
            foreach (var n in neighbours[s] ?? [])
            {
                if (taken.Contains(n) || !Usable(n))
                    continue;
                var c = cost[s] + Distance(seats[s].Position, seats[n].Position) + (seats[n].TableIndex != seats[s].TableIndex ? OtherTableCost : 0);
                if (!cost.TryGetValue(n, out var known) || c < known)
                {
                    cost[n] = c;
                    queue.Enqueue(n, c);
                }
            }
        }
        return chosen.Count == size ? chosen.Select(s => seats[s].Id).ToList() : null;
    }

    /// <summary>Extra cost (as metres) for a moved group going on to another table, so it stays at the dropped one while it fits.</summary>
    private const double OtherTableCost = 10;

    /// <summary>
    /// Seats for swapping two groups: <paramref name="a"/> starts where <paramref name="b"/> sat, and <paramref name="b"/>
    /// takes the rest of the two groups' seats. Groups of the same size simply trade places; otherwise the larger
    /// one ends up partly in each place.
    /// </summary>
    public static IReadOnlyDictionary<Guid, IReadOnlyList<SeatId>> SeatsForSwap(SeatingProblem problem, SeatingAssignment assignment, Guid a, Guid b)
    {
        var order = SeatSequence.FillOrder(problem.Graph);
        var position = new Dictionary<SeatId, int>();
        for (var i = 0; i < order.Count; i++)
            position[problem.Graph.Seats[order[i]].Id] = i;
        List<SeatId> InOrder(Guid group) => assignment.SeatsOf(group).Where(position.ContainsKey).OrderBy(s => position[s]).ToList();

        var pool = InOrder(b).Concat(InOrder(a)).ToList();
        var sizeA = problem.IndexOfGroup(a) is { } ga ? problem.Size[ga] : 0;
        var takeA = Math.Min(sizeA, pool.Count);
        return new Dictionary<Guid, IReadOnlyList<SeatId>>
        {
            [a] = pool.Take(takeA).ToList(),
            [b] = pool.Skip(takeA).ToList(),
        };
    }
}
