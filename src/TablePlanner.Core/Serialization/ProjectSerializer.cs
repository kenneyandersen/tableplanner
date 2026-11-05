using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Serialization;

/// <summary>Source-generated JSON metadata, so serialization works with trimming in WebAssembly.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true,
    RespectNullableAnnotations = true)]
[JsonSerializable(typeof(EventProject))]
[JsonSerializable(typeof(ProjectFileImages))]
[JsonSerializable(typeof(List<ProjectImage>))]
internal sealed partial class ProjectJsonContext : JsonSerializerContext;

/// <summary>The extra part of a project file that holds its images.</summary>
internal sealed record ProjectFileImages(List<ProjectImage>? Images);

public sealed record ProjectLoadResult(EventProject? Project, string? Error)
{
    public bool Success => Project is not null;

    /// <summary>The images that came with a project file (see <see cref="ProjectSerializer.SerializeFile"/>).</summary>
    public IReadOnlyList<ProjectImage> Images { get; init; } = [];
}

/// <summary>Reads and writes project files (JSON with a schema version).</summary>
public static class ProjectSerializer
{
    /// <summary>The project without its images: for browser storage and undo snapshots.</summary>
    public static string Serialize(EventProject project) =>
        JsonSerializer.Serialize(project, ProjectJsonContext.Default.EventProject);

    /// <summary>
    /// A project file to export: the project plus the images its floors use, as data URLs in an extra
    /// <c>images</c> property, so one file holds everything.
    /// </summary>
    public static string SerializeFile(EventProject project, IEnumerable<ProjectImage> images)
    {
        var node = JsonSerializer.SerializeToNode(project, ProjectJsonContext.Default.EventProject)!.AsObject();
        var used = Editing.BackgroundOperations.ImagesInUse(project);
        var list = images.Where(i => used.Contains(i.Id)).DistinctBy(i => i.Id).ToList();
        if (list.Count > 0)
            node["images"] = JsonSerializer.SerializeToNode(list, ProjectJsonContext.Default.ListProjectImage);
        return node.ToJsonString(ProjectJsonContext.Default.Options);
    }

    /// <summary>Parses a project file. Never throws for bad input; returns an error message instead.</summary>
    public static ProjectLoadResult Deserialize(string json)
    {
        try
        {
            using (var document = JsonDocument.Parse(json))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("schemaVersion", out var version)
                    || version.ValueKind != JsonValueKind.Number)
                {
                    return Fail("This is not a Table Planner project file.");
                }

                if (version.GetInt32() > EventProject.CurrentSchemaVersion)
                    return Fail("This project was saved by a newer version of Table Planner.");
            }

            // Version 1 is the first format, so there is nothing to migrate yet.
            var project = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.EventProject);
            if (project is null)
                return Fail("The project file is empty.");
            // Only pictures are accepted as images; anything else is left out.
            var images = (JsonSerializer.Deserialize(json, ProjectJsonContext.Default.ProjectFileImages)?.Images ?? [])
                .Where(i => i.DataUrl.StartsWith("data:image/", StringComparison.Ordinal))
                .ToList();
            return new ProjectLoadResult(project, null) { Images = images };
        }
        catch (JsonException ex)
        {
            return Fail($"The project file is invalid: {ex.Message}");
        }
    }

    private static ProjectLoadResult Fail(string error) => new(null, error);
}
