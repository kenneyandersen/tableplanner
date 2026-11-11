using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;

namespace TablePlanner.Core.Tests;

public class GroupTreeTests
{
    /// <summary>A flat list: Ann (4), Bo (6), Cy (3), Di (5), each at the top.</summary>
    private static (EventProject Project, Group Ann, Group Bo, Group Cy, Group Di) Flat()
    {
        var project = new EventProject();
        var groups = new[] { ("Ann", 4), ("Bo", 6), ("Cy", 3), ("Di", 5) }.Select(x => new Group { Name = x.Item1, PeopleCount = x.Item2 }).ToArray();
        project.Setup.Groups.AddRange(groups);
        return (project, groups[0], groups[1], groups[2], groups[3]);
    }

    private static IEnumerable<ValidationIssue> Errors(EventProject project) => SetupValidator.Validate(project.Setup).Errors;

    [Fact]
    public void Grouping_a_flat_list_makes_a_parent_with_no_people_of_its_own()
    {
        var (project, ann, bo, cy, _) = Flat();

        var sales = SetupOperations.GroupInto(project, [bo.Id, ann.Id], "Sales");

        Assert.Equal(0, sales.PeopleCount);
        Assert.Null(sales.ParentId);
        Assert.Equal(sales.Id, ann.ParentId);
        Assert.Equal(sales.Id, bo.ParentId);
        Assert.Null(cy.ParentId);
        Assert.Equal(10, project.Setup.PeopleIn(sales));
        Assert.Equal(0, project.Setup.IndexOf(sales)); // where the first of them was
        Assert.Equal("Sales › Ann", project.Setup.PathOf(ann));
        Assert.Equal(2, project.Setup.LevelOf(ann));
        Assert.Empty(Errors(project));
    }

    [Fact]
    public void Grouping_subgroups_of_one_parent_makes_the_new_group_a_level_below_it()
    {
        var (project, ann, bo, cy, _) = Flat();
        var sales = SetupOperations.GroupInto(project, [ann.Id, bo.Id, cy.Id], "Sales");

        var north = SetupOperations.GroupInto(project, [ann.Id, bo.Id], "North");

        Assert.Equal(sales.Id, north.ParentId);
        Assert.Equal(3, project.Setup.LevelOf(ann));
        Assert.Equal(sales, project.Setup.TopOf(ann));
        Assert.Equal(north, project.Setup.AtLevel(ann, 2));
        Assert.Equal([ann, bo, cy], project.Setup.BottomGroupsOf(sales).OrderBy(g => g.Name));
        Assert.Equal(13, project.Setup.PeopleIn(sales));
        // A fourth level is refused.
        Assert.NotNull(SetupOperations.CannotGroup(project, [ann.Id]));
        Assert.Throws<InvalidOperationException>(() => SetupOperations.GroupInto(project, [ann.Id], "Too deep"));
    }

    [Fact]
    public void Ungrouping_moves_the_subgroups_up_and_drops_preferences_for_the_parent()
    {
        var (project, ann, bo, cy, _) = Flat();
        var sales = SetupOperations.GroupInto(project, [ann.Id, bo.Id], "Sales");
        cy.PreferredGroupIds = [sales.Id];

        SetupOperations.Ungroup(project, sales.Id);

        Assert.Null(project.Setup.FindGroup(sales.Id));
        Assert.Null(ann.ParentId);
        Assert.Null(bo.ParentId);
        Assert.Empty(cy.PreferredGroupIds);
    }

    [Fact]
    public void Removing_a_parent_keeps_its_subgroups()
    {
        var (project, ann, bo, _, _) = Flat();
        var sales = SetupOperations.GroupInto(project, [ann.Id, bo.Id], "Sales");
        var all = SetupOperations.GroupInto(project, [sales.Id], "Company");

        SetupOperations.RemoveGroup(project, sales.Id);

        Assert.Equal(all.Id, ann.ParentId);
        Assert.Equal(all.Id, bo.ParentId);
    }

    [Fact]
    public void Moving_a_group_under_a_bottom_group_moves_its_people_seats_and_pins_to_an_other_group()
    {
        var (project, ann, bo, _, _) = Flat();
        var table = new Table { Number = 1, SeatsPerSide = 5 };
        project.Setup.Floors.Add(new Floor { Name = "F", Number = 1, Tables = [table] });
        project.CurrentAssignment.Seats.AddRange(Enumerable.Range(0, 4).Select(i => new SeatAssignment(new SeatId(table.Id, i), ann.Id)));
        PlanOperations.Lock(project, ann.Id);
        ann.DietaryInfo = "2 vegan";

        SetupOperations.SetParent(project, bo.Id, ann.Id);

        var other = project.Setup.ChildrenOf(ann.Id).Single(g => g != bo);
        Assert.Equal("Ann – other", other.Name);
        Assert.Equal(4, other.PeopleCount);
        Assert.Equal("2 vegan", other.DietaryInfo);
        Assert.Equal(0, ann.PeopleCount);
        Assert.Equal(10, project.Setup.PeopleIn(ann));
        Assert.All(project.CurrentAssignment.Seats, s => Assert.Equal(other.Id, s.GroupId));
        Assert.Equal(other.Id, Assert.Single(project.Setup.Pins).GroupId);
        Assert.Empty(Errors(project));
    }

