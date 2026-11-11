using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Core.Editing;

/// <summary>Copies of whole projects: for "Copy" on the projects page, and for importing a project that is already there.</summary>
public static class ProjectCopies
{
    /// <summary>
    /// A full copy (setup, plans, priorities) with a new ID and the given name, never exported. Groups, tables and
    /// images keep their IDs, which are only unique within a project.
    /// </summary>
    public static EventProject Copy(EventProject source, string name)
    {
        var copy = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(source)).Project
            ?? throw new InvalidOperationException("The project could not be copied.");
        copy.Id = Guid.NewGuid();
        copy.Name = name;
        copy.LastExportedAt = null;
        return copy;
    }

    /// <summary>"Name (copy)", or "Name (copy 2)" and so on when that is taken too (ignoring case).</summary>
    public static string CopyName(string name, IEnumerable<string> existingNames)
    {
        var taken = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = $"{name} (copy)";
        for (var n = 2; taken.Contains(candidate); n++)
            candidate = $"{name} (copy {n})";
        return candidate;
    }
}
