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
}
