using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Editing;

/// <summary>A change in a floor element's drawing order.</summary>
public enum LayerMove
{
    ToFront,
    Forward,
    Backward,
    ToBack,
}

/// <summary>
/// Edits to a project's setup. Each operation keeps the project consistent, e.g. removing a table
/// also removes pins and seat assignments that refer to it. Run them through <see cref="EditHistory"/> for undo.
/// </summary>
public static class SetupOperations
{
    // ---- Floors ----

    public static Floor AddFloor(EventProject project, string? name = null)
    {
        var number = project.Setup.Floors.Count == 0 ? 1 : project.Setup.Floors.Max(f => f.Number) + 1;
        var floor = new Floor { Name = name ?? $"Floor {number}", Number = number };
        project.Setup.Floors.Add(floor);
        return floor;
    }

    /// <summary>Copies a floor with all its tables, elements and background image as a new floor. Tables get the new floor's numbers.</summary>
    public static Floor DuplicateFloor(EventProject project, Guid floorId)
    {
        var source = GetFloor(project, floorId);
        var copy = AddFloor(project, $"{source.Name} (copy)");
        foreach (var table in source.Tables)
        {
            var clone = CloneTable(table);
            clone.Number = copy.NextTableNumber();
            copy.Tables.Add(clone);
        }
        copy.Elements.AddRange(source.Elements.Select(CloneElement));
        copy.Background = source.Background is { } b ? BackgroundOperations.Clone(b) : null;
        return copy;
    }

    public static void RemoveFloor(EventProject project, Guid floorId)
    {
        var floor = GetFloor(project, floorId);
        RemoveItems(project, floorId, floor.Tables.Select(t => t.Id).ToList());
        project.Setup.Pins.RemoveAll(p => p.Scope == PinScope.Floor && p.FloorId == floorId);
        project.Setup.Floors.Remove(floor);
    }

