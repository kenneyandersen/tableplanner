namespace TablePlanner.Solver;

/// <summary>
/// Builds a plan group by group along the seat order (<see cref="SeatSequence"/>), keeping the best
/// <c>width</c> partial plans at each step. A partial plan is rated by its score so far plus an optimistic estimate
/// for the groups still to come (each seated on its own, all their preferences met), minus a penalty when no
/// combination of the remaining groups can fill what is left of the current area of adjacent tables, so that
/// someone will have to be split into another part of the room.
/// </summary>
internal sealed class BeamSearch
{
    private readonly SeatingProblem _problem;
    private readonly SeatSequence _sequence;
    private readonly PlanEvaluator _evaluator;
    private readonly int[] _free;
    private readonly int[] _priority;
    private readonly double[] _ideal;
    private readonly double[][] _preferenceWeight;
    private readonly int[][] _linked;
    private readonly ulong[] _key;
    private readonly double _misfitPenalty;
    private readonly int[] _empty;
    private ulong[] _reach = [];

    /// <summary>Partial plans rated so far, for progress reports.</summary>
    public long Evaluations { get; private set; }

    public BeamSearch(SeatingProblem problem, SeatSequence sequence, PlanEvaluator evaluator, int[] free, Random random)
    {
        _problem = problem;
        _sequence = sequence;
        _evaluator = evaluator;
        _free = free;
        var groups = problem.Groups.Count;
        var p = problem.Priorities;

        // Which group of a size is tried first; groups of the same size without preferences are interchangeable.
        _priority = free.OrderByDescending(g => problem.Preferences[g].Length > 0).ThenBy(_ => random.Next()).ToArray();
        _key = Enumerable.Range(0, groups).Select(_ => (ulong)random.NextInt64()).ToArray();
        _misfitPenalty = p.TableSplitPenalty + p.DistantTableSplitPenalty + 3 * p.ApartPersonPenalty;

        _linked = Enumerable.Range(0, groups)
            .Select(g => problem.Preferences[g].Concat(Enumerable.Range(0, groups).Where(h => problem.Preferences[h].Contains(g))).Distinct().ToArray())
            .ToArray();

        // Per group and preference: what it adds when met at the same table (the rank weight, and the mutual bonus once per pair).
        _preferenceWeight = Enumerable.Range(0, groups)
            .Select(g => problem.Preferences[g].Select((h, r) => (r < p.PreferenceRankWeights.Count ? p.PreferenceRankWeights[r] : 0)
                + (problem.IsMutual[g][r] && g < h ? p.MutualPreferenceBonus : 0)).ToArray())
            .ToArray();

        // Per group: what it scores seated on its own at the start of the largest area.
        _empty = problem.EmptyPlan();
        sequence.Clear(_empty);
        var pending = new bool[groups];
        Array.Fill(pending, true);
        var baseScore = evaluator.ScorePartial(_empty, pending);
        var (start, largest) = (0, 0);
        for (var pos = 0; pos < sequence.Length; pos = sequence.AreaEnd(pos))
            if (sequence.AreaEnd(pos) - pos > largest)
                (start, largest) = (pos, sequence.AreaEnd(pos) - pos);
        _ideal = new double[groups];
        var plan = new int[_empty.Length];
        foreach (var g in free)
        {
            Array.Copy(_empty, plan, plan.Length);
            var pos = start;
            sequence.Place(plan, ref pos, g, sequence.Length - start);
            pending[g] = false;
            _ideal[g] = evaluator.ScorePartial(plan, pending) - baseScore;
            pending[g] = true;
        }
    }

    private sealed class Node(int[] plan, int pos, bool[] pending, int pendingPeople, int[] order, ulong key, int[] end)
    {
        public readonly int[] Plan = plan;
        public readonly int Pos = pos;
        public readonly bool[] Pending = pending;
        public readonly int PendingPeople = pendingPeople;
        public readonly int[] Order = order;
        public readonly ulong Key = key;

        /// <summary>Per group: the position in the seat order just after its seats; 0 when the beam has not seated it.</summary>
        public readonly int[] End = end;
    }

    private readonly record struct Child(int Parent, int Group, double Rating, ulong Key, int Pos);

