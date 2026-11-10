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
    public Rotation Rotation { get; set; } = Rotation.R0;

    /// <summary>For a pillar: drawn round (an ellipse in its box) rather than square.</summary>
    public bool IsRound { get; set; }
}
