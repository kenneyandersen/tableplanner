namespace TablePlanner.Core.Model;

/// <summary>Identifies a seat: a table and the seat's index on it (see <see cref="Geometry.TableGeometry"/>).</summary>
public readonly record struct SeatId(Guid TableId, int Index);

public enum PinScope
{
    Floor,
    Table,
    Seats,
}

/// <summary>Fixes a group to a floor, a table or exact seats. The solver keeps pins as they are.</summary>
public sealed class Pin
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public PinScope Scope { get; set; }

    /// <summary>Set when <see cref="Scope"/> is <see cref="PinScope.Floor"/>.</summary>
    public Guid? FloorId { get; set; }

    /// <summary>Set when <see cref="Scope"/> is <see cref="PinScope.Table"/>.</summary>
    public Guid? TableId { get; set; }

    /// <summary>Set when <see cref="Scope"/> is <see cref="PinScope.Seats"/>.</summary>
    public List<SeatId> Seats { get; set; } = [];
}

public sealed record SeatAssignment(SeatId Seat, Guid GroupId);

/// <summary>The output: which group sits in which seat. Seats not listed are empty.</summary>
public sealed class SeatingAssignment
{
    public List<SeatAssignment> Seats { get; set; } = [];

    public Guid? GroupAt(SeatId seat) => Seats.FirstOrDefault(s => s.Seat == seat)?.GroupId;

    public IEnumerable<SeatId> SeatsOf(Guid groupId) => Seats.Where(s => s.GroupId == groupId).Select(s => s.Seat);

    public SeatingAssignment Clone() => new() { Seats = [.. Seats] };
}

/// <summary>A named snapshot of an assignment, kept so it survives re-running the solver.</summary>
public sealed class SavedAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset SavedAt { get; set; }

    /// <summary>The priorities that were in effect when this assignment was made.</summary>
    public SeatingPriorities Priorities { get; set; } = new();

    public SeatingAssignment Assignment { get; set; } = new();

    /// <summary>How good the plan was when it was saved. Null for plans saved without statistics.</summary>
    public PlanStats? Stats { get; set; }
}

/// <summary>Key figures of a plan, stored with a saved plan so the list can compare plans at a glance.</summary>
public sealed record PlanStats(double Score, int PeoplePlaced, int PeopleTotal, int SplitGroups, int FirstChoicesMet, int FirstChoicesWanted);
