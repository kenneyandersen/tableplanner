using System.Globalization;
using System.Reflection;

namespace TablePlanner.Web;

/// <summary>
/// Which build of the app is running: the version and commit set by the deploy workflow (e.g. "2026.11.12.42",
/// the date and the workflow's run number), or a development build. Read from the assembly's metadata.
/// </summary>
public static class BuildInfo
{
    private static readonly Dictionary<string, string?> Metadata = typeof(BuildInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .GroupBy(a => a.Key)
        .ToDictionary(g => g.Key, g => g.First().Value);

    private static string? Get(string key) => Metadata.GetValueOrDefault(key) is { Length: > 0 } value ? value : null;

    /// <summary>Set by the deploy workflow; null for a development build.</summary>
    public static string? Version => Get("BuildVersion");

    public static string? Commit => Get("BuildCommit");

    public static DateTime? BuildTime =>
        DateTime.TryParse(Get("BuildTime"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
            ? time : null;

    /// <summary>E.g. "Version 2026.11.12.42 · abc1234", or "Development build" for a local one.</summary>
    public static string Text => Version is { } version
        ? Commit is { } commit ? $"Version {version} · {commit[..Math.Min(7, commit.Length)]}" : $"Version {version}"
        : "Development build";

    /// <summary>When it was built (in the viewer's time zone), and the full commit.</summary>
    public static string Details => string.Join(" · ", new[]
    {
        BuildTime is { } time ? $"Built {time.ToLocalTime():yyyy-MM-dd HH:mm}" : null,
        Commit is { } commit ? $"commit {commit}" : null,
    }.Where(x => x is not null));
}
