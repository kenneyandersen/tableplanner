using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Core.Editing;

/// <summary>
/// Undo/redo for a project. Each edit stores a snapshot of the project from before the edit,
/// so any change made through <see cref="Execute"/> or <see cref="BeginEdit"/> can be undone.
/// Undo restores the content into the same <see cref="Project"/> instance; callers should refer to
/// floors, tables and groups by ID rather than holding on to the objects.
/// </summary>
public sealed class EditHistory
{
    private readonly List<(string Description, string Snapshot)> _undo = [];
    private readonly List<(string Description, string Snapshot)> _redo = [];

    public EditHistory(EventProject project) => Project = project;

    public EventProject Project { get; }

    public int Limit { get; init; } = 100;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoDescription => CanUndo ? _undo[^1].Description : null;
    public string? RedoDescription => CanRedo ? _redo[^1].Description : null;

    /// <summary>Raised after every edit, undo and redo.</summary>
    public event Action? Changed;

    /// <summary>Applies an edit and records it, so it can be undone.</summary>
    public void Execute(string description, Action<EventProject> edit)
    {
        var before = ProjectSerializer.Serialize(Project);
        edit(Project);
        Record(description, before);
    }

    /// <summary>
    /// Starts an edit that is applied step by step (e.g. dragging). Change the project freely,
    /// then call <see cref="PendingEdit.Commit"/> to record it as one undo step, or <see cref="PendingEdit.Cancel"/>.
    /// </summary>
    public PendingEdit BeginEdit() => new(this, ProjectSerializer.Serialize(Project));

    public void Undo()
    {
        if (!CanUndo) return;
        var (description, snapshot) = Pop(_undo);
        _redo.Add((description, ProjectSerializer.Serialize(Project)));
        Restore(snapshot);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var (description, snapshot) = Pop(_redo);
        _undo.Add((description, ProjectSerializer.Serialize(Project)));
        Restore(snapshot);
    }

    internal void Record(string description, string before)
    {
        if (before == ProjectSerializer.Serialize(Project))
            return; // Nothing changed; don't add an empty undo step.

        _undo.Add((description, before));
        if (_undo.Count > Limit)
            _undo.RemoveAt(0);
        _redo.Clear();
        Changed?.Invoke();
    }

    internal void Restore(string snapshot)
    {
        var saved = ProjectSerializer.Deserialize(snapshot).Project
                    ?? throw new InvalidOperationException("Undo snapshot could not be read.");
        // LastExportedAt is not part of the undo history: undoing an edit doesn't un-export the file.
        // A new project property must be added here (EditHistoryTests checks the list).
        Project.Name = saved.Name;
        Project.GuestPageUrl = saved.GuestPageUrl;
        Project.Setup = saved.Setup;
        Project.Priorities = saved.Priorities;
        Project.CurrentAssignment = saved.CurrentAssignment;
        Project.SavedAssignments = saved.SavedAssignments;
        Changed?.Invoke();
    }

    private static (string, string) Pop(List<(string, string)> stack)
    {
        var item = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        return item;
    }
}

/// <summary>An edit in progress, started with <see cref="EditHistory.BeginEdit"/>.</summary>
public sealed class PendingEdit
{
    private readonly EditHistory _history;
    private readonly string _before;
    private bool _done;

    internal PendingEdit(EditHistory history, string before)
    {
        _history = history;
        _before = before;
    }

    /// <summary>Records the changes made since the edit began as one undo step.</summary>
    public void Commit(string description)
    {
        if (_done) return;
        _done = true;
        _history.Record(description, _before);
    }

    /// <summary>Reverts the changes made since the edit began.</summary>
    public void Cancel()
    {
        if (_done) return;
        _done = true;
        _history.Restore(_before);
    }
}
