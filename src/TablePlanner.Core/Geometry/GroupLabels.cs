using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>A group's name on the table top, in the middle of its seats at that table.</summary>
/// <param name="FontSize">In metres: <see cref="GroupLabels.FontSize"/>, or smaller for a long name on a short part of the table.</param>
/// <param name="Vertical">True when the table is turned by 90° or 270°: the text runs along the table.</param>
public sealed record GroupLabel(Guid GroupId, string Text, Point Position, double FontSize, bool Vertical);

/// <summary>
/// Where group names and table numbers go on a drawn table, shared by the plan view and the printed poster.
/// The table number sits in a small line along the top edge of the table top (in the table's own frame);
/// group names go on two lines below it, placed so that none overlap (see <see cref="For"/>).
/// </summary>
public static class GroupLabels
{
    /// <summary>Font size of group names, in metres.</summary>
    public const double FontSize = 0.26;

    /// <summary>Long names are drawn down to this share of <see cref="FontSize"/> before they are shortened.</summary>
    public const double MinFontScale = 0.65;

    /// <summary>Average width of a character relative to the font size.</summary>
    private const double CharWidth = 0.58;

    /// <summary>Font size of table numbers, in metres.</summary>
    public const double TableNumberFontSize = 0.2;

    /// <summary>Along the top edge of a long table; in the middle of a round one (group names go around it).</summary>
    public static Point TableNumberPosition(Table table) => table.IsRound
        ? new Point(table.X, table.Y + TableNumberFontSize / 3)
        : new Point(-table.Length / 2 + 0.25, -table.Width / 2 + 0.15).Rotate(table.Rotation).Offset(table.X, table.Y);

    /// <summary>
    /// A label per group at this table, never on top of each other or of the table number. On a long table there are two
    /// lines: a group on the top side only gets the upper one, on the bottom side only (or both sides) the lower one; a
    /// label that would overlap another moves to the other line, and if neither has room it is made smaller or shortened
    /// to fit the gap. Each label sits over the group's longest unbroken run of seats, and fits its length.
    /// </summary>
    public static IEnumerable<GroupLabel> For(Table table, IReadOnlyList<SeatLayout> seats, IReadOnlyDictionary<SeatId, Guid> groupAt, EventSetup setup)
    {
        var parts = seats.Where(s => groupAt.ContainsKey(s.Id)).GroupBy(s => groupAt[s.Id])
            .Select(p => (Group: setup.FindGroup(p.Key), Seats: p.ToList()))
            .Where(p => p.Group is not null)
            .Select(p => (Group: p.Group!, p.Seats))
            .ToList();
        return table.IsRound ? RoundLabels(table, parts) : LongLabels(table, parts);
    }

    /// <summary>The gap kept between two labels on a line, in metres.</summary>
    private const double Gap = 0.06;

    private static IEnumerable<GroupLabel> LongLabels(Table table, List<(Group Group, List<SeatLayout> Seats)> parts)
    {
        var vertical = table.Rotation is Rotation.R90 or Rotation.R270;
        double[] rowY = [-table.Width * 0.2, table.Width * 0.22];
        // What is already on each line, as (from, to) along the table; the table number takes the start of the upper line.
        var taken = new List<(double From, double To)>[] { [], [] };
        var numberX = -table.Length / 2 + 0.25;
        taken[0].Add((numberX - 0.1, numberX + table.Number.ToString().Length * TableNumberFontSize * CharWidth + 0.05));

        var wanted = parts.Select(p =>
        {
            var top = p.Seats.Where(s => s.Side == SeatSide.Top).Select(s => s.PositionAlongSide).ToHashSet();
            var bottom = p.Seats.Where(s => s.Side == SeatSide.Bottom).Select(s => s.PositionAlongSide).ToHashSet();
            var columns = top.Concat(bottom).Distinct().Order().ToList();
            if (columns.Count == 0)
                columns = [p.Seats[0].Side == SeatSide.LeftEnd ? 0 : table.SeatsPerSide - 1];
            var (first, last) = LongestRun(columns);
            var centre = -table.Length / 2 + table.SeatSpacing * ((first + last) / 2.0 + 0.5);
            var room = (last - first + 1) * table.SeatSpacing;
            var row = top.Count > 0 && bottom.Count == 0 ? 0 : 1;
            return (p.Group, Centre: centre, Room: room, Row: row);
        }).OrderBy(w => w.Centre).ToList();

        foreach (var (group, centre, room, preferred) in wanted)
        {
            var (text, fontSize) = Fit(group.Name, room);
            var half = Width(text, fontSize) / 2;
            int row;
            var x = centre;
            if (IsFree(taken[preferred], centre - half, centre + half))
                row = preferred;
            else if (IsFree(taken[1 - preferred], centre - half, centre + half))
                row = 1 - preferred;
            else
            {
                // No room as it is: fit it into the free gap around its place on its own line.
                row = preferred;
                var (from, to) = FreeGap(taken[row], centre, -table.Length / 2, table.Length / 2);
                (text, fontSize) = Fit(group.Name, Math.Max(0.1, Math.Min(room, to - from - 2 * Gap)));
                half = Width(text, fontSize) / 2;
                x = Math.Clamp(centre, from + Gap + half, Math.Max(from + Gap + half, to - Gap - half));
            }
            taken[row].Add((x - half, x + half));
            var local = new Point(x, rowY[row]);
            yield return new GroupLabel(group.Id, text, local.Rotate(table.Rotation).Offset(table.X, table.Y), fontSize, vertical);
        }
    }

