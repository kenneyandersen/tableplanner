using Microsoft.JSInterop;
using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Web.Services;

/// <summary>
/// The project that is open in the app. Every edit goes through <see cref="History"/> (for undo/redo)
/// and is autosaved to browser storage shortly after.
/// </summary>
public sealed class ProjectSession(ProjectStore store, ImageStore imageStore, IJSRuntime js)
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(400);

    private CancellationTokenSource? _pendingSave;
    private readonly Dictionary<Guid, string> _images = [];

    public EditHistory? History { get; private set; }

    public EventProject? Project => History?.Project;

    /// <summary>When the open project was last changed, for the "exported?" reminder.</summary>
    public DateTimeOffset? LastChangedAt { get; private set; }

    /// <summary>Set when the last save to browser storage failed (e.g. storage full).</summary>
    public string? SaveError { get; private set; }

    /// <summary>The data URL of one of the open project's images, or null when it is not available.</summary>
    public string? ImageUrl(Guid? imageId) => imageId is { } id ? _images.GetValueOrDefault(id) : null;

    /// <summary>All loaded images of the open project.</summary>
    public IReadOnlyDictionary<Guid, string> Images => _images;

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
        await LoadImagesAsync(project);
        return true;
    }

    /// <summary>Stores a new or imported project (with the images from its file) and opens it.</summary>
    public async Task OpenNewAsync(EventProject project, IReadOnlyList<ProjectImage>? images = null)
    {
        var now = DateTimeOffset.Now;
        await store.SaveAsync(project, now);
        await imageStore.DeleteAllAsync(project.Id);
        foreach (var image in images ?? [])
            await imageStore.SaveAsync(project.Id, image);
        Attach(project, now);
        await LoadImagesAsync(project);
    }

    /// <summary>Stores an uploaded picture with the open project and returns its ID, for a floor background.</summary>
    public async Task<Guid> AddImageAsync(string dataUrl)
    {
        var image = new ProjectImage(Guid.NewGuid(), dataUrl);
        await imageStore.SaveAsync(Project!.Id, image);
        _images[image.Id] = image.DataUrl;
        return image.Id;
    }

    /// <summary>
    /// Loads the images the project uses and deletes the ones it no longer uses. This happens only when a project
    /// is opened: during editing, undo may bring back a removed background, so its image is kept until then.
    /// </summary>
    private async Task LoadImagesAsync(EventProject project)
    {
        var used = BackgroundOperations.ImagesInUse(project);
        try
        {
            foreach (var id in await imageStore.ListAsync(project.Id))
            {
                if (!used.Contains(id))
                    await imageStore.DeleteAsync(project.Id, id);
                else if (await imageStore.LoadAsync(project.Id, id) is { } image)
                    _images[id] = image.DataUrl;
            }
        }
        catch (JSException ex)
        {
            SaveError = $"Could not read the floor images from the browser: {ex.Message}";
        }
        Changed?.Invoke();
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
        var fileName = $"{Formatting.FileName(Project.Name)}.tableplanner.json";
        await js.InvokeVoidAsync("tablePlanner.downloadFile", fileName, "application/json", ProjectSerializer.SerializeFile(Project, _images.Select(i => new ProjectImage(i.Key, i.Value))));
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
        _images.Clear();
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
}