    /// <summary>
    /// One beam search of the given width. Returns the best complete plan, or null when
    /// <paramref name="stop"/> said to stop before it finished.
    /// </summary>
    public async Task<int[]?> RunAsync(int width, Func<bool> stop, Func<Task> pause)
    {
        var groups = _problem.Groups.Count;
        var rootPending = new bool[groups];
        foreach (var g in _free)
            rootPending[g] = true;
        var beam = new List<Node> { new(_empty, 0, rootPending, _free.Sum(g => _problem.Size[g]), [], 0, new int[groups]) };
        var scratch = new int[_empty.Length];
        var children = new List<Child>();
        var candidates = new List<int>();
        var sizes = new HashSet<int>();

        for (var depth = 0; depth < _free.Length; depth++)
        {
            children.Clear();
            for (var b = 0; b < beam.Count; b++)
            {
                var node = beam[b];
                Candidates(node, candidates, sizes);
                foreach (var g in candidates)
                {
                    Array.Copy(node.Plan, scratch, scratch.Length);
                    var pos = node.Pos;
                    _sequence.Place(scratch, ref pos, g, node.PendingPeople);
                    node.Pending[g] = false;
                    node.End[g] = pos;
                    var rating = _evaluator.ScorePartial(scratch, node.Pending) + Estimate(node.Pending, node.End, pos, node.PendingPeople - _problem.Size[g]);
                    node.End[g] = 0;
                    node.Pending[g] = true;
                    children.Add(new Child(b, g, rating, node.Key ^ _key[g], pos));
                    Evaluations++;
                }
                if ((b & 7) == 7)
                {
                    await pause();
                    if (stop())
                        return null;
                }
            }

            // Keep the best, once per state (the same groups seated up to the same seat).
            children.Sort((x, y) => y.Rating.CompareTo(x.Rating));
            var next = new List<Node>(width);
            var seen = new HashSet<(ulong, int)>();
            foreach (var child in children)
            {
                if (next.Count == width)
                    break;
                if (!seen.Add((child.Key, child.Pos)))
                    continue;
                var parent = beam[child.Parent];
                var plan = (int[])parent.Plan.Clone();
                var pos = parent.Pos;
                _sequence.Place(plan, ref pos, child.Group, parent.PendingPeople);
                var pending = (bool[])parent.Pending.Clone();
                pending[child.Group] = false;
                var end = (int[])parent.End.Clone();
                end[child.Group] = pos;
                next.Add(new Node(plan, pos, pending, parent.PendingPeople - _problem.Size[child.Group], [.. parent.Order, child.Group], child.Key, end));
            }
            beam = next;
        }

        return beam.MaxBy(n => _evaluator.Score(n.Plan))!.Plan;
    }

    /// <summary>The groups to try next: one per size still to seat, and every group linked by a preference to the last one seated.</summary>
    private void Candidates(Node node, List<int> candidates, HashSet<int> sizes)
    {
        candidates.Clear();
        sizes.Clear();
        if (node.Order.Length > 0)
            foreach (var h in _linked[node.Order[^1]])
                if (node.Pending[h])
                    candidates.Add(h);
        foreach (var g in _priority)
            if (node.Pending[g] && sizes.Add(_problem.Size[g]) && !candidates.Contains(g))
                candidates.Add(g);

        // As in SeatSequence.Decode, a group may not run past the end of an area (or leave a small part at a table)
        // while a smaller group would fill the gap; so the plan is exactly what decoding the order gives, and the
        // order search can go on from it.
        if (node.Pos >= _sequence.Length)
            return;
        var smallest = int.MaxValue;
        foreach (var g in _free)
            if (node.Pending[g])
                smallest = Math.Min(smallest, _problem.Size[g]);
        candidates.RemoveAll(g => _sequence.GapLeft(node.Pos, _problem.Size[g]) is var gap and > 0 && smallest <= gap);
    }

    /// <summary>
    /// The optimistic rest: every group still to come seated on its own, less the misfit penalty, plus the preferences
    /// that can still be met. A preference between two groups still to come counts fully; between a seated group and
    /// one still to come only while the seated group is in the area being filled (so the other can still sit at the
    /// same or the next table), otherwise only as "same floor".
    /// </summary>
    private double Estimate(bool[] pending, int[] end, int pos, int pendingPeople)
    {
        var estimate = 0.0;
        var area = pos < _sequence.Length ? _sequence.AreaEnd(pos) : -1;
        bool Reachable(int g) => pending[g] || (end[g] > 0 && area >= 0 && _sequence.AreaEnd(end[g] - 1) == area);
        for (var g = 0; g < pending.Length; g++)
        {
            if (pending[g])
                estimate += _ideal[g];
            var preferences = _problem.Preferences[g];
            for (var r = 0; r < preferences.Length; r++)
            {
                var h = preferences[r];
                if (!pending[g] && !pending[h])
                    continue; // both seated: in the score already
                var closeness = Reachable(g) && Reachable(h) ? 1 : PlanEvaluator.SameFloorCloseness;
                estimate += _preferenceWeight[g][r] * closeness;
            }
        }

        if (pos < _sequence.Length && pendingPeople > 0)
        {
            var left = _sequence.AreaEnd(pos) - pos;
            var spare = Math.Max(0, _sequence.Length - pos - pendingPeople);
            if (!CanFill(pending, Math.Max(0, left - spare), left))
                estimate -= _misfitPenalty;
        }
        return estimate;
    }

    /// <summary>Whether some of the pending groups add up to between <paramref name="low"/> and <paramref name="high"/> people (a subset sum, as a bit set).</summary>
    private bool CanFill(bool[] pending, int low, int high)
    {
        if (low == 0)
            return true;
        var words = high / 64 + 1;
        if (_reach.Length < words)
            _reach = new ulong[words];
        Array.Clear(_reach, 0, words);
        _reach[0] = 1;
        for (var g = 0; g < pending.Length; g++)
        {
            if (!pending[g] || _problem.Size[g] > high)
                continue;
            var (wordShift, bitShift) = (_problem.Size[g] / 64, _problem.Size[g] % 64);
            for (var w = words - 1; w >= wordShift; w--)
            {
                var shifted = _reach[w - wordShift] << bitShift;
                if (bitShift > 0 && w - wordShift - 1 >= 0)
                    shifted |= _reach[w - wordShift - 1] >> (64 - bitShift);
                _reach[w] |= shifted;
            }
        }
        for (var s = low; s <= high; s++)
            if ((_reach[s / 64] >> (s % 64) & 1) != 0)
                return true;
        return false;
    }
}
