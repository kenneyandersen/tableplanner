namespace TablePlanner.Core.Model;

/// <summary>A level (or separate room) of the venue. Coordinates on a floor are in metres.</summary>
public sealed class Floor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>1-based floor number, used for table numbering (floor 1 → 101, 102, …).</summary>
    public int Number { get; set; } = 1;

    public List<Table> Tables { get; set; } = [];
    public List<FloorElement> Elements { get; set; } = [];

    /// <summary>The next free table number on this floor, e.g. 103 after 101 and 102.</summary>
    public int NextTableNumber()
    {
        var first = Number * 100 + 1;
        return Tables.Count == 0 ? first : Math.Max(first, Tables.Max(t => t.Number) + 1);
    }
}
