using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Editing;

/// <summary>Edits to a floor's background image. Run them through <see cref="EditHistory"/> for undo.</summary>
public static class BackgroundOperations
{
    /// <summary>The width a new image gets on the floor until it is calibrated: the floor's contents, at least 30 m.</summary>
    public const double MinDefaultWidth = 30;

    public const double MinOpacity = 0.1;

    /// <summary>
    /// Puts an image under the floor. A replaced image keeps the position and scale when it has the same width in
    /// pixels (a new version of the same plan); otherwise the image starts at the top left, as wide as the floor's contents.
    /// </summary>
    public static FloorBackground SetImage(EventProject project, Guid floorId, Guid imageId, int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
            throw new ArgumentException("The image has no size.");
        var floor = GetFloor(project, floorId);
        var old = floor.Background;
        var background = new FloorBackground { ImageId = imageId, PixelWidth = pixelWidth, PixelHeight = pixelHeight };
        if (old is not null && old.PixelWidth == pixelWidth)
        {
            (background.X, background.Y, background.MetresPerPixel) = (old.X, old.Y, old.MetresPerPixel);
            (background.Opacity, background.Locked, background.ShowOnPoster) = (old.Opacity, old.Locked, old.ShowOnPoster);
        }
        else
        {
            var right = floor.Tables.Select(TableGeometry.GetBounds).Concat(floor.Elements.Select(FloorElementGeometry.GetBounds))
                .Select(b => b.Right).DefaultIfEmpty(0).Max();
            background.MetresPerPixel = Math.Max(MinDefaultWidth, Math.Ceiling(right)) / pixelWidth;
        }
        floor.Background = background;
        return background;
    }

    public static void Remove(EventProject project, Guid floorId) => GetFloor(project, floorId).Background = null;

    public static void Move(EventProject project, Guid floorId, double x, double y)
    {
        var background = GetBackground(project, floorId);
        (background.X, background.Y) = (x, y);
    }

    /// <summary>
    /// Sets the scale from two points on the floor (as the image is drawn now) and the real distance between them.
    /// The image grows or shrinks around the first point, which stays where it is.
    /// </summary>
    public static void Calibrate(EventProject project, Guid floorId, Point first, Point second, double metres)
    {
        var background = GetBackground(project, floorId);
        var drawn = Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
        if (drawn <= 0 || metres <= 0)
            throw new ArgumentException("The two points must be apart and the distance must be more than 0.");
        var factor = metres / drawn;
        background.MetresPerPixel *= factor;
        background.X = first.X - (first.X - background.X) * factor;
        background.Y = first.Y - (first.Y - background.Y) * factor;
    }

    public static void SetOpacity(EventProject project, Guid floorId, double opacity) =>
        GetBackground(project, floorId).Opacity = Math.Clamp(opacity, MinOpacity, 1);

    public static FloorBackground Clone(FloorBackground b) => new()
    {
        ImageId = b.ImageId,
        PixelWidth = b.PixelWidth,
        PixelHeight = b.PixelHeight,
        X = b.X,
        Y = b.Y,
        MetresPerPixel = b.MetresPerPixel,
        Opacity = b.Opacity,
        Locked = b.Locked,
        ShowOnPoster = b.ShowOnPoster,
    };

    /// <summary>The images the project's floors use.</summary>
    public static IReadOnlySet<Guid> ImagesInUse(EventProject project) =>
        project.Setup.Floors.Where(f => f.Background is not null).Select(f => f.Background!.ImageId).ToHashSet();

    private static Floor GetFloor(EventProject project, Guid floorId) =>
        project.Setup.Floors.FirstOrDefault(f => f.Id == floorId) ?? throw new ArgumentException("Unknown floor.", nameof(floorId));

    private static FloorBackground GetBackground(EventProject project, Guid floorId) =>
        GetFloor(project, floorId).Background ?? throw new ArgumentException("The floor has no background image.", nameof(floorId));
}
