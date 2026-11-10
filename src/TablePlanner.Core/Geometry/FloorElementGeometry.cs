using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

public static class FloorElementGeometry
{
    public static Rect GetBounds(FloorElement element)
    {
        var quarterTurn = element.Rotation is Rotation.R90 or Rotation.R270;
        return quarterTurn
            ? Rect.Centered(element.X, element.Y, element.Depth, element.Width)
            : Rect.Centered(element.X, element.Y, element.Width, element.Depth);
    }

    public static string DisplayName(FloorElementKind kind) => kind switch
    {
        FloorElementKind.DanceFloor => "Dance floor",
        FloorElementKind.DjBooth => "DJ booth",
        FloorElementKind.PhotoBooth => "Photo booth",
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
        _ => (2, 1),
    };
}
