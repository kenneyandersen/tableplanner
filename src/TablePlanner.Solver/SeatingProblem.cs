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

        Preferences = Groups
            .Select((g, i) => g.PreferredGroupIds
                .Select(id => _groupIndex.TryGetValue(id, out var j) ? j : -1)
                .Where(j => j >= 0 && j != i)
                .Distinct()
                .Take(Group.MaxPreferences)
                .ToArray())
            .ToArray();
        IsMutual = Preferences.Select((prefs, g) => prefs.Select(h => Preferences[h].Contains(g)).ToArray()).ToArray();

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

    /// <summary>Preferred group indices per group, most wanted first, cleaned of unknown groups, itself and duplicates.</summary>
    internal int[][] Preferences { get; }

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