    /// <summary>Numbers the floor's tables 101, 102, … (for floor 1) in reading order: top to bottom, left to right.</summary>
    public static void RenumberTables(EventProject project, Guid floorId)
    {
        var floor = GetFloor(project, floorId);
        var ordered = floor.Tables.OrderBy(t => Math.Round(t.Y, 1)).ThenBy(t => t.X).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].Number = floor.Number * 100 + i + 1;
    }

    // ---- Tables and elements ----

    public static Table AddTable(EventProject project, Guid floorId, double x, double y, int seatsPerSide = 8, int endSeats = 0)
    {
        var floor = GetFloor(project, floorId);
        var table = new Table
        {
            Number = floor.NextTableNumber(),
            X = x,
            Y = y,
            SeatsPerSide = Math.Max(1, seatsPerSide),
            EndSeats = Math.Clamp(endSeats, 0, Table.MaxEndSeats),
        };
        floor.Tables.Add(table);
        return table;
    }

    public static Table AddRoundTable(EventProject project, Guid floorId, double x, double y, int seats = 8)
    {
        var floor = GetFloor(project, floorId);
        var table = new Table
        {
            Number = floor.NextTableNumber(),
            Shape = TableShape.Round,
            X = x,
            Y = y,
            RoundSeats = Math.Max(Table.MinRoundSeats, seats),
        };
        floor.Tables.Add(table);
        return table;
    }

    public static FloorElement AddElement(EventProject project, Guid floorId, FloorElementKind kind, double x, double y)
    {
        var (width, depth) = FloorElementGeometry.DefaultSize(kind);
        var element = new FloorElement
        {
            Kind = kind,
            Label = FloorElementGeometry.DisplayName(kind),
            X = x,
            Y = y,
            Width = width,
            Depth = depth,
        };
        GetFloor(project, floorId).Elements.Add(element);
        return element;
    }

    /// <summary>Removes tables and/or elements, plus pins and seat assignments that refer to removed tables.</summary>
    public static void RemoveItems(EventProject project, Guid floorId, IReadOnlyCollection<Guid> itemIds)
    {
        var floor = GetFloor(project, floorId);
        var removedTables = floor.Tables.Where(t => itemIds.Contains(t.Id)).Select(t => t.Id).ToHashSet();

        floor.Tables.RemoveAll(t => removedTables.Contains(t.Id));
        floor.Elements.RemoveAll(e => itemIds.Contains(e.Id));

        project.Setup.Pins.RemoveAll(p =>
            (p.Scope == PinScope.Table && p.TableId is { } t && removedTables.Contains(t))
            || (p.Scope == PinScope.Seats && p.Seats.Any(s => removedTables.Contains(s.TableId))));
        project.CurrentAssignment.Seats.RemoveAll(s => removedTables.Contains(s.Seat.TableId));
    }

    /// <summary>Copies tables and/or elements, offset by (dx, dy). Copied tables get new numbers. Returns the new IDs.</summary>
    public static List<Guid> DuplicateItems(EventProject project, Guid floorId, IReadOnlyCollection<Guid> itemIds, double dx, double dy)
    {
        var floor = GetFloor(project, floorId);
        var newIds = new List<Guid>();

        foreach (var table in floor.Tables.Where(t => itemIds.Contains(t.Id)).ToList())
        {
            var clone = CloneTable(table);
            clone.Number = floor.NextTableNumber();
            clone.X += dx;
            clone.Y += dy;
            floor.Tables.Add(clone);
            newIds.Add(clone.Id);
        }

        foreach (var element in floor.Elements.Where(e => itemIds.Contains(e.Id)).ToList())
        {
            var clone = CloneElement(element);
            clone.X += dx;
            clone.Y += dy;
            floor.Elements.Add(clone);
            newIds.Add(clone.Id);
        }

        return newIds;
    }

    public static void MoveItems(EventProject project, Guid floorId, IReadOnlyCollection<Guid> itemIds, double dx, double dy)
    {
        var floor = GetFloor(project, floorId);
        foreach (var table in floor.Tables.Where(t => itemIds.Contains(t.Id)))
        {
            table.X += dx;
            table.Y += dy;
        }
        foreach (var element in floor.Elements.Where(e => itemIds.Contains(e.Id)))
        {
            element.X += dx;
            element.Y += dy;
        }
    }

    /// <summary>Turns each item 90° clockwise around its own centre.</summary>
    public static void RotateItems(EventProject project, Guid floorId, IReadOnlyCollection<Guid> itemIds)
    {
        var floor = GetFloor(project, floorId);
        foreach (var table in floor.Tables.Where(t => itemIds.Contains(t.Id)))
            table.Rotation = Next(table.Rotation);
        foreach (var element in floor.Elements.Where(e => itemIds.Contains(e.Id)))
            element.Rotation = Next(element.Rotation);
    }

    /// <summary>
    /// Changes a table's size while keeping its left end (in the table's own frame) in place, or its right end when
    /// <paramref name="keepRightEnd"/> is set, so seats are added or removed at the other end.
    /// Disabled seats, seat pins and seat assignments move with their seats; seats that disappear are dropped.
    /// </summary>
    public static void ResizeTable(EventProject project, Guid tableId, int seatsPerSide, int endSeats, bool keepRightEnd = false)
    {
        var table = GetTable(project, tableId);
        seatsPerSide = Math.Max(1, seatsPerSide);
        endSeats = Math.Clamp(endSeats, 0, Table.MaxEndSeats);
        if (seatsPerSide == table.SeatsPerSide && endSeats == table.EndSeats)
            return;

        var shiftFromLeft = keepRightEnd ? seatsPerSide - table.SeatsPerSide : 0;
        RemapSeats(project, table, index =>
            LongTableGeometry.RemapSeatIndex(index, table.SeatsPerSide, table.EndSeats, seatsPerSide, endSeats, shiftFromLeft));

        // Keep one end fixed: shift the centre by half the change in length along the table's axis, away from that end.
        var shift = (seatsPerSide - table.SeatsPerSide) * table.SeatSpacing / 2 * (keepRightEnd ? -1 : 1);
        var axis = new Point(1, 0).Rotate(table.Rotation);
        // Rounded to millimetres, so repeated resizing doesn't leave values like 6.8999999.
        table.X = Math.Round(table.X + axis.X * shift, 3);
        table.Y = Math.Round(table.Y + axis.Y * shift, 3);
        table.SeatsPerSide = seatsPerSide;
        table.EndSeats = endSeats;
    }

    /// <summary>
    /// Changes the number of seats at a round table; the table keeps its centre. Seats keep their numbers (clockwise
    /// from the top), so removing seats drops the last ones, with their assignments and pins.
    /// </summary>
    public static void ResizeRoundTable(EventProject project, Guid tableId, int seats)
    {
        var table = GetTable(project, tableId);
        seats = Math.Max(Table.MinRoundSeats, seats);
        if (!table.IsRound || seats == table.RoundSeats)
            return;
        RemapSeats(project, table, index => index >= 0 && index < seats ? index : null);
        table.RoundSeats = seats;
    }

    /// <summary>
    /// Changes a table's shape. Seat numbers mean something else on the other shape, so the table's disabled seats,
    /// seat assignments and seat pins are cleared. A round table gets as many seats as the long one had, and back.
    /// </summary>
    public static void SetTableShape(EventProject project, Guid tableId, TableShape shape)
    {
        var table = GetTable(project, tableId);
        if (table.Shape == shape)
            return;
        var seats = table.SeatCount;
        RemapSeats(project, table, _ => null);
        table.Shape = shape;
        if (shape == TableShape.Round)
            table.RoundSeats = Math.Max(Table.MinRoundSeats, seats);
        else
            (table.SeatsPerSide, table.EndSeats) = (Math.Max(1, seats / 2), seats % 2);
    }

    /// <summary>Moves disabled seats, seat assignments and seat pins of a table to new seat numbers; null drops the seat.</summary>
    private static void RemapSeats(EventProject project, Table table, Func<int, int?> remap)
    {
        var tableId = table.Id;
        table.DisabledSeats = table.DisabledSeats.Select(remap).OfType<int>().ToHashSet();

        var assignments = project.CurrentAssignment.Seats;
        for (var i = assignments.Count - 1; i >= 0; i--)
        {
            if (assignments[i].Seat.TableId != tableId) continue;
            if (remap(assignments[i].Seat.Index) is { } newIndex)
                assignments[i] = assignments[i] with { Seat = new SeatId(tableId, newIndex) };
            else
                assignments.RemoveAt(i);
        }

        // A seat pin that loses a seat no longer describes the group's place, so it is removed.
        project.Setup.Pins.RemoveAll(p => p.Scope == PinScope.Seats && p.Seats.Any(s => s.TableId == tableId && remap(s.Index) is null));
        foreach (var pin in project.Setup.Pins.Where(p => p.Scope == PinScope.Seats))
            pin.Seats = pin.Seats.Select(s => s.TableId == tableId ? new SeatId(tableId, remap(s.Index)!.Value) : s).ToList();
    }

    /// <summary>Smallest width or depth of a floor element, in metres.</summary>
    public const double MinElementSize = 0.5;

    /// <summary>
    /// Gives an element new outer bounds (in floor coordinates, as drawn), keeping its rotation: for an element turned a
    /// quarter, the drawn width is its depth. Sizes below <see cref="MinElementSize"/> are raised to it.
    /// </summary>
    public static void SetElementBounds(EventProject project, Guid floorId, Guid elementId, Rect bounds)
    {
        var element = GetElement(project, floorId, elementId);
        var drawnWidth = Math.Round(Math.Max(MinElementSize, bounds.Width), 3);
        var drawnHeight = Math.Round(Math.Max(MinElementSize, bounds.Height), 3);
        var quarterTurn = element.Rotation is Rotation.R90 or Rotation.R270;
        (element.Width, element.Depth) = quarterTurn ? (drawnHeight, drawnWidth) : (drawnWidth, drawnHeight);
        element.X = Math.Round(bounds.Left + bounds.Width / 2, 3);
        element.Y = Math.Round(bounds.Top + bounds.Height / 2, 3);
    }

    /// <summary>
    /// Changes the drawing order of floor elements (later ones are drawn on top, e.g. toilets inside a hall).
    /// Tables are always drawn above elements.
    /// </summary>
    public static void ReorderElement(EventProject project, Guid floorId, Guid elementId, LayerMove move)
    {
        var floor = GetFloor(project, floorId);
        var element = GetElement(project, floorId, elementId);
        var index = floor.Elements.IndexOf(element);
        var target = move switch
        {
            LayerMove.ToFront => floor.Elements.Count - 1,
            LayerMove.ToBack => 0,
            LayerMove.Forward => Math.Min(floor.Elements.Count - 1, index + 1),
            LayerMove.Backward => Math.Max(0, index - 1),
            _ => index,
        };
        floor.Elements.RemoveAt(index);
        floor.Elements.Insert(target, element);
    }

    public static void ToggleSeatDisabled(EventProject project, Guid tableId, int seatIndex)
    {
        var table = GetTable(project, tableId);
        if (!table.IsValidSeatIndex(seatIndex))
            return;
        if (!table.DisabledSeats.Remove(seatIndex))
        {
            table.DisabledSeats.Add(seatIndex);
            project.CurrentAssignment.Seats.RemoveAll(s => s.Seat == new SeatId(tableId, seatIndex));
        }
    }

    // ---- Groups ----

    public static Group AddGroup(EventProject project, string? name = null, int peopleCount = 4, Guid? parentId = null)
    {
        var group = new Group { Name = name ?? UniqueGroupName(project), PeopleCount = peopleCount };
        project.Setup.Groups.Add(group);
        if (parentId is { } p)
            SetParent(project, group.Id, p);
        return group;
    }

    /// <summary>
    /// Why the groups cannot be put together in a new parent group, or null when they can: at least one group, all
    /// known, and with the new level no more than <see cref="GroupTree.MaxLevels"/> deep. The new group goes under the
    /// groups' common parent, if they share one, otherwise at the top.
    /// </summary>
    public static string? CannotGroup(EventProject project, IReadOnlyCollection<Guid> groupIds)
    {
        var setup = project.Setup;
        var groups = groupIds.Distinct().Select(setup.FindGroup).ToList();
        if (groups.Count == 0)
            return "Select the groups to put together first.";
        if (groups.Any(g => g is null))
            return "A selected group no longer exists.";
        var parentLevel = CommonParent(setup, groups!) is { } parent ? setup.LevelOf(parent) : 0;
        var height = groups.Max(g => setup.HeightOf(g!.Id));
        return parentLevel + 1 + height > GroupTree.MaxLevels
            ? $"That would make more than {GroupTree.MaxLevels} levels of groups."
            : null;
    }

    /// <summary>
    /// Puts the groups in a new parent group named <paramref name="name"/> (see <see cref="CannotGroup"/>), placed in the
    /// list where the first of them was. Returns the new group.
    /// </summary>
    public static Group GroupInto(EventProject project, IReadOnlyCollection<Guid> groupIds, string name)
    {
        if (CannotGroup(project, groupIds) is { } reason)
            throw new InvalidOperationException(reason);
        var setup = project.Setup;
        var groups = groupIds.Distinct().Select(id => setup.FindGroup(id)!).ToList();
        var parent = new Group { Name = name.Trim(), PeopleCount = 0, ParentId = CommonParent(setup, groups)?.Id };
        setup.Groups.Insert(groups.Min(g => setup.Groups.IndexOf(g)), parent);
        foreach (var group in groups)
            group.ParentId = parent.Id;
        return parent;
    }

    /// <summary>
    /// Removes a parent group and moves its subgroups up a level, to its own parent. Preferences for it and its pins go
    /// with it.
    /// </summary>
    public static void Ungroup(EventProject project, Guid parentId)
    {
        var setup = project.Setup;
        if (setup.FindGroup(parentId) is not { } parent)
            return;
        foreach (var child in setup.ChildrenOf(parentId).ToList())
            child.ParentId = parent.ParentId;
        RemoveGroup(project, parentId);
    }

    /// <summary>
    /// Moves a group (with its subgroups) under <paramref name="parentId"/>, or to the top level when null. When the new
    /// parent is a bottom group with people, those people move to a new subgroup "&lt;name&gt; – other" (with its seats
    /// and pins), since a parent group has no people of its own. Throws when the move would make a loop or too many levels.
    /// </summary>
    public static void SetParent(EventProject project, Guid groupId, Guid? parentId)
    {
        var setup = project.Setup;
        var group = setup.FindGroup(groupId) ?? throw new ArgumentException("Unknown group.", nameof(groupId));
        if (group.ParentId == parentId)
            return;
        if (!setup.CanMoveUnder(groupId, parentId))
            throw new InvalidOperationException(
                $"'{group.Name}' cannot go there: a group cannot be inside itself, and there can be at most {GroupTree.MaxLevels} levels.");
        if (parentId is { } p && setup.FindGroup(p) is { } parent && !setup.HasChildren(p))
            MovePeopleToOtherGroup(project, parent);
        group.ParentId = parentId;
    }

    /// <summary>A bottom group is becoming a parent: its people (seats, seat and table pins) move to "&lt;name&gt; – other".</summary>
    private static void MovePeopleToOtherGroup(EventProject project, Group group)
    {
        if (group.PeopleCount <= 0 && group.Members.Count == 0)
            return;
        var other = new Group
        {
            Name = UniqueName(project, $"{group.Name} – other"),
            ParentId = group.Id,
            PeopleCount = Math.Max(group.PeopleCount, group.Members.Count),
            Members = group.Members,
            DietaryInfo = group.DietaryInfo,
            ContactPerson = group.ContactPerson,
        };
        project.Setup.Groups.Insert(project.Setup.Groups.IndexOf(group) + 1, other);
        group.PeopleCount = 0;
        group.Members = [];
        project.CurrentAssignment.Seats = project.CurrentAssignment.Seats
            .Select(s => s.GroupId == group.Id ? s with { GroupId = other.Id } : s)
            .ToList();
        foreach (var pin in project.Setup.Pins.Where(p => p.GroupId == group.Id && p.Scope != PinScope.Floor))
            pin.GroupId = other.Id;
    }

    private static Group? CommonParent(EventSetup setup, IReadOnlyList<Group> groups)
    {
        var parentId = groups[0].ParentId;
        return groups.All(g => g.ParentId == parentId) && parentId is { } id ? setup.FindGroup(id) : null;
    }

    private static string UniqueName(EventProject project, string name)
    {
        var names = project.Setup.Groups.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unique = name;
        for (var n = 2; names.Contains(unique); n++)
            unique = $"{name} {n}";
        return unique;
    }

    /// <summary>
    /// Removes a group, and its pins, seat assignments and mentions in other groups' preferences. Its subgroups, if
    /// any, move up to its parent (they are not deleted).
    /// </summary>
    public static void RemoveGroup(EventProject project, Guid groupId)
    {
        var removed = project.Setup.FindGroup(groupId);
        foreach (var child in project.Setup.ChildrenOf(groupId).ToList())
            child.ParentId = removed?.ParentId;
        project.Setup.Groups.RemoveAll(g => g.Id == groupId);
        foreach (var group in project.Setup.Groups)
            group.PreferredGroupIds.Remove(groupId);
        project.Setup.Pins.RemoveAll(p => p.GroupId == groupId);
        project.CurrentAssignment.Seats.RemoveAll(s => s.GroupId == groupId);
    }

    // ---- People (names in groups) ----

    /// <summary>
    /// The group that holds people added to <paramref name="group"/>: the group itself when it has no subgroups; for a
    /// parent group (which has no people of its own) its subgroup "&lt;name&gt; – other", made when missing.
    /// </summary>
    public static Group HolderFor(EventProject project, Group group)
    {
        var setup = project.Setup;
        if (!setup.HasChildren(group.Id))
            return group;
        var otherName = $"{group.Name} – other";
        if (setup.ChildrenOf(group.Id).FirstOrDefault(c => !setup.HasChildren(c.Id) && c.Name.StartsWith(otherName, StringComparison.OrdinalIgnoreCase)) is { } existing)
            return existing;
        var other = new Group { Name = UniqueName(project, otherName), ParentId = group.Id, PeopleCount = 0 };
        setup.Groups.Insert(setup.Groups.IndexOf(group) + 1, other);
        return other;
    }

    /// <summary>
    /// Adds named people to a group (or, for a parent group, to its "– other" subgroup). Empty names are skipped. The
    /// group's number of people grows when it would otherwise have more names than people. Returns the group they went to.
    /// </summary>
    public static Group AddPeople(EventProject project, Guid groupId, IEnumerable<string> names)
    {
        var group = project.Setup.FindGroup(groupId) ?? throw new ArgumentException("Unknown group.", nameof(groupId));
        var holder = HolderFor(project, group);
        foreach (var name in names.Select(n => n.Trim()).Where(n => n.Length > 0))
            holder.Members.Add(new Person { Name = name });
        holder.PeopleCount = Math.Max(holder.PeopleCount, holder.Members.Count);
        return holder;
    }

    /// <summary>Finds a named person and the group they are in.</summary>
    public static (Group Group, Person Person)? FindPerson(EventSetup setup, Guid personId)
    {
        foreach (var group in setup.Groups)
            if (group.Members.FirstOrDefault(m => m.Id == personId) is { } person)
                return (group, person);
        return null;
    }

    public static void RenamePerson(EventProject project, Guid personId, string name)
    {
        if (FindPerson(project.Setup, personId) is { Person: var person } && name.Trim().Length > 0)
            person.Name = name.Trim();
    }

    /// <summary>Removes a named person: they are not coming, so the group has one person fewer.</summary>
    public static void RemovePerson(EventProject project, Guid personId)
    {
        if (FindPerson(project.Setup, personId) is not { } found)
            return;
        var (group, person) = found;
        group.Members.Remove(person);
        group.PeopleCount = Math.Max(group.Members.Count, group.PeopleCount - 1);
    }

    /// <summary>
    /// Moves a named person to another group (or to a parent group's "– other" subgroup): one person fewer in the old
    /// group, one more in the new one.
    /// </summary>
    public static void MovePersonToGroup(EventProject project, Guid personId, Guid groupId)
    {
        if (FindPerson(project.Setup, personId) is not { } found || project.Setup.FindGroup(groupId) is not { } target)
            return;
        var (from, person) = found;
        var to = HolderFor(project, target);
        if (to == from)
            return;
        from.Members.Remove(person);
        from.PeopleCount = Math.Max(from.Members.Count, from.PeopleCount - 1);
        to.Members.Add(person);
        to.PeopleCount = Math.Max(to.Members.Count, to.PeopleCount + 1);
    }

    /// <summary>Sets a group's preferences, dropping unknown groups, itself and duplicates, and keeping at most the maximum.</summary>
    public static void SetPreferences(EventProject project, Guid groupId, IEnumerable<Guid> preferredGroupIds)
    {
        var group = project.Setup.FindGroup(groupId) ?? throw new ArgumentException("Unknown group.", nameof(groupId));
        var known = project.Setup.Groups.Select(g => g.Id).ToHashSet();
        group.PreferredGroupIds = preferredGroupIds
            .Where(id => id != groupId && known.Contains(id))
            .Distinct()
            .Take(Group.MaxPreferences)
            .ToList();
    }

    private static string UniqueGroupName(EventProject project)
    {
        var names = project.Setup.Groups.Select(g => g.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var n = project.Setup.Groups.Count + 1;
        while (names.Contains($"Group {n}"))
            n++;
        return $"Group {n}";
    }

    // ---- Helpers ----

    private static Floor GetFloor(EventProject project, Guid floorId) =>
        project.Setup.Floors.FirstOrDefault(f => f.Id == floorId) ?? throw new ArgumentException("Unknown floor.", nameof(floorId));

    private static Table GetTable(EventProject project, Guid tableId) =>
        project.Setup.FindTable(tableId) ?? throw new ArgumentException("Unknown table.", nameof(tableId));

    private static FloorElement GetElement(EventProject project, Guid floorId, Guid elementId) =>
        GetFloor(project, floorId).Elements.FirstOrDefault(e => e.Id == elementId)
        ?? throw new ArgumentException("Unknown element.", nameof(elementId));

    private static Rotation Next(Rotation rotation) => (Rotation)(((int)rotation + 1) % 4);

    private static Table CloneTable(Table t) => new()
    {
        Number = t.Number,
        Shape = t.Shape,
        X = t.X,
        Y = t.Y,
        Rotation = t.Rotation,
        SeatsPerSide = t.SeatsPerSide,
        EndSeats = t.EndSeats,
        RoundSeats = t.RoundSeats,
        SeatSpacing = t.SeatSpacing,
        Width = t.Width,
        DisabledSeats = [.. t.DisabledSeats],
    };

    private static FloorElement CloneElement(FloorElement e) => new()
    {
        Kind = e.Kind,
        Label = e.Label,
        X = e.X,
        Y = e.Y,
        Width = e.Width,
        Depth = e.Depth,
        Rotation = e.Rotation,
    };
}
