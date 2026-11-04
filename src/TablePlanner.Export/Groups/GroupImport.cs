using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Export.Groups;

public enum GroupImportMode
{
    /// <summary>Update groups with the same name, add the others, keep groups that are not in the file.</summary>
    Merge,

    /// <summary>Like <see cref="Merge"/>, but groups that are not in the file are removed.</summary>
    Replace,
}

public enum GroupChangeKind
{
    Added,
    Updated,
    Unchanged,
    Removed,
}

/// <summary>What happens to one group. <see cref="Details"/> lists what changed for an updated group, e.g. "People 4 → 6".</summary>
public sealed record GroupChange(string Name, GroupChangeKind Kind, int PeopleCount, IReadOnlyList<string> Details);

public sealed class GroupImportResult
{
    public GroupImportResult(IReadOnlyList<GroupChange> changes, IReadOnlyList<ImportMessage> errors, IReadOnlyList<ImportMessage> warnings)
    {
        Changes = changes;
        Errors = errors;
        Warnings = warnings;
    }

    public IReadOnlyList<GroupChange> Changes { get; }

    /// <summary>Problems in the file. Rows with errors are skipped.</summary>
    public IReadOnlyList<ImportMessage> Errors { get; }

    public IReadOnlyList<ImportMessage> Warnings { get; }

    public int Count(GroupChangeKind kind) => Changes.Count(c => c.Kind == kind);

    public bool HasChanges => Changes.Any(c => c.Kind != GroupChangeKind.Unchanged);
}

/// <summary>
/// Applies the groups of a file to a project. Groups are matched by name (ignoring case and surrounding
/// spaces) and keep their IDs, so pins, plans and preferences that refer to them stay valid.
/// For an existing group, only the columns present in the file are changed.
/// </summary>
public static class GroupImport
{
    /// <summary>What <see cref="Apply"/> would do, without changing the project.</summary>
    public static GroupImportResult Preview(EventProject project, GroupFile file, GroupImportMode mode)
    {
        var copy = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project)).Project
            ?? throw new InvalidOperationException("The project could not be copied.");
        return Apply(copy, file, mode);
    }

    public static GroupImportResult Apply(EventProject project, GroupFile file, GroupImportMode mode)
    {
        var setup = project.Setup;
        var warnings = file.Warnings.ToList();
        var before = setup.Groups.ToDictionary(g => g.Id, Snapshot.Of);
        var existingByName = ByName(setup.Groups);

        // Update or add the groups in the file.
        var imported = new List<(ImportedGroup Row, Group Group)>();
        foreach (var row in file.Groups)
        {
            if (existingByName.TryGetValue(row.Name, out var group))
                Update(group, row, file.Columns);
            else
            {
                group = new Group { Name = row.Name, PeopleCount = row.PeopleCount };
                Update(group, row, file.Columns);
                setup.Groups.Add(group);
            }
            imported.Add((row, group));
        }

        // Remove the groups that are not in the file. A group whose row has errors is kept as it is.
        var removed = new List<GroupChange>();
        if (mode == GroupImportMode.Replace)
        {
            var inFile = imported.Select(i => i.Group.Id).ToHashSet();
            var skipped = file.SkippedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var group in setup.Groups.Where(g => !inFile.Contains(g.Id)).ToList())
            {
                if (skipped.Contains(group.Name.Trim()))
                {
                    warnings.Add(new(null, $"'{group.Name}' is kept as it is, because its row has errors."));
                    continue;
                }
                SetupOperations.RemoveGroup(project, group.Id);
                removed.Add(new GroupChange(group.Name, GroupChangeKind.Removed, group.PeopleCount, []));
            }
        }

        // Preferences are resolved once all groups exist, so a group may prefer one further down the file.
        if (file.HasPreferences)
        {
            var allByName = ByName(setup.Groups);
            foreach (var (row, group) in imported)
            {
                var preferred = new List<Guid>();
                foreach (var name in row.Preferences)
                {
                    if (!allByName.TryGetValue(name, out var other))
                        warnings.Add(new(row.RowNumber, $"'{row.Name}' would like to sit with '{name}', but there is no group with that name. It is left out."));
                    else if (other.Id == group.Id)
                        warnings.Add(new(row.RowNumber, $"'{row.Name}' lists itself as a preference. It is left out."));
                    else if (!preferred.Contains(other.Id))
                        preferred.Add(other.Id);
                }
                group.PreferredGroupIds = preferred.Take(Group.MaxPreferences).ToList();
            }
        }

        var changes = imported.Select(i => Describe(i.Group, before.GetValueOrDefault(i.Group.Id))).Concat(removed).ToList();
        return new GroupImportResult(changes, file.Errors, warnings);
    }

    private static void Update(Group group, ImportedGroup row, IReadOnlySet<GroupColumn> columns)
    {
        group.Name = row.Name;
        group.PeopleCount = row.PeopleCount;
        if (columns.Contains(GroupColumn.ContactPerson))
            group.ContactPerson = row.ContactPerson;
        if (columns.Contains(GroupColumn.DietaryInfo))
            group.DietaryInfo = row.DietaryInfo;
        if (columns.Contains(GroupColumn.Colour))
            group.Colour = row.Colour;
        if (columns.Contains(GroupColumn.Notes))
            group.Notes = row.Notes;
    }

    private static GroupChange Describe(Group group, Snapshot? before)
    {
        if (before is null)
            return new GroupChange(group.Name, GroupChangeKind.Added, group.PeopleCount, []);

        var details = new List<string>();
        if (before.Name != group.Name)
            details.Add($"Name '{before.Name}' → '{group.Name}'");
        if (before.PeopleCount != group.PeopleCount)
            details.Add($"People {before.PeopleCount} → {group.PeopleCount}");
        if (before.ContactPerson != group.ContactPerson)
            details.Add("Contact person");
        if (before.DietaryInfo != group.DietaryInfo)
            details.Add("Dietary info");
        if (before.Colour != group.Colour)
            details.Add("Colour");
        if (before.Notes != group.Notes)
            details.Add("Notes");
        if (!before.Preferences.SequenceEqual(group.PreferredGroupIds))
            details.Add("Preferences");

        return new GroupChange(group.Name, details.Count == 0 ? GroupChangeKind.Unchanged : GroupChangeKind.Updated, group.PeopleCount, details);
    }

    /// <summary>Groups by trimmed name, ignoring case. If the project already has duplicate names, the first one wins.</summary>
    private static Dictionary<string, Group> ByName(IEnumerable<Group> groups)
    {
        var byName = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
            byName.TryAdd(group.Name.Trim(), group);
        return byName;
    }

    private sealed record Snapshot(string Name, int PeopleCount, string? ContactPerson, string? DietaryInfo, string? Colour, string? Notes, IReadOnlyList<Guid> Preferences)
    {
        public static Snapshot Of(Group g) =>
            new(g.Name, g.PeopleCount, g.ContactPerson, g.DietaryInfo, g.Colour, g.Notes, g.PreferredGroupIds.ToList());
    }
}
