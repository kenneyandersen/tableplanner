using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>A group's name on the table top, in the middle of its seats at that table.</summary>
/// <param name="FontSize">In metres: <see cref="GroupLabels.FontSize"/>, or smaller for a long name on a short part of the table.</param>
/// <param name="Vertical">True when the table is turned by 90° or 270°: the text runs along the table.</param>
public sealed record GroupLabel(Guid GroupId, string Text, Point Position, double FontSize, bool Vertical);

/// <summary>
/// Where group names and table numbers go on a drawn table, shared by the plan view and the printed poster.
/// The table number sits in a small line along the top edge of the table top (in the table's own frame);
/// group names sit just below the middle, so the two never overlap.
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

    /// <summary>A label per group at this table, made smaller or shortened to fit the length of the group's part of the table.</summary>
    public static IEnumerable<GroupLabel> For(Table table, IReadOnlyList<SeatLayout> seats, IReadOnlyDictionary<SeatId, Guid> groupAt, EventSetup setup)
    {
        var vertical = table.Rotation is Rotation.R90 or Rotation.R270;
        foreach (var part in seats.Where(s => groupAt.ContainsKey(s.Id)).GroupBy(s => groupAt[s.Id]))
        {
            if (setup.FindGroup(part.Key) is not { } group)
                continue;
            if (table.IsRound)
            {
                yield return RoundLabel(table, part.ToList(), group);
                continue;
            }
            // Along the table: the middle of the group's side seats (or the end it sits at). Across: the middle of the table top.
            var along = part.Where(s => s.Side is SeatSide.Top or SeatSide.Bottom).Select(s => s.PositionAlongSide).ToList();
            if (along.Count == 0)
                along = [part.First().Side == SeatSide.LeftEnd ? 0 : table.SeatsPerSide - 1];
            var local = new Point(-table.Length / 2 + table.SeatSpacing * ((along.Min() + along.Max()) / 2.0 + 0.5), table.Width * 0.12);
            var room = (along.Max() - along.Min() + 1) * table.SeatSpacing;
            var fontSize = Math.Clamp(room / (Math.Max(1, group.Name.Length) * CharWidth), FontSize * MinFontScale, FontSize);
            var maxChars = Math.Max(3, (int)(room / (fontSize * CharWidth)));
            var text = group.Name.Length <= maxChars ? group.Name : group.Name[..(maxChars - 1)] + "…";
            yield return new GroupLabel(group.Id, text, local.Rotate(table.Rotation).Offset(table.X, table.Y), fontSize, vertical);
        }
    }

    /// <summary>
    /// On a round table: towards the group's seats, half-way out from the middle (where the table number is). A group
    /// that sits all around the table gets its name just below the number.
    /// </summary>
    private static GroupLabel RoundLabel(Table table, List<SeatLayout> part, Group group)
    {
        var (sx, sy) = (part.Sum(s => s.Position.X - table.X), part.Sum(s => s.Position.Y - table.Y));
        var length = Math.Sqrt(sx * sx + sy * sy);
        var radius = table.Diameter / 2;
        var position = length < 0.25 * part.Count
            ? new Point(table.X, table.Y + radius * 0.4)
            : new Point(table.X + sx / length * radius * 0.5, table.Y + sy / length * radius * 0.5);

        var room = Math.Max(table.Diameter * 0.7, 0.6);
        var fontSize = Math.Clamp(room / (Math.Max(1, group.Name.Length) * CharWidth), FontSize * MinFontScale, FontSize);
        var maxChars = Math.Max(3, (int)(room / (fontSize * CharWidth)));
        var text = group.Name.Length <= maxChars ? group.Name : group.Name[..(maxChars - 1)] + "…";
        return new GroupLabel(group.Id, text, position, fontSize, false);
    }
}
