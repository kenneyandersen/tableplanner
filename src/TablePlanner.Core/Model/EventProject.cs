namespace TablePlanner.Core.Model;

/// <summary>One event: everything that is saved to and loaded from a project file.</summary>
public sealed class EventProject
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset? LastExportedAt { get; set; }

    /// <summary>Where the guest page is put online, for QR codes on the poster and cards. Null when not set.</summary>
    public string? GuestPageUrl { get; set; }

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

    /// <summary>
    /// What the groups at each level are called in this project, from the top (e.g. "Departments", "Teams"). Optional:
    /// a missing or blank name falls back to <see cref="GroupTree.DefaultLevelName"/>.
    /// </summary>
    public List<string> LevelNames { get; set; } = [];

    public IEnumerable<Table> AllTables => Floors.SelectMany(f => f.Tables);

    public Table? FindTable(Guid tableId) => AllTables.FirstOrDefault(t => t.Id == tableId);

    public Group? FindGroup(Guid groupId) => Groups.FirstOrDefault(g => g.Id == groupId);
}
