using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class GroupColoursTests
{
    [Fact]
    public void Uses_the_groups_own_colour_and_distinct_automatic_colours_for_the_rest()
    {
        var setup = new EventSetup
        {
            Groups = [new Group { Name = "A" }, new Group { Name = "B", Colour = "#123456" }, new Group { Name = "C" }, new Group { Name = "D" }],
        };

        var colours = GroupColours.For(setup);

        Assert.Equal("#123456", colours[setup.Groups[1].Id]);
        Assert.Equal(GroupColours.Automatic(0), colours[setup.Groups[0].Id]);
        Assert.Equal(GroupColours.Automatic(1), colours[setup.Groups[2].Id]);
        Assert.Equal(4, colours.Values.Distinct().Count());
    }

    [Fact]
    public void Automatic_colours_are_hex_and_differ_for_many_groups()
    {
        var colours = Enumerable.Range(0, 120).Select(GroupColours.Automatic).ToList();

        Assert.All(colours, c => Assert.Matches("^#[0-9a-f]{6}$", c));
        Assert.Equal(120, colours.Distinct().Count());
    }

    [Fact]
    public void Clone_copies_priorities_including_the_weight_list()
    {
        var original = new SeatingPriorities { TableSplitPenalty = 7 };

        var copy = original.Clone();
        copy.PreferenceRankWeights[0] = 1;

        Assert.Equal(7, copy.TableSplitPenalty);
        Assert.NotEqual(1, original.PreferenceRankWeights[0]);
    }
}
