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
    /// Seats for moving a whole group to <paramref name="start"/>: free seats (or its own) on the same floor, first
    /// from the clicked seat onwards along the fill order, then the ones just before it. The group never ends up
    /// on another floor than the one clicked. Null when that floor does not have enough free seats.
    /// </summary>
    public static IReadOnlyList<SeatId>? SeatsForMove(SeatingProblem problem, SeatingAssignment assignment, Guid groupId, SeatId start)
    {
        if (problem.IndexOfGroup(groupId) is not { } g || problem.Graph.IndexOf(start) is not { } startSeat)
            return null;
        var plan = problem.ToPlan(assignment);
        var floor = problem.Graph.Seats[startSeat].FloorIndex;
        var order = SeatSequence.FillOrder(problem.Graph).Where(s => problem.Graph.Seats[s].FloorIndex == floor).ToList();
        var from = order.IndexOf(startSeat);
        var size = problem.Size[g];

        bool Usable(int s) => plan[s] < 0 || plan[s] == g;
        var after = order.Skip(from).Where(Usable);
        var before = order.Take(from).Reverse().Where(Usable);
        var seats = after.Concat(before).Take(size).Select(s => problem.Graph.Seats[s].Id).ToList();
        return seats.Count == size ? seats : null;
    }

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
