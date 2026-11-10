using System.Globalization;
using System.Text;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>
/// Drawing walls: thin dark lines between their corners, with a gap where an entrance or exit element sits on them (a
/// door). The same SVG for the layout editor, the plan page, and the poster and guest page. Also the geometry the editor
/// needs: bounds, lengths, and how a new corner snaps while drawing.
/// </summary>
public static class WallDrawing
{
    public const string Colour = "#3b3f46";
    public const double Thickness = 0.12;

    /// <summary>The corners of a wall as SVG "points".</summary>
    public static string Points(Wall wall) => string.Join(" ", wall.Points.Select(p => $"{N(p.X)},{N(p.Y)}"));

    /// <summary>
    /// The floor's walls as SVG: a mask that cuts out entrance and exit elements (doors in a wall), and the walls.
    /// <paramref name="id"/> keeps the mask's ID unique when several floors are on one page. Empty without walls.
    /// </summary>
    public static string Svg(Floor floor, string id)
    {
        if (floor.Walls.Count == 0)
            return "";
        var svg = new StringBuilder();
        var doors = floor.Elements.Where(e => e.Kind is FloorElementKind.Entrance or FloorElementKind.Exit).Select(FloorElementGeometry.GetBounds).ToList();
        var mask = "";
        if (doors.Count > 0)
        {
            var b = Bounds(floor.Walls);
            var maskId = $"wall-gaps-{id}";
            svg.Append($"<defs><mask id=\"{maskId}\" maskUnits=\"userSpaceOnUse\" x=\"{N(b.Left - 1)}\" y=\"{N(b.Top - 1)}\" width=\"{N(b.Width + 2)}\" height=\"{N(b.Height + 2)}\">");
            svg.Append($"<rect x=\"{N(b.Left - 1)}\" y=\"{N(b.Top - 1)}\" width=\"{N(b.Width + 2)}\" height=\"{N(b.Height + 2)}\" fill=\"white\"/>");
            foreach (var d in doors)
                svg.Append($"<rect x=\"{N(d.Left)}\" y=\"{N(d.Top)}\" width=\"{N(d.Width)}\" height=\"{N(d.Height)}\" fill=\"black\"/>");
            svg.Append("</mask></defs>");
            mask = $" mask=\"url(#{maskId})\"";
        }
        svg.Append($"<g class=\"walls\" pointer-events=\"none\" fill=\"none\" stroke=\"{Colour}\" stroke-width=\"{N(Thickness)}\" stroke-linejoin=\"miter\" stroke-linecap=\"square\"{mask}>");
        foreach (var wall in floor.Walls.Where(w => w.Points.Count >= 2))
            svg.Append($"<{(wall.Closed ? "polygon" : "polyline")} points=\"{Points(wall)}\"/>");
        svg.Append("</g>");
        return svg.ToString();
    }

    /// <summary>The rectangle around the walls' corners (a 1 × 1 m square at the origin without walls).</summary>
    public static Rect Bounds(IEnumerable<Wall> walls)
    {
        var points = walls.SelectMany(w => w.Points).ToList();
        if (points.Count == 0)
            return new Rect(0, 0, 1, 1);
        var (left, top) = (points.Min(p => p.X), points.Min(p => p.Y));
        return new Rect(left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
    }

    /// <summary>The wall's pieces, each from one corner to the next (and back to the first when closed).</summary>
    public static IEnumerable<(Point From, Point To)> Segments(Wall wall)
    {
        for (var i = 0; i + 1 < wall.Points.Count; i++)
            yield return (wall.Points[i], wall.Points[i + 1]);
        if (wall.Closed && wall.Points.Count >= 3)
            yield return (wall.Points[^1], wall.Points[0]);
    }

    public static double Length(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>A length to show, e.g. "3.5 m".</summary>
    public static string LengthText(double metres) => $"{metres.ToString("0.##", CultureInfo.InvariantCulture)} m";

    /// <summary>
    /// Where the next corner goes while drawing: straight across or down from the last corner (whichever is closer to the
    /// pointer) unless <paramref name="anyAngle"/>, with the length on the grid unless <paramref name="free"/>; the first
    /// corner just snaps to the grid.
    /// </summary>
    public static Point NextCorner(Point? last, Point pointer, bool anyAngle, bool free)
    {
        double Snap(double v) => free ? v : Grid.Snap(v);
        if (last is not { } from)
            return new Point(Snap(pointer.X), Snap(pointer.Y));
        if (anyAngle)
            return new Point(Snap(pointer.X), Snap(pointer.Y));
        var (dx, dy) = (pointer.X - from.X, pointer.Y - from.Y);
        return Math.Abs(dx) >= Math.Abs(dy) ? new Point(from.X + Snap(dx), from.Y) : new Point(from.X, from.Y + Snap(dy));
    }

    private static string N(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
}
