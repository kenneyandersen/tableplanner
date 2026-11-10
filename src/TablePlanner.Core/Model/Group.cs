namespace TablePlanner.Core.Model;

/// <summary>
/// A group of guests that is seated as a unit, or a parent group of other groups (a department of teams, see
/// <see cref="GroupTree"/>). Parent groups are kept together more loosely and have no people of their own.
/// </summary>
public sealed class Group
{
    public const int MaxPreferences = 5;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";

    /// <summary>The group this one belongs to, or null for a top-level group.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>People in this group; 0 for a parent group, whose people are in its subgroups.</summary>
    public int PeopleCount { get; set; } = 1;
    public string? Notes { get; set; }

    /// <summary>Fixed colour in the plan, as "#RRGGBB". Null = assigned automatically.</summary>
    public string? Colour { get; set; }

    public string? DietaryInfo { get; set; }
    public string? ContactPerson { get; set; }

    /// <summary>Groups this group would like to sit with, most wanted first. At most <see cref="MaxPreferences"/>.</summary>
    public List<Guid> PreferredGroupIds { get; set; } = [];
}
