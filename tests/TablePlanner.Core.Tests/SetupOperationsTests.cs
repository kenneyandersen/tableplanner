using TablePlanner.Core.Editing;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class SetupOperationsTests
{
    private const double Tolerance = 1e-9;

    private static (EventProject Project, Floor Floor) NewProject()
    {
        var project = new EventProject();
        return (project, SetupOperations.AddFloor(project, "Ground"));
    }

    [Fact]
    public void Floors_are_numbered_in_order()
    {
        var (project, ground) = NewProject();

        var first = SetupOperations.AddFloor(project);

        Assert.Equal(1, ground.Number);
        Assert.Equal(2, first.Number);
        Assert.Equal("Floor 2", first.Name);
    }

    [Fact]
    public void Added_tables_get_the_next_number_on_their_floor()
    {
        var (project, floor) = NewProject();

        var a = SetupOperations.AddTable(project, floor.Id, 0, 0);
        var b = SetupOperations.AddTable(project, floor.Id, 0, 3);

        Assert.Equal((101, 102), (a.Number, b.Number));
    }

    [Fact]
    public void Added_table_size_is_kept_within_limits()
    {
        var (project, floor) = NewProject();

        var table = SetupOperations.AddTable(project, floor.Id, 0, 0, seatsPerSide: 0, endSeats: 5);

        Assert.Equal((1, 2), (table.SeatsPerSide, table.EndSeats));
    }

    [Fact]
    public void Added_element_gets_default_size_and_label()
    {
        var (project, floor) = NewProject();

        var element = SetupOperations.AddElement(project, floor.Id, FloorElementKind.DanceFloor, 3, 4);

        Assert.Equal("Dance floor", element.Label);
        Assert.Equal((5.0, 5.0), (element.Width, element.Depth));
        Assert.Equal((3.0, 4.0), (element.X, element.Y));
    }

    [Fact]
    public void Duplicating_a_floor_copies_tables_with_the_new_floors_numbers()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 2, 3, seatsPerSide: 6);
        table.DisabledSeats = [1];
        SetupOperations.AddTable(project, floor.Id, 2, 6);
        SetupOperations.AddElement(project, floor.Id, FloorElementKind.Bar, 9, 9);

        var copy = SetupOperations.DuplicateFloor(project, floor.Id);

        Assert.Equal(2, copy.Number);
        Assert.Equal("Ground (copy)", copy.Name);
        Assert.Equal([201, 202], copy.Tables.Select(t => t.Number));
        Assert.Equal((2.0, 3.0, 6), (copy.Tables[0].X, copy.Tables[0].Y, copy.Tables[0].SeatsPerSide));
        Assert.Equal([1], copy.Tables[0].DisabledSeats);
        Assert.NotEqual(table.Id, copy.Tables[0].Id);
        Assert.Single(copy.Elements);
        Assert.NotEqual(floor.Elements[0].Id, copy.Elements[0].Id);

        copy.Tables[0].DisabledSeats.Add(2);
        Assert.Equal([1], table.DisabledSeats);
    }

    [Fact]
    public void Removing_a_floor_removes_pins_and_assignments_on_it()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0);
        var a = SetupOperations.AddGroup(project, "A");
        var b = SetupOperations.AddGroup(project, "B");
        project.Setup.Pins.Add(new Pin { GroupId = a.Id, Scope = PinScope.Floor, FloorId = floor.Id });
        project.Setup.Pins.Add(new Pin { GroupId = b.Id, Scope = PinScope.Table, TableId = table.Id });
        project.CurrentAssignment.Seats.Add(new(new SeatId(table.Id, 0), a.Id));

        SetupOperations.RemoveFloor(project, floor.Id);

        Assert.Empty(project.Setup.Floors);
        Assert.Empty(project.Setup.Pins);
        Assert.Empty(project.CurrentAssignment.Seats);
    }

    [Fact]
    public void Renumbering_orders_tables_top_to_bottom_then_left_to_right()
    {
        var (project, floor) = NewProject();
        var bottomRight = SetupOperations.AddTable(project, floor.Id, 10, 5);
        var topRight = SetupOperations.AddTable(project, floor.Id, 10, 1);
        var bottomLeft = SetupOperations.AddTable(project, floor.Id, 2, 5);
        var topLeft = SetupOperations.AddTable(project, floor.Id, 2, 1);

        SetupOperations.RenumberTables(project, floor.Id);

        Assert.Equal((101, 102, 103, 104), (topLeft.Number, topRight.Number, bottomLeft.Number, bottomRight.Number));
    }

    [Fact]
    public void Removing_items_removes_tables_elements_and_what_refers_to_them()
    {
        var (project, floor) = NewProject();
        var keep = SetupOperations.AddTable(project, floor.Id, 0, 0);
        var remove = SetupOperations.AddTable(project, floor.Id, 0, 3);
        var element = SetupOperations.AddElement(project, floor.Id, FloorElementKind.Bar, 5, 5);
        var a = SetupOperations.AddGroup(project, "A", 1);
        var b = SetupOperations.AddGroup(project, "B", 1);
        var c = SetupOperations.AddGroup(project, "C", 1);
        project.Setup.Pins.Add(new Pin { GroupId = a.Id, Scope = PinScope.Table, TableId = remove.Id });
        project.Setup.Pins.Add(new Pin { GroupId = b.Id, Scope = PinScope.Seats, Seats = [new(remove.Id, 0)] });
        project.Setup.Pins.Add(new Pin { GroupId = c.Id, Scope = PinScope.Table, TableId = keep.Id });
        project.CurrentAssignment.Seats.Add(new(new SeatId(remove.Id, 1), a.Id));
        project.CurrentAssignment.Seats.Add(new(new SeatId(keep.Id, 1), c.Id));

        SetupOperations.RemoveItems(project, floor.Id, [remove.Id, element.Id]);

        Assert.Equal([keep.Id], floor.Tables.Select(t => t.Id));
        Assert.Empty(floor.Elements);
        Assert.Equal([c.Id], project.Setup.Pins.Select(p => p.GroupId));
        Assert.Equal([c.Id], project.CurrentAssignment.Seats.Select(s => s.GroupId));
    }

    [Fact]
    public void Duplicating_items_offsets_copies_and_gives_new_numbers()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 1, 1);
        var element = SetupOperations.AddElement(project, floor.Id, FloorElementKind.Bar, 5, 5);

        var newIds = SetupOperations.DuplicateItems(project, floor.Id, [table.Id, element.Id], 1, 2);

        Assert.Equal(2, newIds.Count);
        var copy = floor.Tables.Single(t => t.Id == newIds[0]);
        Assert.Equal((102, 2.0, 3.0), (copy.Number, copy.X, copy.Y));
        var elementCopy = floor.Elements.Single(e => e.Id == newIds[1]);
        Assert.Equal((6.0, 7.0, "Bar"), (elementCopy.X, elementCopy.Y, elementCopy.Label));
    }

    [Fact]
    public void Moving_items_moves_only_the_selected_ones()
    {
        var (project, floor) = NewProject();
        var moved = SetupOperations.AddTable(project, floor.Id, 1, 1);
        var still = SetupOperations.AddTable(project, floor.Id, 5, 5);

        SetupOperations.MoveItems(project, floor.Id, [moved.Id], 0.5, -0.5);

        Assert.Equal((1.5, 0.5), (moved.X, moved.Y));
        Assert.Equal((5.0, 5.0), (still.X, still.Y));
    }

    [Fact]
    public void Rotating_turns_90_degrees_and_wraps_around()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0);
        var element = SetupOperations.AddElement(project, floor.Id, FloorElementKind.Stage, 0, 0);
        element.Rotation = Rotation.R270;

        SetupOperations.RotateItems(project, floor.Id, [table.Id, element.Id]);

        Assert.Equal(Rotation.R90, table.Rotation);
        Assert.Equal(Rotation.R0, element.Rotation);
    }

    [Theory]
    [InlineData(Rotation.R0)]
    [InlineData(Rotation.R90)]
    [InlineData(Rotation.R180)]
    [InlineData(Rotation.R270)]
    public void Resizing_keeps_the_left_end_in_place(Rotation rotation)
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 10, 10, seatsPerSide: 4);
        table.Rotation = rotation;
        var leftEndBefore = TableGeometry.GetSeats(table)[0].Position;

        SetupOperations.ResizeTable(project, table.Id, 7, 0);

        var leftEndAfter = TableGeometry.GetSeats(table)[0].Position;
        Assert.Equal(7, table.SeatsPerSide);
        Assert.Equal(leftEndBefore.X, leftEndAfter.X, Tolerance);
        Assert.Equal(leftEndBefore.Y, leftEndAfter.Y, Tolerance);
    }

    [Theory]
    [InlineData(Rotation.R0)]
    [InlineData(Rotation.R90)]
    [InlineData(Rotation.R180)]
    [InlineData(Rotation.R270)]
    public void Resizing_from_the_left_keeps_the_right_end_in_place(Rotation rotation)
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 10, 10, seatsPerSide: 4);
        table.Rotation = rotation;
        var rightEndBefore = TableGeometry.GetSeats(table)[3].Position;

        SetupOperations.ResizeTable(project, table.Id, 7, 0, keepRightEnd: true);

        var rightEndAfter = TableGeometry.GetSeats(table)[6].Position;
        Assert.Equal(rightEndBefore.X, rightEndAfter.X, Tolerance);
        Assert.Equal(rightEndBefore.Y, rightEndAfter.Y, Tolerance);
    }

    [Fact]
    public void Resizing_from_the_left_keeps_seats_and_assignments_where_they_are()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0, seatsPerSide: 4, endSeats: 2);
        var group = SetupOperations.AddGroup(project, "A", 3);
        // Top seats 0..3, bottom 4..7, ends 8 (left) and 9 (right).
        table.DisabledSeats = [0, 3];
        project.CurrentAssignment.Seats.AddRange([new(new(table.Id, 5), group.Id), new(new(table.Id, 7), group.Id), new(new(table.Id, 9), group.Id)]);
        var positions = project.CurrentAssignment.SeatsOf(group.Id).Select(Position).ToList();

        // Two seats more per side, added at the left: top 0..5, bottom 6..11, ends 12 and 13.
        SetupOperations.ResizeTable(project, table.Id, 6, 2, keepRightEnd: true);

        Assert.Equal([2, 5], table.DisabledSeats.Order());
        var seats = project.CurrentAssignment.SeatsOf(group.Id).OrderBy(s => s.Index).ToList();
        Assert.Equal([new SeatId(table.Id, 9), new SeatId(table.Id, 11), new SeatId(table.Id, 13)], seats);
        Assert.Equal(positions.OrderBy(p => p.X).ThenBy(p => p.Y), seats.Select(Position).OrderBy(p => p.X).ThenBy(p => p.Y),
            new PointComparer());

        // Shrinking from the left drops the left-most seats.
        SetupOperations.ResizeTable(project, table.Id, 3, 2, keepRightEnd: true);
        Assert.Equal([2], table.DisabledSeats);

        Point Position(SeatId id) => TableGeometry.GetSeats(table).First(s => s.Id == id).Position;
    }

    private sealed class PointComparer : IEqualityComparer<Point>
    {
        public bool Equals(Point a, Point b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
        public int GetHashCode(Point p) => 0;
    }

    [Fact]
    public void Element_bounds_respect_rotation_and_minimum_size()
    {
        var (project, floor) = NewProject();
        var element = SetupOperations.AddElement(project, floor.Id, FloorElementKind.Bar, 0, 0);
        element.Rotation = Rotation.R90;

        SetupOperations.SetElementBounds(project, floor.Id, element.Id, new Rect(1, 2, 3, 6));

        Assert.Equal((2.5, 5.0), (element.X, element.Y));
        Assert.Equal((6.0, 3.0), (element.Width, element.Depth));
        Assert.Equal(new Rect(1, 2, 3, 6), FloorElementGeometry.GetBounds(element));

        SetupOperations.SetElementBounds(project, floor.Id, element.Id, new Rect(0, 0, 0.1, 2));
        Assert.Equal(SetupOperations.MinElementSize, element.Depth);
    }

    [Fact]
    public void Elements_can_be_brought_to_front_and_sent_back()
    {
        var (project, floor) = NewProject();
        var a = SetupOperations.AddElement(project, floor.Id, FloorElementKind.DanceFloor, 0, 0);
        var b = SetupOperations.AddElement(project, floor.Id, FloorElementKind.Bar, 0, 0);
        var c = SetupOperations.AddElement(project, floor.Id, FloorElementKind.Toilets, 0, 0);

        SetupOperations.ReorderElement(project, floor.Id, a.Id, LayerMove.ToFront);
        Assert.Equal([b, c, a], floor.Elements);
        SetupOperations.ReorderElement(project, floor.Id, a.Id, LayerMove.Backward);
        Assert.Equal([b, a, c], floor.Elements);
        SetupOperations.ReorderElement(project, floor.Id, c.Id, LayerMove.ToBack);
        Assert.Equal([c, b, a], floor.Elements);
        SetupOperations.ReorderElement(project, floor.Id, c.Id, LayerMove.Forward);
        Assert.Equal([b, c, a], floor.Elements);
        SetupOperations.ReorderElement(project, floor.Id, a.Id, LayerMove.Forward);
        Assert.Equal([b, c, a], floor.Elements);
    }

    [Fact]
    public void Resizing_moves_disabled_seats_and_assignments_with_their_seats()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0, seatsPerSide: 4, endSeats: 2);
        var group = SetupOperations.AddGroup(project, "A", 3);
        // Seat 1 (top), seat 5 (bottom, 2nd from left), seat 9 (right end); seat 3 (top, last) disappears on shrinking.
        table.DisabledSeats = [1, 3];
        project.CurrentAssignment.Seats.AddRange([new(new(table.Id, 5), group.Id), new(new(table.Id, 9), group.Id), new(new(table.Id, 7), group.Id)]);

        SetupOperations.ResizeTable(project, table.Id, 3, 2);

        Assert.Equal([1], table.DisabledSeats);
        Assert.Equal([new SeatId(table.Id, 4), new SeatId(table.Id, 7)], project.CurrentAssignment.SeatsOf(group.Id).OrderBy(s => s.Index));
    }

    [Fact]
    public void Resizing_drops_seat_pins_that_lose_a_seat_and_remaps_others()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0, seatsPerSide: 4);
        var a = SetupOperations.AddGroup(project, "A", 2);
        var b = SetupOperations.AddGroup(project, "B", 2);
        project.Setup.Pins.Add(new Pin { GroupId = a.Id, Scope = PinScope.Seats, Seats = [new(table.Id, 0), new(table.Id, 4)] });
        project.Setup.Pins.Add(new Pin { GroupId = b.Id, Scope = PinScope.Seats, Seats = [new(table.Id, 3), new(table.Id, 7)] });

        SetupOperations.ResizeTable(project, table.Id, 2, 0);

        var pin = Assert.Single(project.Setup.Pins);
        Assert.Equal(a.Id, pin.GroupId);
        Assert.Equal([new SeatId(table.Id, 0), new SeatId(table.Id, 2)], pin.Seats);
    }

    [Theory]
    [InlineData(0, 4, 0, 6, 0, 0)]
    [InlineData(3, 4, 0, 6, 0, 3)]
    [InlineData(4, 4, 0, 6, 0, 6)]
    [InlineData(7, 4, 0, 6, 0, 9)]
    [InlineData(8, 4, 2, 6, 2, 12)]
    [InlineData(9, 4, 2, 6, 2, 13)]
    [InlineData(9, 4, 2, 6, 1, null)]
    [InlineData(3, 4, 0, 3, 0, null)]
    [InlineData(-1, 4, 0, 4, 0, null)]
    public void Seat_indices_remap_by_side_and_position(int index, int oldPerSide, int oldEnds, int newPerSide, int newEnds, int? expected)
    {
        Assert.Equal(expected, LongTableGeometry.RemapSeatIndex(index, oldPerSide, oldEnds, newPerSide, newEnds));
    }

    [Fact]
    public void Disabling_a_seat_removes_its_assignment_and_enabling_restores_availability()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0);
        var group = SetupOperations.AddGroup(project, "A", 1);
        project.CurrentAssignment.Seats.Add(new(new(table.Id, 2), group.Id));

        SetupOperations.ToggleSeatDisabled(project, table.Id, 2);
        Assert.Contains(2, table.DisabledSeats);
        Assert.Empty(project.CurrentAssignment.Seats);

        SetupOperations.ToggleSeatDisabled(project, table.Id, 2);
        Assert.DoesNotContain(2, table.DisabledSeats);
    }

    [Fact]
    public void New_groups_get_unique_names()
    {
        var project = new EventProject();
        SetupOperations.AddGroup(project, "Group 2");

        var group = SetupOperations.AddGroup(project);

        Assert.Equal("Group 3", group.Name);
    }

    [Fact]
    public void Removing_a_group_removes_it_from_preferences_pins_and_assignments()
    {
        var (project, floor) = NewProject();
        var table = SetupOperations.AddTable(project, floor.Id, 0, 0);
        var a = SetupOperations.AddGroup(project, "A", 1);
        var b = SetupOperations.AddGroup(project, "B", 1);
        b.PreferredGroupIds = [a.Id];
        project.Setup.Pins.Add(new Pin { GroupId = a.Id, Scope = PinScope.Table, TableId = table.Id });
        project.CurrentAssignment.Seats.Add(new(new(table.Id, 0), a.Id));

        SetupOperations.RemoveGroup(project, a.Id);

        Assert.Equal([b.Id], project.Setup.Groups.Select(g => g.Id));
        Assert.Empty(b.PreferredGroupIds);
        Assert.Empty(project.Setup.Pins);
        Assert.Empty(project.CurrentAssignment.Seats);
    }

    [Fact]
    public void Setting_preferences_drops_invalid_entries_and_keeps_order()
    {
        var project = new EventProject();
        var groups = Enumerable.Range(0, 7).Select(i => SetupOperations.AddGroup(project, $"G{i}", 1)).ToList();
        var me = groups[0];

        SetupOperations.SetPreferences(project, me.Id,
            [groups[3].Id, me.Id, Guid.NewGuid(), groups[1].Id, groups[3].Id, groups[2].Id, groups[4].Id, groups[5].Id, groups[6].Id]);

        Assert.Equal([groups[3].Id, groups[1].Id, groups[2].Id, groups[4].Id, groups[5].Id], me.PreferredGroupIds);
    }

    [Fact]
    public void End_seats_are_added_and_removed_one_end_at_a_time()
    {
        var (project, floor) = NewProject();
        var table = new Table { Number = 1, SeatsPerSide = 4, EndSeats = 0 };
        floor.Tables.Add(table);
        var (left, right) = (8, 9);

        SetupOperations.ToggleEndSeat(project, table.Id, rightEnd: true);
        Assert.Equal(2, table.EndSeats);
        Assert.Equal([left], table.DisabledSeats);
        Assert.False(SetupOperations.HasEndSeat(table, rightEnd: false));
        Assert.True(SetupOperations.HasEndSeat(table, rightEnd: true));

        SetupOperations.ToggleEndSeat(project, table.Id, rightEnd: false);
        Assert.Equal(2, table.EndSeats);
        Assert.Empty(table.DisabledSeats);

        project.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(table.Id, right), Guid.NewGuid()));
        SetupOperations.ToggleEndSeat(project, table.Id, rightEnd: true);
        Assert.Equal(1, table.EndSeats);
        Assert.Empty(project.CurrentAssignment.Seats);

        SetupOperations.ToggleEndSeat(project, table.Id, rightEnd: false);
        Assert.Equal(0, table.EndSeats);
        Assert.Empty(table.DisabledSeats);
    }
}
