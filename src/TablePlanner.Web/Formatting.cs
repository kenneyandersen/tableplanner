using System.Globalization;

namespace TablePlanner.Web;

public static class Formatting
{
    /// <summary>A number for SVG attributes. Always uses '.', whatever the browser's language.</summary>
    public static string Svg(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>How long ago, e.g. "5 min ago".</summary>
    public static string Ago(DateTimeOffset time)
    {
        var age = DateTimeOffset.Now - time;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} min ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} h ago"
            : $"{(int)age.TotalDays} days ago";
    }

    /// <summary>"1st", "2nd", "3rd", "4th", …</summary>
    public static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };

    /// <summary>A name made safe to use in a file name. Empty names become <paramref name="fallback"/>.</summary>
    public static string FileName(string name, string fallback = "project")
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? fallback : safe;
    }
}
