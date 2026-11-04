namespace TablePlanner.Core.Model;

/// <summary>A group of guests that is seated as a unit.</summary>
public sealed class Group
{
    public const int MaxPreferences = 5;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public int PeopleCount { get; set; } = 1;
    public string? Notes { get; set; }

    /// <summary>Fixed colour in the plan, as "#RRGGBB". Null = assigned automatically.</summary>
    public string? Colour { get; set; }

    public string? DietaryInfo { get; set; }
    public string? ContactPerson { get; set; }

    /// <summary>Groups this group would like to sit with, most wanted first. At most <see cref="MaxPreferences"/>.</summary>
    public List<Guid> PreferredGroupIds { get; set; } = [];
}
