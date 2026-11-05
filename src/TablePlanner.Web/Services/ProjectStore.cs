using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;
using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Web.Services;

/// <summary>An entry in the browser's project list.</summary>
public sealed record ProjectSummary(Guid Id, string Name, DateTimeOffset UpdatedAt, DateTimeOffset? LastExportedAt, int Groups, int People);

[JsonSerializable(typeof(List<ProjectSummary>))]
internal sealed partial class StoreJsonContext : JsonSerializerContext;

/// <summary>
/// Keeps projects in the browser's localStorage: one entry per project plus an index for the project list.
/// Their images are in <see cref="ImageStore"/>.
/// </summary>
public sealed class ProjectStore(IJSRuntime js, ImageStore images)
{
    private const string IndexKey = "tableplanner:index";

    private static string ProjectKey(Guid id) => $"tableplanner:project:{id}";

    public async Task<List<ProjectSummary>> ListAsync()
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", IndexKey);
        if (string.IsNullOrEmpty(json))
            return [];
        try
        {
            return JsonSerializer.Deserialize(json, StoreJsonContext.Default.ListProjectSummary) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task<ProjectSummary?> GetSummaryAsync(Guid id) => (await ListAsync()).FirstOrDefault(p => p.Id == id);

    public async Task<EventProject?> LoadAsync(Guid id)
    {
        var json = await js.InvokeAsync<string?>("localStorage.getItem", ProjectKey(id));
        return json is null ? null : ProjectSerializer.Deserialize(json).Project;
    }

    /// <summary>
    /// Saves a project and updates the index. <paramref name="updatedAt"/> is when its content last changed.
    /// Throws <see cref="JSException"/> when browser storage is full.
    /// </summary>
    public async Task SaveAsync(EventProject project, DateTimeOffset updatedAt)
    {
        await js.InvokeVoidAsync("localStorage.setItem", ProjectKey(project.Id), ProjectSerializer.Serialize(project));

        var index = await ListAsync();
        index.RemoveAll(p => p.Id == project.Id);
        index.Add(new ProjectSummary(project.Id, project.Name, updatedAt, project.LastExportedAt,
            project.Setup.Groups.Count, project.Setup.Groups.Sum(g => Math.Max(0, g.PeopleCount))));
        await WriteIndexAsync(index);
    }

    public async Task DeleteAsync(Guid id)
    {
        await js.InvokeVoidAsync("localStorage.removeItem", ProjectKey(id));
        await images.DeleteAllAsync(id);
        var index = await ListAsync();
        index.RemoveAll(p => p.Id == id);
        await WriteIndexAsync(index);
    }

    private Task WriteIndexAsync(List<ProjectSummary> index) =>
        js.InvokeVoidAsync("localStorage.setItem", IndexKey, JsonSerializer.Serialize(index, StoreJsonContext.Default.ListProjectSummary)).AsTask();
}
