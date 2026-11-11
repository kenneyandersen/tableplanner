using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>A guide line shown while a table snaps to a neighbour: across (<see cref="Vertical"/> false) or down, at a coordinate.</summary>
/// <param name="Vertical">A vertical line at x = <paramref name="At"/>, from y = <paramref name="From"/> to <paramref name="To"/>; otherwise horizontal.</param>
public readonly record struct SnapGuide(bool Vertical, double At, double From, double To);

/// <summary>
/// Lining a dragged table up with the tables near it, instead of only with the grid: its edges with their edges, its
/// centre with their centres, and its seats with their seats (so seats sit back to back or across from each other). On
/// each axis the closest match within <see cref="Reach"/> wins; an axis without one is left to the grid.
/// </summary>
public static class TableSnapping
{
    /// <summary>How close (metres) a line must come to a neighbour's to snap to it.</summary>
    public const double Reach = 0.25;

    /// <summary>Only tables this close (metres, across the other axis) count as neighbours.</summary>
    public const double Near = 4;

    private enum Kind { Edge, Centre, Seat }

    /// <param name="moving">The dragged table, at its position before the drag.</param>
    /// <param name="dx">How far it has been dragged, before snapping.</param>
    /// <param name="others">The tables it may line up with (not those being dragged with it).</param>
    /// <returns>The movement corrected on the axes that snapped (null on an axis that did not), and the guides to show.</returns>
    public static (double? Dx, double? Dy, List<SnapGuide> Guides) Snap(Table moving, double dx, double dy, IEnumerable<Table> others)
    {
        var start = TableGeometry.GetBounds(moving);
        var b = new Rect(start.Left + dx, start.Top + dy, start.Width, start.Height);
        var seats = TableGeometry.GetSeats(moving).Where(s => !s.IsDisabled).Select(s => s.Position.Offset(dx, dy)).ToList();
        var neighbours = others.Where(o => o.Id != moving.Id).Select(o => (Table: o, Bounds: TableGeometry.GetBounds(o))).ToList();
        var guides = new List<SnapGuide>();

        // Across: x lines of tables that are near in y; down: y lines of tables near in x.
        var snapX = Best(Lines(b, seats, vertical: true),
            neighbours.Where(n => Gap(n.Bounds.Top, n.Bounds.Bottom, b.Top, b.Bottom) <= Near)
                .SelectMany(n => Lines(n.Bounds, TableGeometry.GetSeats(n.Table).Select(s => s.Position).ToList(), vertical: true).Select(l => (l.At, l.Kind, n.Bounds))));
        var snapY = Best(Lines(b, seats, vertical: false),
            neighbours.Where(n => Gap(n.Bounds.Left, n.Bounds.Right, b.Left, b.Right) <= Near)
                .SelectMany(n => Lines(n.Bounds, TableGeometry.GetSeats(n.Table).Select(s => s.Position).ToList(), vertical: false).Select(l => (l.At, l.Kind, n.Bounds))));

        double? snappedX = null, snappedY = null;
        if (snapX is var (cx, atX, otherX))
        {
            snappedX = dx + cx;
            guides.Add(new SnapGuide(true, atX, Math.Min(b.Top, otherX.Top) - 0.3, Math.Max(b.Bottom, otherX.Bottom) + 0.3));
        }
        if (snapY is var (cy, atY, otherY))
        {
            snappedY = dy + cy;
            guides.Add(new SnapGuide(false, atY, Math.Min(b.Left, otherY.Left) - 0.3, Math.Max(b.Right, otherY.Right) + 0.3));
        }
        return (snappedX, snappedY, guides);
    }

    /// <summary>The lines of a table along one axis: edges, centre, and its seats' positions.</summary>
    private static List<(double At, Kind Kind)> Lines(Rect b, IReadOnlyList<Point> seats, bool vertical)
    {
        var lines = vertical
            ? new List<(double, Kind)> { (b.Left, Kind.Edge), (b.Right, Kind.Edge), (b.Left + b.Width / 2, Kind.Centre) }
            : [(b.Top, Kind.Edge), (b.Bottom, Kind.Edge), (b.Top + b.Height / 2, Kind.Centre)];
        lines.AddRange(seats.Select(s => (vertical ? s.X : s.Y, Kind.Seat)));
        return lines;
    }

    /// <summary>The closest pair of lines of the same kind within <see cref="Reach"/>: the correction, the line, and the neighbour.</summary>
    private static (double Correction, double At, Rect Other)? Best(List<(double At, Kind Kind)> mine, IEnumerable<(double At, Kind Kind, Rect Bounds)> theirs)
    {
        (double, double, Rect)? best = null;
        var bestDistance = Reach + 1e-9;
        foreach (var other in theirs)
            foreach (var line in mine)
            {
                if (line.Kind != other.Kind)
                    continue;
                var distance = Math.Abs(other.At - line.At);
                if (distance < bestDistance)
                    (best, bestDistance) = ((other.At - line.At, other.At, other.Bounds), distance);
            }
        return best;
    }

    /// <summary>The gap between two ranges; 0 when they overlap.</summary>
    private static double Gap(double a1, double a2, double b1, double b2) => Math.Max(0, Math.Max(a1 - b2, b1 - a2));
}
