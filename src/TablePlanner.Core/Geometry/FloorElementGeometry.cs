using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

public static class FloorElementGeometry
{
    /// <summary>The rectangle around the element as turned (for a quarter turn exactly its box).</summary>
    public static Rect GetBounds(FloorElement element)
    {
        if (element.IsQuarterTurn)
        {
            var sideways = element.Rotation is Rotation.R90 or Rotation.R270;
            return sideways
                ? Rect.Centered(element.X, element.Y, element.Depth, element.Width)
                : Rect.Centered(element.X, element.Y, element.Width, element.Depth);
        }
        var radians = element.Angle * Math.PI / 180;
        var (cos, sin) = (Math.Abs(Math.Cos(radians)), Math.Abs(Math.Sin(radians)));
        return Rect.Centered(element.X, element.Y, element.Width * cos + element.Depth * sin, element.Width * sin + element.Depth * cos);
    }

    /// <summary>
    /// The element's box before turning (centred on it, width × depth); drawn with <see cref="Transform"/> it shows the
    /// element at its angle.
    /// </summary>
    public static Rect Unturned(FloorElement element) => Rect.Centered(element.X, element.Y, element.Width, element.Depth);

    /// <summary>The SVG transform that turns the element around its centre, or null when it is not turned.</summary>
    public static string? Transform(FloorElement element) => element.Angle == 0 ? null
        : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"rotate({element.Angle} {Math.Round(element.X, 3)} {Math.Round(element.Y, 3)})");

    public static string DisplayName(FloorElementKind kind) => kind switch
    {
        FloorElementKind.DanceFloor => "Dance floor",
        FloorElementKind.DjBooth => "DJ booth",
        FloorElementKind.PhotoBooth => "Photo booth",
        FloorElementKind.Text => "Text",
        _ => kind.ToString(),
    };

    /// <summary>A sensible starting size (width × depth, in metres) for a new element.</summary>
    public static (double Width, double Depth) DefaultSize(FloorElementKind kind) => kind switch
    {
        FloorElementKind.Bar => (4, 1),
        FloorElementKind.Buffet => (4, 1),
        FloorElementKind.Stairs => (2, 3),
        FloorElementKind.Entrance => (2, 0.5),
        FloorElementKind.Exit => (1.5, 0.5),
        FloorElementKind.Toilets => (3, 2),
        FloorElementKind.Stage => (6, 3),
        FloorElementKind.DanceFloor => (5, 5),
        FloorElementKind.Cloakroom => (3, 2),
        FloorElementKind.Pillar => (0.5, 0.5),
        FloorElementKind.DjBooth => (2.5, 1.2),
        FloorElementKind.PhotoBooth => (2, 2),
        FloorElementKind.Text => (3, 0.8),
        _ => (2, 1),
    };
}
