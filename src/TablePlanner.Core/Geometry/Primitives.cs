using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>A point in floor coordinates (metres). Y grows downwards, as on screen.</summary>
public readonly record struct Point(double X, double Y)
{
    /// <summary>Rotates around the origin, clockwise in screen coordinates. Exact for 90° steps.</summary>
    public Point Rotate(Rotation rotation) => rotation switch
    {
        Rotation.R0 => this,
        Rotation.R90 => new Point(-Y, X),
        Rotation.R180 => new Point(-X, -Y),
        Rotation.R270 => new Point(Y, -X),
        _ => throw new ArgumentOutOfRangeException(nameof(rotation), rotation, null),
    };

    public Point Offset(double dx, double dy) => new(X + dx, Y + dy);
}

/// <summary>An axis-aligned rectangle in floor coordinates.</summary>
public readonly record struct Rect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;

    public static Rect Centered(double centreX, double centreY, double width, double height) =>
        new(centreX - width / 2, centreY - height / 2, width, height);

    public bool Contains(Point p) => p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
}

public static class Grid
{
    public const double DefaultStep = 0.5;

    /// <summary>Snaps a coordinate to the nearest multiple of <paramref name="step"/>.</summary>
    public static double Snap(double value, double step = DefaultStep) =>
        step <= 0 ? value : Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
}
