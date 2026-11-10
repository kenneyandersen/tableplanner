using TablePlanner.Core.Model;

namespace TablePlanner.Export.Groups;

public enum GroupColumn
{
    Name,
    Parent,
    People,
    ContactPerson,
    DietaryInfo,
    Colour,
    Notes,
    Preference1,
    Preference2,
    Preference3,
    Preference4,
    Preference5,
}

/// <summary>The columns of the groups file, in the order they are written, and the header names accepted on import.</summary>
public static class GroupColumns
{
    public static IReadOnlyList<GroupColumn> All { get; } = Enum.GetValues<GroupColumn>();

    public static IReadOnlyList<GroupColumn> Preferences { get; } =
        [GroupColumn.Preference1, GroupColumn.Preference2, GroupColumn.Preference3, GroupColumn.Preference4, GroupColumn.Preference5];

    public static string Header(GroupColumn column) => column switch
    {
        GroupColumn.Name => "Name",
        GroupColumn.Parent => "Parent group",
        GroupColumn.People => "People",
        GroupColumn.ContactPerson => "Contact person",
        GroupColumn.DietaryInfo => "Dietary info",
        GroupColumn.Colour => "Colour",
        GroupColumn.Notes => "Notes",
        _ => $"Preference {PreferenceRank(column)}",
    };

    /// <summary>1 for <see cref="GroupColumn.Preference1"/> and so on.</summary>
    public static int PreferenceRank(GroupColumn column) => column - GroupColumn.Preference1 + 1;

    /// <summary>Matches a header ignoring case, spaces, '_', '-' and '.', and accepts a few common alternatives.</summary>
    public static GroupColumn? Match(string header) => Normalize(header) switch
    {
        "name" or "group" or "groupname" => GroupColumn.Name,
        "parent" or "parentgroup" or "partof" or "belongsto" or "ingroup" => GroupColumn.Parent,
        "people" or "peoplecount" or "count" or "size" or "persons" or "guests" or "numberofpeople" => GroupColumn.People,
        "contactperson" or "contact" => GroupColumn.ContactPerson,
        "dietaryinfo" or "dietary" or "diet" or "allergies" => GroupColumn.DietaryInfo,
        "colour" or "color" => GroupColumn.Colour,
        "notes" or "note" or "comments" or "comment" => GroupColumn.Notes,
        var h when PreferenceNumber(h) is { } rank => GroupColumn.Preference1 + rank - 1,
        _ => null,
    };

    private static int? PreferenceNumber(string normalized)
    {
        foreach (var prefix in new[] { "preference", "pref", "sitwith" })
            if (normalized.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(normalized[prefix.Length..], out var rank) && rank >= 1 && rank <= Group.MaxPreferences)
                return rank;
        return null;
    }

    private static string Normalize(string header) =>
        new string(header.Where(c => !char.IsWhiteSpace(c) && c is not '_' and not '-' and not '.').ToArray()).ToLowerInvariant();
}
