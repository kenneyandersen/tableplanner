namespace TablePlanner.Solver;

/// <summary>
/// The "By levels" method's view of a group order: the groups as a tree (main groups, their subgroups, and so on, to
/// any depth), with the order made of an order at each level. Every parent group is then one unbroken run of the order,
/// so it gets one stretch of the room, with its subgroups inside it; a main group too large for an area or floor is
/// split between its subgroups by the fill rule (<see cref="SeatSequence"/>). The search changes one level at a time:
/// it swaps or moves whole main groups, or the subgroups within one parent group.
/// </summary>
internal sealed class LevelOrder
{
    private readonly SeatingProblem _problem;

    // Per seated group (null for pinned ones): its path from the top, as node ids. A parent group c is node c, a seated
    // group g is node ParentGroups.Count + g.
    private readonly int[]?[] _path;

    // The nodes with at least two children, with their depth (the root, i.e. the main groups, has depth -1).
    private readonly List<(int Node, int Depth)> _choices = [];

    private readonly bool _rootHasChoice;

    // Scratch for rebuilding a range of the order.
    private readonly List<(int Start, int Length)> _segments = [];
    private int[] _buffer = [];

    /// <summary>Of the changes, this share is made at the top level (the order of the main groups).</summary>
    internal const double TopLevelShare = 0.35;

    public LevelOrder(SeatingProblem problem, int[] free)
    {
        _problem = problem;
        var parents = problem.ParentGroups.Count;
        _path = new int[]?[problem.Groups.Count];
        foreach (var g in free)
            _path[g] = [.. problem.GroupParents[g].Reverse(), parents + g];

        var children = new Dictionary<(int Node, int Depth), HashSet<int>>();
        foreach (var g in free)
        {
            var path = _path[g]!;
            for (var d = -1; d < path.Length - 1; d++)
            {
                var key = (d < 0 ? -1 : path[d], d);
                if (!children.TryGetValue(key, out var set))
                    children[key] = set = [];
                set.Add(path[d + 1]);
            }
        }
        foreach (var ((node, depth), set) in children)
            if (set.Count >= 2)
            {
                if (depth < 0)
                    _rootHasChoice = true;
                else
                    _choices.Add((node, depth));
            }
    }

    /// <summary>The start order: at each level the largest first (by people), so every parent group is one run.</summary>
    public int[] Initial(int[] free)
    {
        var order = new List<int>(free.Length);
        void Visit(IEnumerable<int> groups, int depth)
        {
            var byChild = groups.GroupBy(g => _path[g]![depth])
                .OrderByDescending(c => c.Sum(g => _problem.Size[g]))
                .ThenBy(c => c.Key);
            foreach (var child in byChild)
            {
                if (child.Count() == 1 && _path[child.First()]!.Length == depth + 1)
                    order.Add(child.First());
                else
                    Visit(child, depth + 1);
            }
        }
        Visit(free, 0);
        return [.. order];
    }

    /// <summary>The node at <paramref name="depth"/> on the group's path, or -1 when its path is shorter.</summary>
    private int At(int g, int depth) => _path[g]! is var path && depth < path.Length ? path[depth] : -1;

    /// <summary>One change at one level: two blocks (main groups, or the subgroups of one parent group) swap places, or one moves.</summary>
    public void Mutate(int[] order, Random random)
    {
        int node, depth;
        if (_rootHasChoice && (_choices.Count == 0 || random.NextDouble() < TopLevelShare))
            (node, depth) = (-1, -1);
        else if (_choices.Count > 0)
            (node, depth) = _choices[random.Next(_choices.Count)];
        else
            return;

        // The node's run in the order (all of it for the top level), split into its children's runs.
        int from = 0, to = order.Length;
        if (depth >= 0)
        {
            from = Array.FindIndex(order, g => At(g, depth) == node);
            to = from;
            while (to < order.Length && At(order[to], depth) == node)
                to++;
        }
        _segments.Clear();
        for (var i = from; i < to;)
        {
            var child = At(order[i], depth + 1);
            var j = i + 1;
            while (j < to && At(order[j], depth + 1) == child)
                j++;
            _segments.Add((i, j - i));
            i = j;
        }
        if (_segments.Count < 2)
            return;

        var a = random.Next(_segments.Count);
        var b = random.Next(_segments.Count - 1);
        if (b >= a)
            b++;
        if (random.Next(2) == 0)
            (_segments[a], _segments[b]) = (_segments[b], _segments[a]);
        else
        {
            var moved = _segments[a];
            _segments.RemoveAt(a);
            _segments.Insert(b, moved);
        }

        if (_buffer.Length < to - from)
            _buffer = new int[order.Length];
        var k = 0;
        foreach (var (start, length) in _segments)
        {
            Array.Copy(order, start, _buffer, k, length);
            k += length;
        }
        Array.Copy(_buffer, 0, order, from, k);
    }
}
