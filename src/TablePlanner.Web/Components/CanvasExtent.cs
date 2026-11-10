using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Web.Components;

/// <summary>The area a floor canvas draws, in metres.</summary>
public static class CanvasExtent
{
    /// <summary>Space before the first metre on the top and left, in metres (tables' chairs stick out about 0.9 m).</summary>
    public const double Margin = 1.5;

    /// <summary>
    /// The floor's tables, elements, walls and background image plus a margin, at least 30 × 20 m from the origin.
    /// It starts a little above and left of the origin (or of anything further up or left), so walls and tables placed at
    /// the origin are not stuck to the edge, and their seats, corners and handles are easy to reach.
    /// </summary>
    public static Rect Of(Floor floor)
    {
        var bounds = floor.Tables.Select(TableGeometry.GetBounds).Concat(floor.Elements.Select(FloorElementGeometry.GetBounds)).ToList();
        if (floor.Walls.Count > 0)
            bounds.Add(WallDrawing.Bounds(floor.Walls));
        if (floor.Background is { } background)
            bounds.Add(background.Bounds);
        var left = Math.Floor(Math.Min(0, bounds.Select(b => b.Left).DefaultIfEmpty(0).Min()) - Margin);
        var top = Math.Floor(Math.Min(0, bounds.Select(b => b.Top).DefaultIfEmpty(0).Min()) - Margin);
        var right = Math.Max(30, Math.Ceiling(bounds.Select(b => b.Right).DefaultIfEmpty(0).Max()) + 5);
        var bottom = Math.Max(20, Math.Ceiling(bounds.Select(b => b.Bottom).DefaultIfEmpty(0).Max()) + 5);
        return new Rect(left, top, right - left, bottom - top);
    }
}
