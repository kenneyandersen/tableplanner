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
}
