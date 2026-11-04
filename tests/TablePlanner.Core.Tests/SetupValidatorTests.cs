using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;

namespace TablePlanner.Core.Tests;

public class SetupValidatorTests
{
    /// <summary>One floor with one table of 10 seats and two groups of 4.</summary>
    private static EventSetup ValidSetup()
    {
        var table = new Table { Number = 101, SeatsPerSide = 5 };
        return new EventSetup
        {
            Floors = [new Floor { Name = "Ground", Number = 1, Tables = [table] }],
            Groups = [new Group { Name = "A", PeopleCount = 4 }, new Group { Name = "B", PeopleCount = 4 }],
        };
    }

    private static Table OnlyTable(EventSetup setup) => setup.Floors[0].Tables[0];

    private static ValidationIssue Single(EventSetup setup, IssueCode code) =>
        Assert.Single(SetupValidator.Validate(setup).Issues, i => i.Code == code);

    [Fact]
    public void Valid_setup_has_no_issues()
    {
        Assert.Empty(SetupValidator.Validate(ValidSetup()).Issues);
    }

    [Fact]
    public void Example_project_has_no_issues()
    {
        Assert.Empty(SetupValidator.Validate(ExampleProject.Create().Setup).Issues);
    }

    [Fact]
    public void Group_without_name_is_an_error()
    {
        var setup = ValidSetup();
        setup.Groups[0].Name = "  ";

        Assert.Equal(IssueSeverity.Error, Single(setup, IssueCode.GroupNameMissing).Severity);
    }

