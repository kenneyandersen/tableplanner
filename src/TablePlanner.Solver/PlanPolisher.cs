namespace TablePlanner.Solver;

/// <summary>
/// Improves a finished plan with direct seat changes, which the group-order search cannot make: a small part of a
/// split group (the people at a table away from most of their group) swaps seats with an equally long run of seats
/// elsewhere, typically next to the rest of the group. The best swap for a part is kept if it raises the score, and
/// this repeats until no swap helps or time runs out.
/// </summary>
internal sealed class PlanPolisher(SeatingProblem problem, SeatSequence sequence, PlanEvaluator evaluator)
{
    /// <summary>Parts up to this many people are tried; larger parts are left to the order search.</summary>
    internal const int MaxPartSize = 16;

    /// <summary>Runs of free seats tried per pass: those furthest inside the room, and only if not already near its outside.</summary>
    internal const int MaxFreeRuns = 3;

    internal const double MaxFreeRunOutside = 0.7;

    private readonly List<(int Start, int Length)> _parts = [];

    /// <summary>Polishes <paramref name="plan"/> in place. Returns the number of swaps made.</summary>
    public async Task<int> PolishAsync(int[] plan, Func<bool> stop, Func<Task> pause)
    {
        var seats = sequence.Seats;
        var score = evaluator.Score(plan);
        var swaps = 0;
        var improved = true;
        while (improved && !stop())
        {
            improved = false;
            FindParts(plan);
            foreach (var (start, length) in _parts)
            {
                var (bestTarget, bestScore) = (-1, score + 1e-6);
                for (var target = 0; target + length <= seats.Count; target++)
                {
                    if (target < start + length && start < target + length)
                        continue;
                    if (plan[seats[target]] == plan[seats[start]] && plan[seats[target + length - 1]] == plan[seats[start]])
                        continue;
                    Swap(plan, start, target, length);
                    var s = evaluator.Score(plan);
                    Swap(plan, start, target, length);
                    if (s > bestScore)
                        (bestTarget, bestScore) = (target, s);
                }

                await pause();
                if (bestTarget >= 0)
                {
                    Swap(plan, start, bestTarget, length);
                    score = bestScore;
                    swaps++;
                    improved = true;
                    break; // the parts have changed; find them again
                }
                if (stop())
                    break;
            }
            if (!improved && !stop() && SlideToFreeEnds(plan, ref score))
            {
                swaps++;
                improved = true;
            }
        }
        return swaps;
    }

    /// <summary>
    /// An end seat of a long table that is taken while a seat of the same table is free: everyone between the two (in fill
    /// order) moves one seat along, so the free seat ends up at the end. Kept if the score improves. True when one was made.
    /// </summary>
    private bool SlideToFreeEnds(int[] plan, ref double score)
    {
        var seats = sequence.Seats;
        var graph = problem.Graph;
        for (var a = 0; a < seats.Count; a++)
        {
            var seat = graph.Seats[seats[a]];
            if (plan[seats[a]] < 0 || seat.Side is not (Core.Geometry.SeatSide.LeftEnd or Core.Geometry.SeatSide.RightEnd))
                continue;
            // The nearest free seat of the same table, either way along the order.
            foreach (var step in new[] { 1, -1 })
            {
                var b = a + step;
                while (b >= 0 && b < seats.Count && graph.Seats[seats[b]].TableIndex == seat.TableIndex && plan[seats[b]] >= 0)
                    b += step;
                if (b < 0 || b >= seats.Count || graph.Seats[seats[b]].TableIndex != seat.TableIndex)
                    continue;
                Slide(plan, a, b);
                var s = evaluator.Score(plan);
                if (s > score + 1e-6)
                {
                    score = s;
                    return true;
                }
                Slide(plan, b, a);
            }
        }
        return false;
    }

