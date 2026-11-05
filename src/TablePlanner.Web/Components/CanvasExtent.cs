using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Web.Components;

/// <summary>The area a floor canvas draws, in metres.</summary>
public static class CanvasExtent
{
    /// <summary>
    /// The floor's tables, elements and background image plus a margin, at least 30 × 20 m from the origin.
    /// It starts at the origin unless something lies further up or left (e.g. a background image scaled around a point).
    /// </summary>
    public static Rect Of(Floor floor)
    {
        var bounds = floor.Tables.Select(TableGeometry.GetBounds).Concat(floor.Elements.Select(FloorElementGeometry.GetBounds)).ToList();
        if (floor.Background is { } background)
            bounds.Add(background.Bounds);
        var left = Math.Min(0, Math.Floor(bounds.Select(b => b.Left).DefaultIfEmpty(0).Min()));
        var top = Math.Min(0, Math.Floor(bounds.Select(b => b.Top).DefaultIfEmpty(0).Min()));
        var right = Math.Max(30, Math.Ceiling(bounds.Select(b => b.Right).DefaultIfEmpty(0).Max()) + 5);
        var bottom = Math.Max(20, Math.Ceiling(bounds.Select(b => b.Bottom).DefaultIfEmpty(0).Max()) + 5);
        return new Rect(left, top, right - left, bottom - top);
    }
}
