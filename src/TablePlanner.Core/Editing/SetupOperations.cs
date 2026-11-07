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
        int? Remap(int index) =>
            LongTableGeometry.RemapSeatIndex(index, table.SeatsPerSide, table.EndSeats, seatsPerSide, endSeats, shiftFromLeft);

        table.DisabledSeats = table.DisabledSeats.Select(Remap).OfType<int>().ToHashSet();

        var assignments = project.CurrentAssignment.Seats;
        for (var i = assignments.Count - 1; i >= 0; i--)
        {
            if (assignments[i].Seat.TableId != tableId) continue;
            if (Remap(assignments[i].Seat.Index) is { } newIndex)
                assignments[i] = assignments[i] with { Seat = new SeatId(tableId, newIndex) };
            else
                assignments.RemoveAt(i);
        }

        // A seat pin that loses a seat no longer describes the group's place, so it is removed.
        project.Setup.Pins.RemoveAll(p => p.Scope == PinScope.Seats && p.Seats.Any(s => s.TableId == tableId && Remap(s.Index) is null));
        foreach (var pin in project.Setup.Pins.Where(p => p.Scope == PinScope.Seats))
            pin.Seats = pin.Seats.Select(s => s.TableId == tableId ? new SeatId(tableId, Remap(s.Index)!.Value) : s).ToList();

        // Keep one end fixed: shift the centre by half the change in length along the table's axis, away from that end.
        var shift = (seatsPerSide - table.SeatsPerSide) * table.SeatSpacing / 2 * (keepRightEnd ? -1 : 1);
        var axis = new Point(1, 0).Rotate(table.Rotation);
        // Rounded to millimetres, so repeated resizing doesn't leave values like 6.8999999.
        table.X = Math.Round(table.X + axis.X * shift, 3);
        table.Y = Math.Round(table.Y + axis.Y * shift, 3);
        table.SeatsPerSide = seatsPerSide;
        table.EndSeats = endSeats;
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

    public static Group AddGroup(EventProject project, string? name = null, int peopleCount = 4)
    {
        var group = new Group { Name = name ?? UniqueGroupName(project), PeopleCount = peopleCount };
        project.Setup.Groups.Add(group);
        return group;
    }

    /// <summary>Removes a group, and its pins, seat assignments and mentions in other groups' preferences.</summary>
    public static void RemoveGroup(EventProject project, Guid groupId)
    {
        project.Setup.Groups.RemoveAll(g => g.Id == groupId);
        foreach (var group in project.Setup.Groups)
            group.PreferredGroupIds.Remove(groupId);
        project.Setup.Pins.RemoveAll(p => p.GroupId == groupId);
        project.CurrentAssignment.Seats.RemoveAll(s => s.GroupId == groupId);
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
