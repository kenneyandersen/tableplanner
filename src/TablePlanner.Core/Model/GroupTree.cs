namespace TablePlanner.Core.Model;

/// <summary>
/// Groups can have a parent group (<see cref="Group.ParentId"/>), up to <see cref="MaxLevels"/> levels: for example a
/// department, its teams and their subteams. Only groups without subgroups ("bottom groups") have people and are
/// seated; a parent group has no people of its own (<see cref="Group.PeopleCount"/> 0) and its size is the sum of
/// its bottom groups. A flat list of groups is the case where no group has a parent.
/// </summary>
public static class GroupTree
{
    public const int MaxLevels = 3;

    /// <summary>Joins the names in a group's path, e.g. "Sales › Team North".</summary>
    public const string PathSeparator = " › ";

    public static Group? ParentOf(this EventSetup setup, Group group) =>
        group.ParentId is { } id ? setup.FindGroup(id) : null;

    public static IEnumerable<Group> ChildrenOf(this EventSetup setup, Guid groupId) =>
        setup.Groups.Where(g => g.ParentId == groupId);

    public static bool HasChildren(this EventSetup setup, Guid groupId) =>
        setup.Groups.Any(g => g.ParentId == groupId);

    /// <summary>Whether any group has a parent: the setup uses levels.</summary>
    public static bool HasLevels(this EventSetup setup) => setup.Groups.Any(g => g.ParentId is not null);

    /// <summary>The name of a level when the project has none: "Main groups", "Subgroups", "Sub-subgroups".</summary>
    public static string DefaultLevelName(int level) => level switch
    {
        1 => "Main groups",
        2 => "Subgroups",
        _ => string.Concat(Enumerable.Repeat("Sub-", level - 2)) + "subgroups",
    };

    /// <summary>What the groups at <paramref name="level"/> (1 = top) are called: the project's name for it, or the default.</summary>
    public static string LevelName(this EventSetup setup, int level) =>
        level >= 1 && level <= setup.LevelNames.Count && !string.IsNullOrWhiteSpace(setup.LevelNames[level - 1])
            ? setup.LevelNames[level - 1].Trim()
            : DefaultLevelName(level);

    /// <summary>How many levels the groups have (1 without parent groups).</summary>
    public static int LevelCount(this EventSetup setup) => setup.Groups.Count == 0 ? 1 : setup.Groups.Max(setup.LevelOf);

    /// <summary>The group's parent, its parent and so on, nearest first. Stops at a loop or a missing parent.</summary>
    public static List<Group> AncestorsOf(this EventSetup setup, Group group)
    {
        var ancestors = new List<Group>();
        var seen = new HashSet<Guid> { group.Id };
        for (var parent = setup.ParentOf(group); parent is not null && seen.Add(parent.Id); parent = setup.ParentOf(parent))
            ancestors.Add(parent);
        return ancestors;
    }

    /// <summary>1 for a top-level group, 2 for its subgroups, and so on.</summary>
    public static int LevelOf(this EventSetup setup, Group group) => setup.AncestorsOf(group).Count + 1;

    /// <summary>The group at the top of the group's path (the group itself when it has no parent).</summary>
    public static Group TopOf(this EventSetup setup, Group group) => setup.AncestorsOf(group) is { Count: > 0 } a ? a[^1] : group;

    /// <summary>The group's ancestor at <paramref name="level"/> (1 = top), or the group itself when it is at that level or above.</summary>
    public static Group AtLevel(this EventSetup setup, Group group, int level)
    {
        var path = setup.AncestorsOf(group);
        path.Reverse();
        path.Add(group);
        return path[Math.Clamp(level, 1, path.Count) - 1];
    }

    /// <summary>The names from the top group down to this one, e.g. "Sales › Team North".</summary>
    public static string PathOf(this EventSetup setup, Group group) =>
        string.Join(PathSeparator, setup.AncestorsOf(group).AsEnumerable().Reverse().Select(g => g.Name).Append(group.Name));

    /// <summary>All groups below this one (children, their children, …), not the group itself.</summary>
    public static List<Group> DescendantsOf(this EventSetup setup, Guid groupId)
    {
        var result = new List<Group>();
        var seen = new HashSet<Guid> { groupId };
        var queue = new Queue<Guid>([groupId]);
        while (queue.Count > 0)
            foreach (var child in setup.ChildrenOf(queue.Dequeue()))
                if (seen.Add(child.Id))
                {
                    result.Add(child);
                    queue.Enqueue(child.Id);
                }
        return result;
    }

    /// <summary>The bottom groups (those that are seated) at or below this group: the group itself when it has no subgroups.</summary>
    public static List<Group> BottomGroupsOf(this EventSetup setup, Group group)
    {
        var below = setup.DescendantsOf(group.Id);
        return below.Count == 0 ? [group] : below.Where(g => !setup.HasChildren(g.Id)).ToList();
    }

    /// <summary>The people in a group: its own for a bottom group, the sum of its bottom groups for a parent group.</summary>
    public static int PeopleIn(this EventSetup setup, Group group) =>
        setup.BottomGroupsOf(group).Sum(g => Math.Max(0, g.PeopleCount));

    /// <summary>How many levels the group and its subgroups span (1 for a bottom group).</summary>
    public static int HeightOf(this EventSetup setup, Guid groupId)
    {
        var children = setup.ChildrenOf(groupId).ToList();
        return children.Count == 0 ? 1 : 1 + children.Max(c => setup.HeightOf(c.Id));
    }

    /// <summary>
    /// The groups in tree order: each top-level group followed by its subgroups (depth first), siblings in list order.
    /// With the level of each (1 = top). Groups in a loop or below a missing parent come last, at level 1.
    /// </summary>
    public static List<(Group Group, int Level)> InTreeOrder(this EventSetup setup)
    {
        var result = new List<(Group, int)>();
        var placed = new HashSet<Guid>();
        void Visit(Group group, int level)
        {
            if (!placed.Add(group.Id))
                return;
            result.Add((group, level));
            foreach (var child in setup.ChildrenOf(group.Id))
                Visit(child, level + 1);
        }
        foreach (var top in setup.Groups.Where(g => g.ParentId is null || setup.FindGroup(g.ParentId.Value) is null))
            Visit(top, 1);
        foreach (var rest in setup.Groups.Where(g => !placed.Contains(g.Id)))
            Visit(rest, 1);
        return result;
    }

    /// <summary>
    /// Whether <paramref name="groupId"/> may get <paramref name="parentId"/> as its parent: not itself or one of its own
    /// subgroups (a loop), and the result no deeper than <see cref="MaxLevels"/>.
    /// </summary>
    public static bool CanMoveUnder(this EventSetup setup, Guid groupId, Guid? parentId)
    {
        if (parentId is not { } p)
            return true;
        if (p == groupId || setup.FindGroup(p) is not { } parent || setup.DescendantsOf(groupId).Any(d => d.Id == p))
            return false;
        return setup.LevelOf(parent) + setup.HeightOf(groupId) <= MaxLevels;
    }
}
