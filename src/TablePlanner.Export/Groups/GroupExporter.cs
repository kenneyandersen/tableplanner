using System.Globalization;
using TablePlanner.Core.Model;
using TablePlanner.Export.Tabular;

namespace TablePlanner.Export.Groups;

/// <summary>Writes the groups as CSV or Excel, one row per group, with preferences as group names.</summary>
public static class GroupExporter
{
    public static byte[] ToCsv(EventSetup setup) => CsvFile.Write(ToRows(setup));

    public static byte[] ToXlsx(EventSetup setup) =>
        XlsxFile.Write("Groups", ToRows(setup), new HashSet<int> { Columns(setup).IndexOf(GroupColumn.People) });

    /// <summary>The columns written: all of <see cref="GroupColumns.All"/>, but the parent group only when the groups have levels.</summary>
    private static List<GroupColumn> Columns(EventSetup setup) =>
        GroupColumns.All.Where(c => c != GroupColumn.Parent || setup.HasLevels()).ToList();

    /// <summary>
    /// The header row followed by one row per group. With levels, in tree order (each parent group before its
    /// subgroups), parent groups with an empty number of people.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> ToRows(EventSetup setup)
    {
        var columns = Columns(setup);
        var rows = new List<IReadOnlyList<string>> { columns.Select(GroupColumns.Header).ToArray() };
        foreach (var (group, _) in setup.InTreeOrder())
            rows.Add(columns.Select(column => Cell(setup, group, column)).ToArray());
        return rows;
    }

    private static string Cell(EventSetup setup, Group group, GroupColumn column) => column switch
    {
        GroupColumn.Name => group.Name,
        GroupColumn.Parent => setup.ParentOf(group)?.Name ?? "",
        GroupColumn.People => setup.HasChildren(group.Id) ? "" : group.PeopleCount.ToString(CultureInfo.InvariantCulture),
        GroupColumn.ContactPerson => group.ContactPerson ?? "",
        GroupColumn.DietaryInfo => group.DietaryInfo ?? "",
        GroupColumn.Colour => group.Colour ?? "",
        GroupColumn.Notes => group.Notes ?? "",
        _ => PreferenceName(setup, group, GroupColumns.PreferenceRank(column)),
    };

    private static string PreferenceName(EventSetup setup, Group group, int rank) =>
        rank <= group.PreferredGroupIds.Count ? setup.FindGroup(group.PreferredGroupIds[rank - 1])?.Name ?? "" : "";
}
