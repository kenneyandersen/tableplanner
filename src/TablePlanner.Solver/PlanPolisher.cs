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
        }
        return swaps;
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
        _parts.Sort((a, b) => a.Length.CompareTo(b.Length));
    }

    private void Swap(int[] plan, int a, int b, int length)
    {
        var seats = sequence.Seats;
        for (var k = 0; k < length; k++)
            (plan[seats[a + k]], plan[seats[b + k]]) = (plan[seats[b + k]], plan[seats[a + k]]);
    }
}
