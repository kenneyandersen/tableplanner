using TablePlanner.Core.Graph;
using TablePlanner.Core.Model;

namespace TablePlanner.Solver;

/// <summary>
/// Scores a plan. Higher is better. The score adds closeness between people of the same group and
/// preference bonuses, and subtracts penalties for splits, small fragments, gaps, unplaced people and broken pins.
/// Not thread-safe: it reuses work buffers between calls.
/// </summary>
public sealed class PlanEvaluator
{
    /// <summary>Per person without a seat. Far above any other term, so the solver seats everyone it can.</summary>
    public const double UnplacedPersonPenalty = 10_000;

    /// <summary>Per person breaking a hard rule (a pin or "never split across floors"). Above leaving them unplaced.</summary>
    public const double HardRulePenalty = 100_000;

    // How close two groups are where they meet, as a fraction of the preference weight.
    internal const double SameTableCloseness = 1.0;
    internal const double AdjacentTableCloseness = 0.6;
    internal const double SameFloorCloseness = 0.2;

    private readonly SeatingProblem _problem;
    private readonly int _tableCount;
    private readonly int _floorCount;
    private readonly double[] _edgeWeight;
    private readonly int[] _edgeA;
    private readonly int[] _edgeB;
    private readonly int[] _seatTable;
    private readonly int[] _seatFloor;
    private readonly int[] _sideLeft;
    private readonly int[] _sideRight;
    private readonly double[] _rankWeight;

    // Work buffers, sized once.
    private readonly int[] _placed;
    private readonly int[] _tableCount2D;
    private readonly int[] _floorCount2D;
    private readonly List<int>[] _tablesOf;
    private readonly List<int>[] _floorsOf;
    private readonly int[] _component;
    private readonly Stack<int> _stack = new();

    private int[]? _previousTableCount2D;

    public PlanEvaluator(SeatingProblem problem)
    {
        _problem = problem;
        _tableCount = problem.Graph.Tables.Count;
        _floorCount = problem.Graph.Floors.Count;
        var p = problem.Priorities;
        _edgeWeight = problem.Graph.Edges.Select(e => e.Relation switch
        {
            SeatRelation.NextTo => p.NextToWeight,
            SeatRelation.Across => p.AcrossWeight,
            SeatRelation.Diagonal => p.DiagonalWeight,
            _ => p.BackToBackWeight,
        }).ToArray();
        _edgeA = problem.Graph.Edges.Select(e => e.A).ToArray();
        _edgeB = problem.Graph.Edges.Select(e => e.B).ToArray();
        _seatTable = problem.Graph.Seats.Select(s => s.TableIndex).ToArray();
        _seatFloor = problem.Graph.Seats.Select(s => s.FloorIndex).ToArray();
        _sideLeft = problem.SideNeighbours.Select(n => n.Left).ToArray();
        _sideRight = problem.SideNeighbours.Select(n => n.Right).ToArray();
        _rankWeight = Enumerable.Range(0, Group.MaxPreferences)
            .Select(r => r < p.PreferenceRankWeights.Count ? p.PreferenceRankWeights[r] : 0).ToArray();

        var groups = problem.Groups.Count;
        _placed = new int[groups];
        _tableCount2D = new int[groups * _tableCount];
        _floorCount2D = new int[groups * _floorCount];
        _tablesOf = Enumerable.Range(0, groups).Select(_ => new List<int>()).ToArray();
        _floorsOf = Enumerable.Range(0, groups).Select(_ => new List<int>()).ToArray();
        _component = new int[_tableCount];
    }

    /// <summary>
    /// The plan that re-optimizing should stay close to: each person at a different table than in this plan costs
    /// <see cref="SeatingPriorities.MovedPersonPenalty"/>. Null turns this off.
    /// </summary>
    public void SetPreviousPlan(int[]? plan)
    {
        if (plan is null)
        {
            _previousTableCount2D = null;
            return;
        }
        _previousTableCount2D = new int[_problem.Groups.Count * _tableCount];
        for (var s = 0; s < plan.Length; s++)
            if (plan[s] >= 0)
                _previousTableCount2D[plan[s] * _tableCount + _problem.Graph.Seats[s].TableIndex]++;
    }

