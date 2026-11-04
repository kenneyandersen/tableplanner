using System.Text.Json;
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
internal sealed partial class ProjectJsonContext : JsonSerializerContext;

public sealed record ProjectLoadResult(EventProject? Project, string? Error)
{
    public bool Success => Project is not null;
}

/// <summary>Reads and writes project files (JSON with a schema version).</summary>
public static class ProjectSerializer
{
    public static string Serialize(EventProject project) =>
        JsonSerializer.Serialize(project, ProjectJsonContext.Default.EventProject);

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
            return project is null ? Fail("The project file is empty.") : new ProjectLoadResult(project, null);
        }
        catch (JsonException ex)
        {
            return Fail($"The project file is invalid: {ex.Message}");
        }
    }

    private static ProjectLoadResult Fail(string error) => new(null, error);
}
