using Microsoft.JSInterop;
using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Web.Services;

/// <summary>
/// The project that is open in the app. Every edit goes through <see cref="History"/> (for undo/redo)
/// and is autosaved to browser storage shortly after.
/// </summary>
public sealed class ProjectSession(ProjectStore store, IJSRuntime js)
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(400);

    private CancellationTokenSource? _pendingSave;

    public EditHistory? History { get; private set; }

    public EventProject? Project => History?.Project;

    /// <summary>When the open project was last changed, for the "exported?" reminder.</summary>
    public DateTimeOffset? LastChangedAt { get; private set; }

    /// <summary>Set when the last save to browser storage failed (e.g. storage full).</summary>
    public string? SaveError { get; private set; }

    /// <summary>Raised when the open project, its content or its save state changes.</summary>
    public event Action? Changed;

    public bool HasChangesSinceExport =>
        Project is not null && (Project.LastExportedAt is null || LastChangedAt > Project.LastExportedAt);

    /// <summary>Opens a stored project. Returns false when it does not exist.</summary>
    public async Task<bool> OpenAsync(Guid id)
    {
        if (Project?.Id == id)
            return true;

        var project = await store.LoadAsync(id);
        if (project is null)
            return false;

        Attach(project, (await store.GetSummaryAsync(id))?.UpdatedAt);
        return true;
    }

    /// <summary>Stores a new or imported project and opens it.</summary>
    public async Task OpenNewAsync(EventProject project)
    {
        var now = DateTimeOffset.Now;
        await store.SaveAsync(project, now);
        Attach(project, now);
    }

    public void Close()
    {
        Detach();
        Changed?.Invoke();
    }

    /// <summary>Downloads the open project as a file and remembers when it was exported.</summary>
    public async Task ExportAsync()
    {
        if (Project is null)
            return;

        Project.LastExportedAt = DateTimeOffset.Now;
        var fileName = $"{SafeFileName(Project.Name)}.tableplanner.json";
        await js.InvokeVoidAsync("tablePlanner.downloadFile", fileName, "application/json", ProjectSerializer.Serialize(Project));
        await SaveNowAsync();
    }

    private void Attach(EventProject project, DateTimeOffset? lastChangedAt)
    {
        Detach();
        History = new EditHistory(project);
        History.Changed += OnEdited;
        LastChangedAt = lastChangedAt;
        SaveError = null;
        Changed?.Invoke();
    }

    private void Detach()
    {
        _pendingSave?.Cancel();
        if (History is not null)
            History.Changed -= OnEdited;
        History = null;
    }

    private void OnEdited()
    {
        LastChangedAt = DateTimeOffset.Now;
        Changed?.Invoke();
        _ = SaveSoonAsync();
    }

    private async Task SaveSoonAsync()
    {
        _pendingSave?.Cancel();
        var cts = _pendingSave = new CancellationTokenSource();
        try
        {
            await Task.Delay(AutosaveDelay, cts.Token);
            await SaveNowAsync();
        }
        catch (TaskCanceledException)
        {
            // A newer edit has scheduled its own save.
        }
    }

    private async Task SaveNowAsync()
    {
        if (Project is null)
            return;
        try
        {
            await store.SaveAsync(Project, LastChangedAt ?? DateTimeOffset.Now);
            SaveError = null;
        }
        catch (JSException ex)
        {
            SaveError = $"Could not save in the browser: {ex.Message}. Export the project to keep your changes.";
        }
        Changed?.Invoke();
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "project" : safe;
    }
}