    public double Score(int[] plan)
    {
        Count(plan);
        return SeatTerms(plan) + GroupTerms(null) + PreferenceTerms(null);
    }

    public PlanSummary Summarize(int[] plan)
    {
        Count(plan);
        var groups = new GroupPlacementBuilder[_problem.Groups.Count];
        for (var g = 0; g < groups.Length; g++)
            groups[g] = new GroupPlacementBuilder();
        var score = SeatTerms(plan) + GroupTerms(groups) + PreferenceTerms(groups);
        return PlanSummary.Create(_problem, score, groups.Select((b, g) => b.Build(_problem, g, _placed[g], _tablesOf[g], _floorsOf[g])).ToList());
    }

    private void Count(int[] plan)
    {
        Array.Clear(_placed);
        for (var g = 0; g < _tablesOf.Length; g++)
        {
            foreach (var t in _tablesOf[g])
                _tableCount2D[g * _tableCount + t] = 0;
            foreach (var f in _floorsOf[g])
                _floorCount2D[g * _floorCount + f] = 0;
            _tablesOf[g].Clear();
            _floorsOf[g].Clear();
        }

        for (var s = 0; s < plan.Length; s++)
        {
            var g = plan[s];
            if (g < 0)
                continue;
            _placed[g]++;
            var t = _seatTable[s];
            if (_tableCount2D[g * _tableCount + t]++ == 0)
                _tablesOf[g].Add(t);
            var f = _seatFloor[s];
            if (_floorCount2D[g * _floorCount + f]++ == 0)
                _floorsOf[g].Add(f);
        }
    }

    /// <summary>Closeness of neighbouring seats of the same group, and empty gaps between occupied seats.</summary>
    private double SeatTerms(int[] plan)
    {
        var score = 0.0;
        for (var e = 0; e < _edgeA.Length; e++)
        {
            var g = plan[_edgeA[e]];
            if (g >= 0 && g == plan[_edgeB[e]])
                score += _edgeWeight[e];
        }

        var gap = _problem.Priorities.EmptySeatGapPenalty;
        if (gap != 0)
            for (var s = 0; s < plan.Length; s++)
            {
                if (plan[s] >= 0)
                    continue;
                var (left, right) = (_sideLeft[s], _sideRight[s]);
                if (left >= 0 && right >= 0 && plan[left] >= 0 && plan[right] >= 0)
                    score -= gap;
            }
        return score;
    }

    private double GroupTerms(GroupPlacementBuilder[]? details)
    {
        var p = _problem.Priorities;
        var graph = _problem.Graph;
        var score = 0.0;

        for (var g = 0; g < _placed.Length; g++)
        {
            var size = _problem.Size[g];
            var placed = _placed[g];
            score -= (size - placed) * UnplacedPersonPenalty;
            if (placed == 0)
                continue;

            var tables = _tablesOf[g];
            var minimumPart = Math.Min(p.MinimumFragmentSize, size);
            var smallParts = 0;
            for (var i = 0; i < tables.Count; i++)
            {
                var c = _tableCount2D[g * _tableCount + tables[i]];
                score += p.SameTableWeight * c * (c - 1) / 2;
                if (tables.Count > 1 && c < minimumPart)
                    smallParts++;
                for (var j = i + 1; j < tables.Count; j++)
                    if (graph.AreAdjacent(tables[i], tables[j]))
                        score += p.AdjacentTableWeight * c * _tableCount2D[g * _tableCount + tables[j]];
            }
            var parts = Components(tables);
            score -= (tables.Count - 1) * p.TableSplitPenalty;
            score -= (parts - 1) * p.DistantTableSplitPenalty;
            score -= smallParts * p.SmallFragmentPenalty;

            var floors = _floorsOf[g].Count;
            score -= (floors - 1) * p.FloorSplitPenalty;
            var hardBroken = 0;
            if (p.NeverSplitAcrossFloors && floors > 1)
                hardBroken += placed - _floorsOf[g].Max(f => _floorCount2D[g * _floorCount + f]);
            if (_problem.FloorPin[g] is var fp and >= 0)
                hardBroken += placed - _floorCount2D[g * _floorCount + fp];
            if (_problem.TablePin[g] is var tp and >= 0)
                hardBroken += placed - _tableCount2D[g * _tableCount + tp];
            score -= hardBroken * HardRulePenalty;

            var moved = 0;
            if (_previousTableCount2D is { } previous)
                for (var t = 0; t < _tableCount; t++)
                    moved += Math.Max(0, previous[g * _tableCount + t] - _tableCount2D[g * _tableCount + t]);
            score -= moved * p.MovedPersonPenalty;

            if (details is not null)
            {
                details[g].DistantParts = parts - 1;
                details[g].SmallParts = smallParts;
                details[g].BreaksHardRule = hardBroken > 0;
            }
        }
        return score;
    }

