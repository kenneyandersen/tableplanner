using TablePlanner.Core.Graph;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Solver;

/// <summary>
/// A setup compiled for solving and scoring: the seat graph, the groups as indices, preferences and pins.
/// A plan is an array with one entry per graph seat: the group index sitting there, or -1 when empty.
/// </summary>
public sealed class SeatingProblem
{
    private readonly Dictionary<Guid, int> _groupIndex;

    private SeatingProblem(EventSetup setup, SeatingPriorities priorities, SeatGraph graph)
    {
        Setup = setup;
        Priorities = priorities;
        Graph = graph;
        Groups = setup.Groups.Where(g => g.PeopleCount > 0).ToList();
        _groupIndex = new Dictionary<Guid, int>();
        for (var i = 0; i < Groups.Count; i++)
            _groupIndex.TryAdd(Groups[i].Id, i);

        Size = Groups.Select(g => g.PeopleCount).ToArray();

        // Parent groups (departments, teams with subteams) that have seated groups below them.
        var parents = setup.Groups.Where(g => setup.HasChildren(g.Id)).ToList();
        var members = parents.Select(p => setup.BottomGroupsOf(p).Select(b => IndexOfGroup(b.Id)).OfType<int>().ToArray()).ToList();
        ParentGroups = parents.Where((_, i) => members[i].Length > 0).ToList();
        ParentMembers = members.Where(m => m.Length > 0).ToArray();
        ParentLevel = ParentGroups.Select(setup.LevelOf).ToArray();
        _parentIndex = ParentGroups.Select((p, i) => (p.Id, i)).ToDictionary(x => x.Id, x => x.i);
        GroupParents = Groups.Select(g => setup.AncestorsOf(g).Select(a => _parentIndex.TryGetValue(a.Id, out var c) ? c : -1).Where(c => c >= 0).ToArray()).ToArray();

        // Preferences: between seated groups as before (by rank), and those to or from a parent group as "units".
        var unitPreferences = new List<UnitPreference>();
        int? UnitOf(Guid id) => _groupIndex.TryGetValue(id, out var g) ? g : _parentIndex.TryGetValue(id, out var c) ? Groups.Count + c : null;
        var cleaned = Groups.Select(g => g.Id).Concat(ParentGroups.Select(p => p.Id))
            .Select((id, unit) => setup.FindGroup(id)!.PreferredGroupIds
                .Select(UnitOf).OfType<int>().Where(u => u != unit).Distinct().Take(Group.MaxPreferences).ToArray())
            .ToArray();
        Preferences = new int[Groups.Count][];
        PreferenceRanks = new int[Groups.Count][];
        for (var g = 0; g < Groups.Count; g++)
        {
            var toGroups = cleaned[g].Select((u, rank) => (u, rank)).Where(x => x.u < Groups.Count).ToArray();
            Preferences[g] = toGroups.Select(x => x.u).ToArray();
            PreferenceRanks[g] = toGroups.Select(x => x.rank).ToArray();
        }
        IsMutual = Preferences.Select((prefs, g) => prefs.Select(h => Preferences[h].Contains(g)).ToArray()).ToArray();
        for (var unit = 0; unit < cleaned.Length; unit++)
            for (var rank = 0; rank < cleaned[unit].Length; rank++)
                if (cleaned[unit][rank] is var to && (unit >= Groups.Count || to >= Groups.Count))
                    unitPreferences.Add(new UnitPreference(unit, to, rank, cleaned[to].Contains(unit)));
        UnitPreferences = unitPreferences;

        // Groups worth seating next to each other: preferences, siblings, and the groups below a preferred parent.
        // Without parents this is just the preferred groups, as before.
        MoveTargets = Groups.Select((_, g) =>
        {
            var targets = new List<int>(Preferences[g]);
            foreach (var p in unitPreferences.Where(p => p.From == g && p.To >= Groups.Count))
                targets.AddRange(ParentMembers[p.To - Groups.Count]);
            if (GroupParents[g] is [var nearest, ..])
                targets.AddRange(ParentMembers[nearest]);
            return targets.Where(t => t != g).Distinct().ToArray();
        }).ToArray();

        FloorPin = new int[Groups.Count];
        TablePin = new int[Groups.Count];
        SeatPin = new int[Groups.Count][];
        Array.Fill(FloorPin, -1);
        Array.Fill(TablePin, -1);
        var floorIndex = graph.Floors.Select((f, i) => (f.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var tableIndex = graph.Tables.ToDictionary(t => t.Table.Id, t => t.Index);
        foreach (var pin in setup.Pins)
        {
            if (!_groupIndex.TryGetValue(pin.GroupId, out var g) || FloorPin[g] >= 0 || TablePin[g] >= 0 || SeatPin[g] is not null)
                continue;
            switch (pin.Scope)
            {
                case PinScope.Floor when pin.FloorId is { } f && floorIndex.TryGetValue(f, out var fi):
                    FloorPin[g] = fi;
                    break;
                case PinScope.Table when pin.TableId is { } t && tableIndex.TryGetValue(t, out var ti):
                    TablePin[g] = ti;
                    break;
                case PinScope.Seats:
                    SeatPin[g] = pin.Seats.Select(graph.IndexOf).OfType<int>().Distinct().ToArray();
                    break;
            }
        }
        // A floor or table pin on a parent group holds each of its groups that has no pin of its own.
        foreach (var pin in setup.Pins)
        {
            if (!_parentIndex.TryGetValue(pin.GroupId, out var c))
                continue;
            foreach (var g in ParentMembers[c].Where(g => FloorPin[g] < 0 && TablePin[g] < 0 && SeatPin[g] is null))
                if (pin is { Scope: PinScope.Floor, FloorId: { } f } && floorIndex.TryGetValue(f, out var fi))
                    FloorPin[g] = fi;
                else if (pin is { Scope: PinScope.Table, TableId: { } t } && tableIndex.TryGetValue(t, out var ti))
                    TablePin[g] = ti;
        }

        // The seats either side of each seat along the same side of its table, for the empty-gap rule.
        SideNeighbours = new (int, int)[graph.Seats.Count];
        Array.Fill(SideNeighbours, (-1, -1));
        foreach (var edge in graph.Edges.Where(e => e.Relation == SeatRelation.NextTo))
        {
            var (a, b) = (graph.Seats[edge.A], graph.Seats[edge.B]);
            if (a.TableIndex != b.TableIndex || a.Side != b.Side || a.Side is not (SeatSide.Top or SeatSide.Bottom or SeatSide.Round))
                continue;
            // Around a round table the "left" seat is the one before, counting clockwise (the last seat comes before seat 0).
            var count = graph.Tables[a.TableIndex].Table.RoundSeats;
            var aFirst = a.Side == SeatSide.Round
                ? (b.PositionAlongSide - a.PositionAlongSide + count) % count == 1
                : a.PositionAlongSide < b.PositionAlongSide;
            if (a.Side == SeatSide.Round && count <= 2)
                continue;
            var (left, right) = aFirst ? (a, b) : (b, a);
            SideNeighbours[left.Index] = (SideNeighbours[left.Index].Item1, right.Index);
            SideNeighbours[right.Index] = (left.Index, SideNeighbours[right.Index].Item2);
        }
    }

    public EventSetup Setup { get; }

    public SeatingPriorities Priorities { get; }

    public SeatGraph Graph { get; }

    /// <summary>The groups that can be seated (at least 1 person), in setup order. Plans refer to them by index.</summary>
    public IReadOnlyList<Group> Groups { get; }

    internal int[] Size { get; }

    /// <summary>
    /// Preferred seated groups per group, most wanted first, cleaned of unknown groups, itself and duplicates.
    /// Preferences for parent groups are in <see cref="UnitPreferences"/>.
    /// </summary>
    internal int[][] Preferences { get; }

    /// <summary>Per group, parallel to <see cref="Preferences"/>: the rank of each (0 = first choice), counting preferences for parent groups too.</summary>
    internal int[][] PreferenceRanks { get; }

    /// <summary>Parent groups with at least one seated group below them. Index c is "unit" Groups.Count + c in <see cref="UnitPreferences"/>.</summary>
    public IReadOnlyList<Group> ParentGroups { get; }

    /// <summary>Per parent group: the seated groups below it.</summary>
    internal int[][] ParentMembers { get; }

    /// <summary>Per parent group: its level (1 = top).</summary>
    internal int[] ParentLevel { get; }

    /// <summary>Per seated group: its parent groups (indices into <see cref="ParentGroups"/>), nearest first.</summary>
    internal int[][] GroupParents { get; }

    /// <summary>Preferences to or from a parent group. Units below Groups.Count are seated groups, the rest parent groups.</summary>
    internal IReadOnlyList<UnitPreference> UnitPreferences { get; }

    /// <summary>Per seated group: groups it would be good to seat next to (preferences, siblings, groups of a preferred parent).</summary>
    internal int[][] MoveTargets { get; }

    /// <summary>Per group and rank: whether the preferred group also prefers this group.</summary>
    internal bool[][] IsMutual { get; }

    /// <summary>Floor index a group is pinned to, or -1.</summary>
    internal int[] FloorPin { get; }

    /// <summary>Table index a group is pinned to, or -1.</summary>
    internal int[] TablePin { get; }

    /// <summary>Seat indices a group is pinned to, or null.</summary>
    internal int[]?[] SeatPin { get; }

    /// <summary>Per seat: the seat to its left and right on the same side of the table, or -1.</summary>
    internal (int Left, int Right)[] SideNeighbours { get; }

    public int SeatCount => Graph.Seats.Count;

    public int PeopleCount => Size.Sum();

    public static SeatingProblem Create(EventSetup setup, SeatingPriorities priorities, SeatGraphOptions? graphOptions = null) =>
        new(setup, priorities, SeatGraph.Build(setup, graphOptions));

    public int? IndexOfGroup(Guid groupId) => _groupIndex.TryGetValue(groupId, out var index) ? index : null;

    private readonly Dictionary<Guid, int> _parentIndex;

    /// <summary>A plan from an assignment. Seats that no longer exist and groups that are not in the problem are left out.</summary>
    public int[] ToPlan(SeatingAssignment assignment)
    {
        var plan = EmptyPlan();
        foreach (var seat in assignment.Seats)
            if (Graph.IndexOf(seat.Seat) is { } s && IndexOfGroup(seat.GroupId) is { } g)
                plan[s] = g;
        return plan;
    }

    public SeatingAssignment ToAssignment(int[] plan)
    {
        var assignment = new SeatingAssignment();
        for (var s = 0; s < plan.Length; s++)
            if (plan[s] >= 0)
                assignment.Seats.Add(new SeatAssignment(Graph.Seats[s].Id, Groups[plan[s]].Id));
        return assignment;
    }

    public int[] EmptyPlan()
    {
        var plan = new int[SeatCount];
        Array.Fill(plan, -1);
        return plan;
    }
}

/// <summary>A preference where one side is a parent group. A unit below the number of seated groups is a seated group, otherwise a parent group.</summary>
internal readonly record struct UnitPreference(int From, int To, int Rank, bool Mutual);