    [Fact]
    public void A_group_cannot_go_inside_itself_or_too_deep()
    {
        var (project, ann, bo, cy, di) = Flat();
        var sales = SetupOperations.GroupInto(project, [ann.Id, bo.Id], "Sales");
        var north = SetupOperations.GroupInto(project, [ann.Id], "North");

        Assert.False(project.Setup.CanMoveUnder(sales.Id, north.Id)); // a loop
        Assert.False(project.Setup.CanMoveUnder(sales.Id, sales.Id));
        Assert.True(project.Setup.CanMoveUnder(cy.Id, north.Id)); // level 3
        SetupOperations.SetParent(project, cy.Id, north.Id);
        Assert.False(project.Setup.CanMoveUnder(di.Id, cy.Id)); // level 4
        Assert.Throws<InvalidOperationException>(() => SetupOperations.SetParent(project, di.Id, cy.Id));
        SetupOperations.SetParent(project, cy.Id, null);
        Assert.Null(cy.ParentId);
    }

    [Fact]
    public void Tree_order_lists_each_group_followed_by_its_subgroups()
    {
        var (project, ann, bo, cy, di) = Flat();
        var sales = SetupOperations.GroupInto(project, [bo.Id, di.Id], "Sales");

        var order = project.Setup.InTreeOrder();

        Assert.Equal([(ann, 1), (sales, 1), (bo, 2), (di, 2), (cy, 1)], order);
        Assert.True(project.Setup.HasLevels());
    }

    [Fact]
    public void Validation_finds_people_on_a_parent_unknown_parents_and_loops()
    {
        var (project, ann, bo, cy, di) = Flat();
        bo.ParentId = ann.Id; // Ann has 4 people of its own
        cy.ParentId = Guid.NewGuid();
        di.ParentId = di.Id;

        var codes = Errors(project).Select(e => e.Code).ToList();

        Assert.Contains(IssueCode.GroupParentHasPeople, codes);
        Assert.Contains(IssueCode.GroupParentUnknown, codes);
        Assert.Contains(IssueCode.GroupLevelsInvalid, codes);
    }

    [Fact]
    public void A_floor_pin_on_a_parent_counts_its_subgroups_and_a_seat_lock_on_it_locks_them()
    {
        var (project, ann, bo, _, _) = Flat();
        var small = new Table { Number = 1, SeatsPerSide = 4 };
        project.Setup.Floors.Add(new Floor { Name = "F", Number = 1, Tables = [small] });
        var sales = SetupOperations.GroupInto(project, [ann.Id, bo.Id], "Sales");
        project.CurrentAssignment.Seats.AddRange(Enumerable.Range(0, 4).Select(i => new SeatAssignment(new SeatId(small.Id, i), ann.Id)));

        PlanOperations.PinToFloor(project, sales.Id, project.Setup.Floors[0].Id);
        Assert.Contains(Errors(project), e => e.Code == IssueCode.PinTargetTooSmall); // 10 people, 8 seats

        Assert.True(PlanOperations.Lock(project, sales.Id));
        Assert.Contains(project.Setup.Pins, p => p.GroupId == ann.Id && p.Scope == PinScope.Seats);
        Assert.DoesNotContain(project.Setup.Pins, p => p.GroupId == sales.Id && p.Scope == PinScope.Seats);
    }
}

internal static class SetupTestExtensions
{
    public static int IndexOf(this EventSetup setup, Group group) => setup.Groups.IndexOf(group);
}

public class GroupTreeSerializationTests
{
    [Fact]
    public void The_parent_survives_saving_and_loading()
    {
        var project = new EventProject();
        var team = new Group { Name = "Team", PeopleCount = 3 };
        project.Setup.Groups.Add(team);
        var sales = SetupOperations.GroupInto(project, [team.Id], "Sales");

        var loaded = Serialization.ProjectSerializer.Deserialize(Serialization.ProjectSerializer.Serialize(project)).Project!;

        Assert.Equal(sales.Id, loaded.Setup.FindGroup(team.Id)!.ParentId);
        Assert.Null(loaded.Setup.FindGroup(sales.Id)!.ParentId);
    }

    [Fact]
    public void Levels_have_default_names_until_named()
    {
        var setup = new EventSetup();

        Assert.Equal("Main groups", setup.LevelName(1));
        Assert.Equal("Subgroups", setup.LevelName(2));
        Assert.Equal("Sub-subgroups", setup.LevelName(3));
    }

    [Fact]
    public void A_level_is_named_and_a_blank_name_goes_back_to_the_default()
    {
        var project = new EventProject();

        SetupOperations.SetLevelName(project, 2, " Teams ");
        Assert.Equal(["", "Teams"], project.Setup.LevelNames);
        Assert.Equal("Main groups", project.Setup.LevelName(1));
        Assert.Equal("Teams", project.Setup.LevelName(2));

        SetupOperations.SetLevelName(project, 2, "");
        Assert.Empty(project.Setup.LevelNames);
        Assert.Equal("Subgroups", project.Setup.LevelName(2));
    }
}