    private double PreferenceTerms(GroupPlacementBuilder[]? details)
    {
        var mutualBonus = _problem.Priorities.MutualPreferenceBonus;
        var score = 0.0;
        for (var g = 0; g < _placed.Length; g++)
        {
            var preferences = _problem.Preferences[g];
            for (var r = 0; r < preferences.Length; r++)
            {
                var h = preferences[r];
                var closeness = Closeness(g, h, out var level);
                score += _rankWeight[r] * closeness;
                if (_problem.IsMutual[g][r] && g < h)
                    score += mutualBonus * closeness;
                details?[g].Preferences.Add(new PreferenceOutcome(_problem.Groups[h].Id, r + 1, level));
            }
        }
        return score;
    }

    private double Closeness(int g, int h, out PreferenceCloseness level)
    {
        if (_placed[g] == 0 || _placed[h] == 0)
        {
            level = PreferenceCloseness.NotPlaced;
            return 0;
        }
        foreach (var t in _tablesOf[g])
            if (_tableCount2D[h * _tableCount + t] > 0)
            {
                level = PreferenceCloseness.SameTable;
                return SameTableCloseness;
            }
        foreach (var t in _tablesOf[g])
            foreach (var u in _tablesOf[h])
                if (_problem.Graph.AreAdjacent(t, u))
                {
                    level = PreferenceCloseness.AdjacentTable;
                    return AdjacentTableCloseness;
                }
        foreach (var f in _floorsOf[g])
            if (_floorCount2D[h * _floorCount + f] > 0)
            {
                level = PreferenceCloseness.SameFloor;
                return SameFloorCloseness;
            }
        level = PreferenceCloseness.Apart;
        return 0;
    }

    /// <summary>The number of connected parts among a group's tables, where adjacent tables connect.</summary>
    private int Components(List<int> tables)
    {
        if (tables.Count <= 1)
            return tables.Count;
        for (var i = 0; i < tables.Count; i++)
            _component[i] = -1;
        var parts = 0;
        var stack = _stack;
        for (var i = 0; i < tables.Count; i++)
        {
            if (_component[i] >= 0)
                continue;
            parts++;
            _component[i] = parts;
            stack.Push(i);
            while (stack.Count > 0)
            {
                var x = stack.Pop();
                for (var j = 0; j < tables.Count; j++)
                    if (_component[j] < 0 && _problem.Graph.AreAdjacent(tables[x], tables[j]))
                    {
                        _component[j] = parts;
                        stack.Push(j);
                    }
            }
        }
        return parts;
    }

    private sealed class GroupPlacementBuilder
    {
        public int DistantParts;
        public int SmallParts;
        public bool BreaksHardRule;
        public readonly List<PreferenceOutcome> Preferences = [];

        public GroupPlacement Build(SeatingProblem problem, int g, int placed, List<int> tables, List<int> floors)
        {
            var group = problem.Groups[g];
            var tableNumbers = tables.Select(t => problem.Graph.Tables[t].Table.Number).Order().ToList();
            return new GroupPlacement(group.Id, group.Name, group.PeopleCount, placed, tableNumbers, floors.Count,
                DistantParts > 0, SmallParts, BreaksHardRule, Preferences.ToList());
        }
    }
}
