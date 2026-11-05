using Microsoft.JSInterop;
using TablePlanner.Core.Model;

namespace TablePlanner.Web.Services;

/// <summary>An uploaded picture after it was downscaled in the browser.</summary>
public sealed record PreparedImage(string DataUrl, int Width, int Height);

/// <summary>
/// Keeps project images (floor backgrounds) in the browser's IndexedDB, which has room for them unlike localStorage.
/// Images are stored per project, so deleting a project deletes its images.
/// </summary>
public sealed class ImageStore(IJSRuntime js)
{
    /// <summary>Uploaded pictures are made at most this many pixels on their longest side.</summary>
    public const int MaxImageSize = 2000;

    private static string Prefix(Guid projectId) => $"{projectId}/";

    /// <summary>Downscales an uploaded picture to at most <paramref name="maxSize"/> pixels on its longest side.</summary>
    public Task<PreparedImage> PrepareAsync(byte[] bytes, int maxSize = MaxImageSize) =>
        js.InvokeAsync<PreparedImage>("tablePlanner.prepareImage", bytes, maxSize).AsTask();

    public Task SaveAsync(Guid projectId, ProjectImage image) =>
        js.InvokeVoidAsync("tablePlanner.images.put", Prefix(projectId) + image.Id, image.DataUrl).AsTask();

    public async Task<ProjectImage?> LoadAsync(Guid projectId, Guid imageId) =>
        await js.InvokeAsync<string?>("tablePlanner.images.get", Prefix(projectId) + imageId) is { } dataUrl ? new ProjectImage(imageId, dataUrl) : null;

    public async Task<List<Guid>> ListAsync(Guid projectId)
    {
        var keys = await js.InvokeAsync<string[]>("tablePlanner.images.keys", Prefix(projectId));
        return keys.Select(k => Guid.TryParse(k[Prefix(projectId).Length..], out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
    }

    public Task DeleteAsync(Guid projectId, Guid imageId) =>
        js.InvokeVoidAsync("tablePlanner.images.remove", Prefix(projectId) + imageId).AsTask();

    public Task DeleteAllAsync(Guid projectId) => js.InvokeVoidAsync("tablePlanner.images.removeAll", Prefix(projectId)).AsTask();
}
