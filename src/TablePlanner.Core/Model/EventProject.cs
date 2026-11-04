namespace TablePlanner.Core.Model;

/// <summary>One event: everything that is saved to and loaded from a project file.</summary>
public sealed class EventProject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset? LastExportedAt { get; set; }

    public EventSetup Setup { get; set; } = new();
    public SeatingPriorities Priorities { get; set; } = new();
    public SeatingAssignment CurrentAssignment { get; set; } = new();
    public List<SavedAssignment> SavedAssignments { get; set; } = [];
}

/// <summary>The input the planner edits: the venue and the groups to seat.</summary>
public sealed class EventSetup
{
    public List<Floor> Floors { get; set; } = [];
    public List<Group> Groups { get; set; } = [];
    public List<Pin> Pins { get; set; } = [];

    public IEnumerable<Table> AllTables => Floors.SelectMany(f => f.Tables);

    public Table? FindTable(Guid tableId) => AllTables.FirstOrDefault(t => t.Id == tableId);

    public Group? FindGroup(Guid groupId) => Groups.FirstOrDefault(g => g.Id == groupId);
}
