using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class EditHistoryTests
{
    private static (EditHistory History, Group Group) NewHistory()
    {
        var project = new EventProject();
        var group = SetupOperations.AddGroup(project, "A", 4);
        return (new EditHistory(project), group);
    }

    [Fact]
    public void Undo_and_redo_restore_the_state_before_and_after_an_edit()
    {
        var (history, group) = NewHistory();

        history.Execute("Rename", p => p.Setup.FindGroup(group.Id)!.Name = "B");
        Assert.Equal("B", history.Project.Setup.FindGroup(group.Id)!.Name);

        history.Undo();
        Assert.Equal("A", history.Project.Setup.FindGroup(group.Id)!.Name);

        history.Redo();
        Assert.Equal("B", history.Project.Setup.FindGroup(group.Id)!.Name);
    }

    [Fact]
    public void Undo_restores_the_guest_page_address()
    {
        var (history, _) = NewHistory();
        history.Execute("Set address", p => p.GuestPageUrl = "https://example.org/");

        history.Undo();

        Assert.Null(history.Project.GuestPageUrl);
    }

    [Fact]
    public void Undo_restores_every_project_property_except_the_export_time()
    {
        // When a property is added to EventProject, add it to EditHistory.Restore and then to this list.
        string[] restored = ["Name", "GuestPageUrl", "Setup", "Priorities", "CurrentAssignment", "SavedAssignments"];
        string[] notRestored = ["SchemaVersion", "Id", "LastExportedAt"];

        var properties = typeof(EventProject).GetProperties().Where(p => p.CanWrite).Select(p => p.Name);

        Assert.Equal(restored.Concat(notRestored).Order(), properties.Order());
    }

    [Fact]
    public void Undo_keeps_the_same_project_instance()
    {
        var (history, group) = NewHistory();
        var project = history.Project;

        history.Execute("Rename", p => p.Setup.FindGroup(group.Id)!.Name = "B");
        history.Undo();

        Assert.Same(project, history.Project);
    }

    [Fact]
    public void A_new_edit_clears_redo()
    {
        var (history, group) = NewHistory();
        history.Execute("Rename", p => p.Setup.FindGroup(group.Id)!.Name = "B");
        history.Undo();

        history.Execute("Resize", p => p.Setup.FindGroup(group.Id)!.PeopleCount = 6);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Edits_that_change_nothing_are_not_recorded()
    {
        var (history, group) = NewHistory();

        history.Execute("Rename", p => p.Setup.FindGroup(group.Id)!.Name = "A");

        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Descriptions_describe_the_next_undo_and_redo()
    {
        var (history, group) = NewHistory();
        history.Execute("Rename group", p => p.Setup.FindGroup(group.Id)!.Name = "B");

        Assert.Equal("Rename group", history.UndoDescription);
        history.Undo();
        Assert.Equal("Rename group", history.RedoDescription);
        Assert.Null(history.UndoDescription);
    }

    [Fact]
    public void History_is_limited()
    {
        var project = new EventProject();
        var history = new EditHistory(project) { Limit = 3 };

        for (var i = 0; i < 5; i++)
            history.Execute($"Add {i}", p => SetupOperations.AddGroup(p));

        var undos = 0;
        while (history.CanUndo)
        {
            history.Undo();
            undos++;
        }
        Assert.Equal(3, undos);
        Assert.Equal(2, project.Setup.Groups.Count);
    }

    [Fact]
    public void Changed_is_raised_for_edits_undo_and_redo()
    {
        var (history, group) = NewHistory();
        var count = 0;
        history.Changed += () => count++;

        history.Execute("Rename", p => p.Setup.FindGroup(group.Id)!.Name = "B");
        history.Undo();
        history.Redo();

        Assert.Equal(3, count);
    }

    [Fact]
    public void Pending_edit_is_recorded_as_one_step()
    {
        var (history, group) = NewHistory();

        var edit = history.BeginEdit();
        for (var i = 5; i <= 9; i++)
            history.Project.Setup.FindGroup(group.Id)!.PeopleCount = i;
        edit.Commit("Change size");

        Assert.Equal("Change size", history.UndoDescription);
        history.Undo();
        Assert.Equal(4, history.Project.Setup.FindGroup(group.Id)!.PeopleCount);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Cancelled_pending_edit_reverts_and_records_nothing()
    {
        var (history, group) = NewHistory();

        var edit = history.BeginEdit();
        history.Project.Setup.FindGroup(group.Id)!.PeopleCount = 9;
        edit.Cancel();

        Assert.Equal(4, history.Project.Setup.FindGroup(group.Id)!.PeopleCount);
        Assert.False(history.CanUndo);
    }
}
