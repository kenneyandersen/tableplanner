using System.Globalization;
using TablePlanner.Core.Model;

namespace TablePlanner.Core;

/// <summary>The colour each group is drawn in: its own colour if set, otherwise one picked automatically.</summary>
public static class GroupColours
{
    /// <summary>Colours by group ID, as "#rrggbb". Automatic colours are spread around the colour wheel in group order, so neighbours in the list differ.</summary>
    public static IReadOnlyDictionary<Guid, string> For(EventSetup setup)
    {
        var colours = new Dictionary<Guid, string>();
        var automatic = 0;
        foreach (var group in setup.Groups)
            colours[group.Id] = group.Colour ?? Automatic(automatic++);
        return colours;
    }

    /// <summary>The n-th automatic colour: hues stepped by the golden angle, light enough for dark text.</summary>
    public static string Automatic(int n)
    {
        var hue = n * 137.508 % 360;
        var lightness = n % 2 == 0 ? 0.68 : 0.58;
        return FromHsl(hue, 0.6, lightness);
    }

    private static string FromHsl(double hue, double saturation, double lightness)
    {
        var c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = lightness - c / 2;
        var (r, g, b) = (int)(hue / 60) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static string Hex(double v) => ((int)Math.Round((v) * 255)).ToString("x2", CultureInfo.InvariantCulture);
        return $"#{Hex(r + m)}{Hex(g + m)}{Hex(b + m)}";
    }
}
