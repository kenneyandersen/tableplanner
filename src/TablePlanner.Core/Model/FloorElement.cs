namespace TablePlanner.Core.Model;

public enum FloorElementKind
{
    Bar,
    Buffet,
    Stairs,
    Entrance,
    Exit,
    Toilets,
    Stage,
    DanceFloor,
    Cloakroom,
    Pillar,
    DjBooth,
    PhotoBooth,
    Text,
    Custom,
}

/// <summary>A labelled landmark on a floor (bar, stairs, …). Has no seats; ignored by the solver.</summary>
public sealed class FloorElement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public FloorElementKind Kind { get; set; } = FloorElementKind.Custom;
    public string Label { get; set; } = "";

    /// <summary>Centre, in metres.</summary>
    public double X { get; set; }

    /// <summary>Centre, in metres.</summary>
    public double Y { get; set; }

    public double Width { get; set; } = 2;
    public double Depth { get; set; } = 1;
    /// <summary>
    /// The quarter turn nearest to <see cref="Angle"/>. Kept for projects saved before elements could turn freely: setting
    /// it sets the angle (R90 = 90°). In files it comes before <see cref="Angle"/>, so a saved angle wins.
    /// </summary>
    public Rotation Rotation
    {
        get => (Rotation)((int)Math.Round(Angle / 90) % 4);
        set => Angle = (int)value * 90;
    }

    /// <summary>How far the element is turned around its centre, clockwise, in degrees (0 ≤ angle &lt; 360).</summary>
    public double Angle
    {
        get => _angle;
        set => _angle = ((Math.Round(value, 3) % 360) + 360) % 360;
    }

    private double _angle;

    /// <summary>Whether the element is turned a whole number of quarter turns (its box lines up with the grid).</summary>
    public bool IsQuarterTurn => Math.Abs(Angle / 90 - Math.Round(Angle / 90)) < 1e-9;

    /// <summary>For a pillar: drawn round (an ellipse in its box) rather than square.</summary>
    public bool IsRound { get; set; }

    /// <summary>The label's height in metres; null fits it to the element automatically.</summary>
    public double? LabelSize { get; set; }

    /// <summary>How far the label is moved from where it goes by default, in metres (dragged in the editor).</summary>
    public double LabelOffsetX { get; set; }

    /// <inheritdoc cref="LabelOffsetX"/>
    public double LabelOffsetY { get; set; }
}
