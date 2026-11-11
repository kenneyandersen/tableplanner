using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class GroupLabelsTests
{
    /// <summary>A table of 6 per side with one group on its first <paramref name="columns"/> top seats.</summary>
    private static GroupLabel LabelFor(string name, int columns)
    {
        var table = new Table { SeatsPerSide = 6 };
        var group = new Group { Name = name };
        var setup = new EventSetup { Floors = [new Floor { Tables = [table] }], Groups = [group] };
        var groupAt = Enumerable.Range(0, columns).ToDictionary(i => new SeatId(table.Id, i), _ => group.Id);
        return Assert.Single(GroupLabels.For(table, TableGeometry.GetSeats(table), groupAt, setup));
    }

    [Fact]
    public void A_name_that_fits_is_drawn_in_full_size()
    {
        var label = LabelFor("Sales", 4);

        Assert.Equal("Sales", label.Text);
        Assert.Equal(GroupLabels.FontSize, label.FontSize);
    }

    [Fact]
    public void A_long_name_is_drawn_smaller_before_it_is_shortened()
    {
        var smaller = LabelFor("Old classmates", 3);
        var shortened = LabelFor("Old classmates from school", 2);

        Assert.Equal("Old classmates", smaller.Text);
        Assert.InRange(smaller.FontSize, GroupLabels.FontSize * GroupLabels.MinFontScale, GroupLabels.FontSize - 0.01);
        Assert.EndsWith("…", shortened.Text);
        Assert.Equal(GroupLabels.FontSize * GroupLabels.MinFontScale, shortened.FontSize, 6);
    }

    /// <summary>The labels on a table where each listed seat index belongs to the named group.</summary>
    private static List<GroupLabel> Labels(Table table, params (string Name, int[] Seats)[] groups)
    {
        var made = groups.Select(g => new Group { Name = g.Name }).ToList();
        var setup = new EventSetup { Floors = [new Floor { Tables = [table] }], Groups = made };
        var groupAt = groups.SelectMany((g, i) => g.Seats.Select(s => (Seat: new SeatId(table.Id, s), made[i].Id))).ToDictionary(x => x.Seat, x => x.Id);
        return GroupLabels.For(table, TableGeometry.GetSeats(table), groupAt, setup).ToList();
    }

    /// <summary>A label's box, from the same text width estimate as the labels use.</summary>
    private static Rect Box(GroupLabel l) => l.Vertical
        ? Rect.Centered(l.Position.X, l.Position.Y, l.FontSize, l.Text.Length * l.FontSize * 0.58)
        : Rect.Centered(l.Position.X, l.Position.Y, l.Text.Length * l.FontSize * 0.58, l.FontSize);

    private static void AssertNoOverlap(List<GroupLabel> labels)
    {
        for (var i = 0; i < labels.Count; i++)
            for (var j = i + 1; j < labels.Count; j++)
                Assert.False(Box(labels[i]).Overlaps(Box(labels[j])), $"'{labels[i].Text}' and '{labels[j].Text}' overlap");
    }

    [Fact]
    public void Groups_on_opposite_sides_get_a_line_each()
    {
        // Top seats 0–4 one group, bottom seats 0–4 (indices 8–12) another, on a table of 8 a side.
        var table = new Table { SeatsPerSide = 8 };

        var labels = Labels(table, ("Rowing club", [0, 1, 2, 3, 4]), ("Book club", [8, 9, 10, 11, 12]));

        Assert.Equal(2, labels.Count);
        Assert.True(labels[0].Position.Y < labels[1].Position.Y || labels[1].Position.Y < labels[0].Position.Y);
        AssertNoOverlap(labels);
    }

    [Theory]
    [InlineData(Rotation.R0)]
    [InlineData(Rotation.R90)]
    public void Many_small_groups_on_one_table_do_not_overlap(Rotation rotation)
    {
        var table = new Table { SeatsPerSide = 6, Rotation = rotation };

        // Six groups of two, each one column (top and bottom seat), and long names.
        var labels = Labels(table, Enumerable.Range(0, 6).Select(c => ($"Family number {c}", new[] { c, 6 + c })).ToArray());

        Assert.Equal(6, labels.Count);
        AssertNoOverlap(labels);
    }

    [Fact]
    public void A_group_in_two_pieces_is_labelled_over_its_larger_piece()
    {
        var table = new Table { SeatsPerSide = 8, X = 0 };

        // "Split" on top seats 0 and 4–7, "Middle" on 1–3.
        var labels = Labels(table, ("Split", [0, 4, 5, 6, 7]), ("Middle", [1, 2, 3]));

        var split = labels.Single(l => l.Text == "Split");
        Assert.True(split.Position.X > 0, "over seats 4–7, the right half");
        AssertNoOverlap(labels);
    }

    [Fact]
    public void Labels_around_a_round_table_do_not_overlap()
    {
        var table = new Table { Shape = TableShape.Round, RoundSeats = 10 };

        var labels = Labels(table, Enumerable.Range(0, 5).Select(i => ($"Group {i}", new[] { 2 * i, 2 * i + 1 })).ToArray());

        AssertNoOverlap(labels);
    }
}