    /// <summary>
    /// On a round table: towards the group's seats, half-way out from the middle (where the table number is); a group that
    /// sits all around the table gets its name just below the number. A label that would overlap another moves further
    /// out, and failing that is shortened.
    /// </summary>
    private static IEnumerable<GroupLabel> RoundLabels(Table table, List<(Group Group, List<SeatLayout> Seats)> parts)
    {
        var radius = table.Diameter / 2;
        var placed = new List<Rect>
        {
            // The table number in the middle.
            Rect.Centered(table.X, table.Y, table.Number.ToString().Length * TableNumberFontSize * CharWidth + 0.05, TableNumberFontSize),
        };
        foreach (var (group, part) in parts)
        {
            var (sx, sy) = (part.Sum(s => s.Position.X - table.X), part.Sum(s => s.Position.Y - table.Y));
            var length = Math.Sqrt(sx * sx + sy * sy);
            var all = length < 0.25 * part.Count;
            var (dx, dy) = all ? (0.0, 1.0) : (sx / length, sy / length);
            var (text, fontSize) = Fit(group.Name, Math.Max(table.Diameter * 0.7, 0.6));

            Rect Box(double at, string t, double size) =>
                Rect.Centered(table.X + dx * at, table.Y + dy * at, Width(t, size), size);
            var distance = all ? radius * 0.4 : radius * 0.5;
            if (placed.Any(p => p.Overlaps(Box(distance, text, fontSize))))
            {
                distance = radius * 0.78;
                // Shorten it until it fits between its neighbours at the outer position.
                for (var room = table.Diameter * 0.6; room > 0.15 && placed.Any(p => p.Overlaps(Box(distance, text, fontSize))); room -= 0.1)
                    (text, fontSize) = Fit(group.Name, room);
            }
            placed.Add(Box(distance, text, fontSize));
            yield return new GroupLabel(group.Id, text, new Point(table.X + dx * distance, table.Y + dy * distance), fontSize, false);
        }
    }

    /// <summary>The name in the room: smaller first (down to <see cref="MinFontScale"/>), then shortened with "…".</summary>
    private static (string Text, double FontSize) Fit(string name, double room)
    {
        var fontSize = Math.Clamp(room / (Math.Max(1, name.Length) * CharWidth), FontSize * MinFontScale, FontSize);
        var maxChars = Math.Max(3, (int)(room / (fontSize * CharWidth)));
        return (name.Length <= maxChars ? name : name[..(maxChars - 1)] + "…", fontSize);
    }

    private static double Width(string text, double fontSize) => text.Length * fontSize * CharWidth;

    /// <summary>The longest run of consecutive columns (first and last); the list is sorted.</summary>
    private static (int First, int Last) LongestRun(List<int> sorted)
    {
        var (bestFirst, bestLast, first) = (sorted[0], sorted[0], sorted[0]);
        for (var i = 1; i <= sorted.Count; i++)
        {
            if (i < sorted.Count && sorted[i] == sorted[i - 1] + 1)
                continue;
            var last = sorted[i - 1];
            if (last - first > bestLast - bestFirst)
                (bestFirst, bestLast) = (first, last);
            if (i < sorted.Count)
                first = sorted[i];
        }
        return (bestFirst, bestLast);
    }

    private static bool IsFree(List<(double From, double To)> taken, double from, double to) =>
        taken.All(t => to + Gap <= t.From || from - Gap >= t.To);

    /// <summary>The free stretch of a line around <paramref name="at"/>, between the labels already there and the table's ends.</summary>
    private static (double From, double To) FreeGap(List<(double From, double To)> taken, double at, double start, double end)
    {
        var from = taken.Where(t => t.To <= at).Select(t => t.To).DefaultIfEmpty(start).Max();
        var to = taken.Where(t => t.From >= at).Select(t => t.From).DefaultIfEmpty(end).Min();
        // A label across the point: take the side with more room.
        foreach (var t in taken.Where(t => t.From < at && t.To > at))
            if (at - t.From > t.To - at)
                to = Math.Min(to, t.From);
            else
                from = Math.Max(from, t.To);
        return (from, Math.Max(from, to));
    }
}