    [Fact]
    public void Duplicate_group_names_are_an_error_ignoring_case_and_spaces()
    {
        var setup = ValidSetup();
        setup.Groups[1].Name = " a ";

        Assert.Equal(IssueSeverity.Error, Single(setup, IssueCode.GroupNameDuplicate).Severity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void Group_with_no_people_is_an_error(int count)
    {
        var setup = ValidSetup();
        setup.Groups[0].PeopleCount = count;

        Assert.Equal(IssueSeverity.Error, Single(setup, IssueCode.GroupSizeInvalid).Severity);
    }

    [Fact]
    public void More_than_five_preferences_is_an_error()
    {
        var setup = ValidSetup();
        var others = Enumerable.Range(0, 6).Select(i => new Group { Name = $"G{i}", PeopleCount = 1 }).ToList();
        setup.Groups.AddRange(others);
        setup.Groups[0].PreferredGroupIds = others.Select(g => g.Id).ToList();

        Single(setup, IssueCode.TooManyPreferences);
    }

    [Fact]
    public void Preference_for_unknown_group_is_an_error()
    {
        var setup = ValidSetup();
        setup.Groups[0].PreferredGroupIds = [Guid.NewGuid()];

        Single(setup, IssueCode.PreferenceUnknownGroup);
    }

    [Fact]
    public void Preference_for_itself_is_an_error()
    {
        var setup = ValidSetup();
        setup.Groups[0].PreferredGroupIds = [setup.Groups[0].Id];

        Single(setup, IssueCode.PreferenceSelf);
    }

    [Fact]
    public void Duplicate_preference_is_an_error()
    {
        var setup = ValidSetup();
        setup.Groups[0].PreferredGroupIds = [setup.Groups[1].Id, setup.Groups[1].Id];

        Single(setup, IssueCode.PreferenceDuplicate);
    }

    [Fact]
    public void Table_without_seats_is_an_error()
    {
        var setup = ValidSetup();
        OnlyTable(setup).SeatsPerSide = 0;

        Single(setup, IssueCode.TableSizeInvalid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void End_seats_outside_0_to_2_is_an_error(int endSeats)
    {
        var setup = ValidSetup();
        OnlyTable(setup).EndSeats = endSeats;

        Single(setup, IssueCode.TableEndSeatsInvalid);
    }

    [Fact]
    public void Non_positive_table_dimensions_are_an_error()
    {
        var setup = ValidSetup();
        OnlyTable(setup).Width = 0;

        Single(setup, IssueCode.TableDimensionsInvalid);
    }

    [Fact]
    public void Disabled_seat_that_does_not_exist_is_a_warning()
    {
        var setup = ValidSetup();
        OnlyTable(setup).DisabledSeats = [42];

        Assert.Equal(IssueSeverity.Warning, Single(setup, IssueCode.DisabledSeatInvalid).Severity);
    }

    [Fact]
    public void Duplicate_table_numbers_across_floors_are_an_error()
    {
        var setup = ValidSetup();
        setup.Floors.Add(new Floor { Name = "First", Number = 2, Tables = [new Table { Number = 101 }] });

        Single(setup, IssueCode.TableNumberDuplicate);
    }

    [Fact]
    public void Duplicate_floor_names_are_a_warning()
    {
        var setup = ValidSetup();
        setup.Floors.Add(new Floor { Name = "ground", Number = 2 });

        Assert.Equal(IssueSeverity.Warning, Single(setup, IssueCode.FloorNameDuplicate).Severity);
    }

    [Fact]
    public void Overlapping_tables_are_a_warning()
    {
        var setup = ValidSetup();
        setup.Floors[0].Tables.Add(new Table { Number = 102, SeatsPerSide = 5, X = 2, Y = 0.5 });

        var issue = Single(setup, IssueCode.TablesOverlap);

        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("101 and 102", issue.Message);
    }

    [Fact]
    public void Tables_placed_end_to_end_do_not_overlap()
    {
        var setup = ValidSetup();
        // Table 101 is 3 m long, centred at 0, so it ends at x = 1.5; the next one starts exactly there.
        setup.Floors[0].Tables.Add(new Table { Number = 102, SeatsPerSide = 5, X = 3 });

        Assert.DoesNotContain(SetupValidator.Validate(setup).Issues, i => i.Code == IssueCode.TablesOverlap);
    }

    [Fact]
    public void Tables_on_different_floors_never_overlap()
    {
        var setup = ValidSetup();
        setup.Floors.Add(new Floor { Name = "First", Number = 2, Tables = [new Table { Number = 201, SeatsPerSide = 5 }] });

        Assert.DoesNotContain(SetupValidator.Validate(setup).Issues, i => i.Code == IssueCode.TablesOverlap);
    }

    [Fact]
    public void Not_enough_seats_is_a_warning_with_the_shortfall()
    {
        var setup = ValidSetup();
        setup.Groups[1].PeopleCount = 9;

        var issue = Single(setup, IssueCode.NotEnoughSeats);

        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("3 seats short", issue.Message);
        Assert.False(SetupValidator.Validate(setup).HasErrors);
    }

    [Fact]
    public void Disabled_seats_reduce_capacity()
    {
        var setup = ValidSetup();
        OnlyTable(setup).DisabledSeats = [0, 1, 2];

        Single(setup, IssueCode.NotEnoughSeats);
    }

    [Fact]
    public void Pin_to_unknown_group_is_an_error()
    {
        var setup = ValidSetup();
        setup.Pins.Add(new Pin { GroupId = Guid.NewGuid(), Scope = PinScope.Table, TableId = OnlyTable(setup).Id });

        Single(setup, IssueCode.PinUnknownGroup);
    }

    [Fact]
    public void Pinning_a_group_twice_is_an_error()
    {
        var setup = ValidSetup();
        var group = setup.Groups[0];
        setup.Pins.Add(new Pin { GroupId = group.Id, Scope = PinScope.Table, TableId = OnlyTable(setup).Id });
        setup.Pins.Add(new Pin { GroupId = group.Id, Scope = PinScope.Floor, FloorId = setup.Floors[0].Id });

        Single(setup, IssueCode.PinDuplicateGroup);
    }

    [Fact]
    public void Pin_to_missing_table_or_floor_is_an_error()
    {
        var setup = ValidSetup();
        setup.Pins.Add(new Pin { GroupId = setup.Groups[0].Id, Scope = PinScope.Table, TableId = Guid.NewGuid() });
        setup.Pins.Add(new Pin { GroupId = setup.Groups[1].Id, Scope = PinScope.Floor, FloorId = Guid.NewGuid() });

        Assert.Equal(2, SetupValidator.Validate(setup).Issues.Count(i => i.Code == IssueCode.PinTargetMissing));
    }

    [Fact]
    public void Group_pinned_to_a_table_that_is_too_small_is_an_error()
    {
        var setup = ValidSetup();
        setup.Groups[0].PeopleCount = 6;
        OnlyTable(setup).DisabledSeats = [0, 1, 2, 3, 4];
        setup.Pins.Add(new Pin { GroupId = setup.Groups[0].Id, Scope = PinScope.Table, TableId = OnlyTable(setup).Id });

        Assert.Contains("table 101 (5 seats)", Single(setup, IssueCode.PinTargetTooSmall).Message);
    }

    [Fact]
    public void Group_pinned_to_a_floor_that_is_too_small_is_an_error()
    {
        var setup = ValidSetup();
        setup.Groups[0].PeopleCount = 11;
        setup.Pins.Add(new Pin { GroupId = setup.Groups[0].Id, Scope = PinScope.Floor, FloorId = setup.Floors[0].Id });

        Single(setup, IssueCode.PinTargetTooSmall);
    }

    [Fact]
    public void Seat_pin_must_match_the_group_size()
    {
        var setup = ValidSetup();
        var tableId = OnlyTable(setup).Id;
        setup.Pins.Add(new Pin { GroupId = setup.Groups[0].Id, Scope = PinScope.Seats, Seats = [new(tableId, 0), new(tableId, 1)] });

        Single(setup, IssueCode.PinSeatCountMismatch);
    }

    [Fact]
    public void Seat_pin_to_disabled_or_missing_seat_is_an_error()
    {
        var setup = ValidSetup();
        var table = OnlyTable(setup);
        table.DisabledSeats = [0];
        setup.Pins.Add(new Pin
        {
            GroupId = setup.Groups[0].Id,
            Scope = PinScope.Seats,
            Seats = [new(table.Id, 0), new(table.Id, 1), new(table.Id, 2), new(table.Id, 99)],
        });

        Single(setup, IssueCode.PinSeatInvalid);
    }

    [Fact]
    public void Seats_pinned_to_two_groups_is_an_error()
    {
        var setup = ValidSetup();
        var tableId = OnlyTable(setup).Id;
        setup.Pins.Add(new Pin { GroupId = setup.Groups[0].Id, Scope = PinScope.Seats, Seats = [new(tableId, 0), new(tableId, 1), new(tableId, 2), new(tableId, 3)] });
        setup.Pins.Add(new Pin { GroupId = setup.Groups[1].Id, Scope = PinScope.Seats, Seats = [new(tableId, 3), new(tableId, 4), new(tableId, 5), new(tableId, 6)] });

        Single(setup, IssueCode.PinSeatsOverlap);
    }

    [Fact]
    public void Pins_needing_more_seats_than_a_floor_has_is_an_error()
    {
        var setup = ValidSetup();
        setup.Groups[0].PeopleCount = 6;
        setup.Groups[1].PeopleCount = 6;
        foreach (var group in setup.Groups)
            setup.Pins.Add(new Pin { GroupId = group.Id, Scope = PinScope.Floor, FloorId = setup.Floors[0].Id });

        Single(setup, IssueCode.PinsExceedFloorCapacity);
    }

    [Fact]
    public void Valid_pins_have_no_issues()
    {
        var setup = ValidSetup();
        var tableId = OnlyTable(setup).Id;
        setup.Pins.Add(new Pin { GroupId = setup.Groups[0].Id, Scope = PinScope.Seats, Seats = [new(tableId, 0), new(tableId, 1), new(tableId, 5), new(tableId, 6)] });
        setup.Pins.Add(new Pin { GroupId = setup.Groups[1].Id, Scope = PinScope.Table, TableId = tableId });

        Assert.Empty(SetupValidator.Validate(setup).Issues);
    }
}