    /// <summary>Moves the free seat at position <paramref name="free"/> to <paramref name="to"/>, everyone between moving one seat towards <paramref name="free"/>.</summary>
    private void Slide(int[] plan, int to, int free)
    {
        var seats = sequence.Seats;
        var step = free > to ? 1 : -1;
        for (var k = free; k != to; k -= step)
            plan[seats[k]] = plan[seats[k - step]];
        plan[seats[to]] = -1;
    }

    /// <summary>
    /// The runs of seats (in fill order, within one table) held by a group that also sits elsewhere, other than the
    /// group's largest run; small ones first.
    /// </summary>
    private void FindParts(int[] plan)
    {
        _parts.Clear();
        var seats = sequence.Seats;
        var graph = problem.Graph;
        var runs = new List<(int Group, int Start, int Length)>();
        for (var i = 0; i < seats.Count;)
        {
            var g = plan[seats[i]];
            var j = i + 1;
            while (j < seats.Count && plan[seats[j]] == g && graph.Seats[seats[j]].TableIndex == graph.Seats[seats[i]].TableIndex)
                j++;
            if (g >= 0)
                runs.Add((g, i, j - i));
            i = j;
        }

        foreach (var group in runs.GroupBy(r => r.Group).Where(r => r.Count() > 1))
        {
            var largest = group.MaxBy(r => r.Length);
            foreach (var run in group)
                if (run != largest && run.Length <= MaxPartSize)
                    _parts.Add((run.Start, run.Length));
        }

        // Whole small groups on another floor than most of their parent group (e.g. one family of the guests downstairs
        // while the rest are upstairs): the order search rarely moves them back, as that shifts everyone after them.
        if (problem.ParentGroups.Count > 0)
        {
            var floors = graph.Floors.Count;
            var perFloor = new int[problem.ParentGroups.Count * floors];
            var groupFloor = new int[problem.Groups.Count];
            Array.Fill(groupFloor, -1);
            foreach (var run in runs)
            {
                var floor = graph.Seats[seats[run.Start]].FloorIndex;
                groupFloor[run.Group] = groupFloor[run.Group] == -1 || groupFloor[run.Group] == floor ? floor : -2;
                foreach (var c in problem.GroupParents[run.Group])
                    perFloor[c * floors + floor] += run.Length;
            }
            foreach (var run in runs)
            {
                if (run.Length > MaxPartSize || groupFloor[run.Group] < 0 || problem.GroupParents[run.Group] is not [.., var top])
                    continue;
                var floor = groupFloor[run.Group];
                var mainFloor = Enumerable.Range(0, floors).MaxBy(f => perFloor[top * floors + f]);
                if (floor != mainFloor && !_parts.Contains((run.Start, run.Length)))
                    _parts.Add((run.Start, run.Length));
            }
        }
        _parts.Sort((a, b) => a.Length.CompareTo(b.Length));

        // Then runs of free seats: swapped with people elsewhere, they can move to the outside of the room (the order
        // search decides where gaps go only through its fill order). Only the few furthest inside, as each is tried
        // against every place.
        var freeRuns = new List<(int Start, int Length, double Outside)>();
        for (var i = 0; i < seats.Count;)
        {
            var j = i;
            while (j < seats.Count && plan[seats[j]] < 0 && graph.Seats[seats[j]].TableIndex == graph.Seats[seats[i]].TableIndex)
                j++;
            if (j > i && j - i <= MaxPartSize)
            {
                var outside = Enumerable.Range(i, j - i).Average(k => evaluator.FreeSeatBonus(seats[k])) / PlanEvaluator.FreeAtEdgeWeight;
                if (outside < MaxFreeRunOutside)
                    freeRuns.Add((i, j - i, outside));
            }
            i = Math.Max(j, i + 1);
        }
        foreach (var run in freeRuns.OrderBy(r => r.Outside).Take(MaxFreeRuns))
            _parts.Add((run.Start, run.Length));
    }

    private void Swap(int[] plan, int a, int b, int length)
    {
        var seats = sequence.Seats;
        for (var k = 0; k < length; k++)
            (plan[seats[a + k]], plan[seats[b + k]]) = (plan[seats[b + k]], plan[seats[a + k]]);
    }
}
