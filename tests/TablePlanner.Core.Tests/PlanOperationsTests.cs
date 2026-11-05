using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class PlanOperationsTests
{
    /// <summary>One table of 10 seats; A (2) at seats 0–1, B (2) at seats 2–3.</summary>
    private static (EventProject Project, Table Table, Group A, Group B) Seated()
    {
        var table = new Table { Number = 101, SeatsPerSide = 5 };
        var a = new Group { Name = "A", PeopleCount = 2 };
        var b = new Group { Name = "B", PeopleCount = 2 };
        var project = new EventProject();
        project.Setup.Floors.Add(new Floor { Name = "Ground", Number = 1, Tables = [table] });
        project.Setup.Groups.AddRange([a, b]);
        project.CurrentAssignment.Seats.AddRange(
        [
            new(new SeatId(table.Id, 0), a.Id), new(new SeatId(table.Id, 1), a.Id),
            new(new SeatId(table.Id, 2), b.Id), new(new SeatId(table.Id, 3), b.Id),
        ]);
        return (project, table, a, b);
    }

    private static int[] SeatIndices(EventProject project, Group group) =>
        project.CurrentAssignment.SeatsOf(group.Id).Select(s => s.Index).Order().ToArray();

    [Fact]
    public void SeatGroups_moves_a_group_and_takes_the_seats_from_others()
    {
        var (project, table, a, b) = Seated();

        PlanOperations.SeatGroups(project, new Dictionary<Guid, IReadOnlyList<SeatId>> { [a.Id] = [new(table.Id, 3), new(table.Id, 4)] });

        Assert.Equal([3, 4], SeatIndices(project, a));
        Assert.Equal([2], SeatIndices(project, b));
    }

    [Fact]
    public void A_lock_follows_the_group_when_it_is_moved()
    {
        var (project, table, a, _) = Seated();
        Assert.True(PlanOperations.Lock(project, a.Id));

        PlanOperations.SeatGroups(project, new Dictionary<Guid, IReadOnlyList<SeatId>> { [a.Id] = [new(table.Id, 8), new(table.Id, 9)] });

        var pin = Assert.Single(project.Setup.Pins);
        Assert.Equal(PinScope.Seats, pin.Scope);
        Assert.Equal([8, 9], pin.Seats.Select(s => s.Index).Order());
    }

    [Fact]
    public void Lock_replaces_other_pins_and_needs_seats()
    {
        var (project, table, a, _) = Seated();
        PlanOperations.PinToTable(project, a.Id, table.Id);
        var unseated = new Group { Name = "C", PeopleCount = 1 };
        project.Setup.Groups.Add(unseated);

        Assert.True(PlanOperations.Lock(project, a.Id));
        Assert.False(PlanOperations.Lock(project, unseated.Id));
        Assert.Equal(PinScope.Seats, Assert.Single(project.Setup.Pins).Scope);
    }

    [Fact]
    public void LockAll_and_UnlockAll_keep_table_and_floor_pins()
    {
        var (project, table, a, b) = Seated();
        var c = new Group { Name = "C", PeopleCount = 2 };
        project.Setup.Groups.Add(c);
        PlanOperations.PinToFloor(project, c.Id, project.Setup.Floors[0].Id);

        Assert.Equal(2, PlanOperations.LockAll(project));
        Assert.Equal(2, PlanOperations.UnlockAll(project));

        Assert.Equal(PinScope.Floor, Assert.Single(project.Setup.Pins).Scope);
    }

    [Fact]
    public void Unseat_removes_the_group_and_its_lock()
    {
        var (project, _, a, b) = Seated();
        PlanOperations.Lock(project, a.Id);

        PlanOperations.UnseatGroup(project, a.Id);

        Assert.Empty(SeatIndices(project, a));
        Assert.Equal([2, 3], SeatIndices(project, b));
        Assert.Empty(project.Setup.Pins);
    }

    [Fact]
    public void Save_then_restore_a_plan()
    {
        var (project, _, a, _) = Seated();
        var saved = PlanOperations.SavePlan(project, " First try ", DateTimeOffset.Now, new PlanStats(10, 4, 4, 0, 0, 0));
        project.CurrentAssignment = new SeatingAssignment();

        var report = PlanOperations.RestorePlan(project, saved.Id);

        Assert.True(report.IsClean);
        Assert.Equal("First try", saved.Name);
        Assert.Equal(4, project.CurrentAssignment.Seats.Count);
        Assert.Equal([0, 1], SeatIndices(project, a));
    }

    [Fact]
    public void Saved_plans_are_copies()
    {
        var (project, _, _, _) = Seated();
        var saved = PlanOperations.SavePlan(project, "", DateTimeOffset.Now, null);

        project.CurrentAssignment.Seats.Clear();
        project.Priorities.TableSplitPenalty = 1;

        Assert.Equal("Plan 1", saved.Name);
        Assert.Equal(4, saved.Assignment.Seats.Count);
        Assert.NotEqual(1, saved.Priorities.TableSplitPenalty);
    }

    [Fact]
    public void Restore_leaves_out_seats_and_groups_that_no_longer_exist_and_reports_size_changes()
    {
        var (project, table, a, b) = Seated();
        var saved = PlanOperations.SavePlan(project, "Old", DateTimeOffset.Now, null);
        table.DisabledSeats.Add(0);
        SetupOperations.RemoveGroup(project, b.Id);

        var report = PlanOperations.RestorePlan(project, saved.Id);

        Assert.Equal(1, report.SeatsDropped);
        Assert.Equal(1, report.GroupsMissing);
        Assert.Equal(["A"], report.GroupsWithWrongCount);
        Assert.Equal([1], SeatIndices(project, a));
    }

    [Fact]
    public void Rename_and_delete_saved_plans()
    {
        var (project, _, _, _) = Seated();
        var saved = PlanOperations.SavePlan(project, "Old", DateTimeOffset.Now, null);

        PlanOperations.RenamePlan(project, saved.Id, "New");
        PlanOperations.RenamePlan(project, saved.Id, "  ");
        Assert.Equal("New", saved.Name);

        PlanOperations.DeletePlan(project, saved.Id);
        Assert.Empty(project.SavedAssignments);
    }

    [Fact]
    public void Saved_plans_and_stats_survive_saving_the_project()
    {
        var (project, _, _, _) = Seated();
        PlanOperations.SavePlan(project, "Kept", DateTimeOffset.Now, new PlanStats(12.5, 4, 4, 1, 2, 3));

        var loaded = Serialization.ProjectSerializer.Deserialize(Serialization.ProjectSerializer.Serialize(project)).Project!;

        var saved = Assert.Single(loaded.SavedAssignments);
        Assert.Equal(new PlanStats(12.5, 4, 4, 1, 2, 3), saved.Stats);
        Assert.Equal(4, saved.Assignment.Seats.Count);
    }
}
