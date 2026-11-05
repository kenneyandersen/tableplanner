using TablePlanner.Core.Geometry;

namespace TablePlanner.Core.Model;

/// <summary>
/// A picture under a floor (e.g. the venue's floor plan) to place tables on. Only these settings are part of the
/// project model; the image itself is kept separately (see <see cref="ProjectImage"/>), so undo snapshots stay small.
/// </summary>
public sealed class FloorBackground
{
    public Guid ImageId { get; set; }

    /// <summary>The image's size in pixels (after it was downscaled on upload).</summary>
    public int PixelWidth { get; set; }

    public int PixelHeight { get; set; }

    /// <summary>The image's top-left corner on the floor, in metres.</summary>
    public double X { get; set; }

    public double Y { get; set; }

    /// <summary>The scale: how many metres one image pixel covers. Set by calibrating with a known distance.</summary>
    public double MetresPerPixel { get; set; } = 0.02;

    /// <summary>0.1 (faint) to 1 (fully visible).</summary>
    public double Opacity { get; set; } = 0.5;

    /// <summary>A locked image cannot be dragged by accident.</summary>
    public bool Locked { get; set; }

    /// <summary>Also draws the image (faded) on the printed poster.</summary>
    public bool ShowOnPoster { get; set; }

    public double Width => PixelWidth * MetresPerPixel;

    public double Height => PixelHeight * MetresPerPixel;

    public Rect Bounds => new(X, Y, Width, Height);
}

/// <summary>An image used by a project, as a data URL (e.g. "data:image/jpeg;base64,…").</summary>
public sealed record ProjectImage(Guid Id, string DataUrl);
