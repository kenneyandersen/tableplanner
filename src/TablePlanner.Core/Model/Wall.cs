using TablePlanner.Core.Geometry;

namespace TablePlanner.Core.Model;

/// <summary>
/// A wall drawn on a floor: straight pieces between corner points, in metres. <see cref="Closed"/> joins the last corner
/// to the first (a room's outline). Only a drawing, for when there is no picture of the venue: the solver ignores walls.
/// </summary>
public sealed class Wall
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public List<Point> Points { get; set; } = [];
    public bool Closed { get; set; }
}
