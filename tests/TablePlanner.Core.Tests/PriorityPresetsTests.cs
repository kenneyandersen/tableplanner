using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class PriorityPresetsTests
{
    [Fact]
    public void The_defaults_are_the_balanced_preset()
    {
        Assert.Equal("Balanced", PriorityPresets.Matching(new SeatingPriorities())?.Name);
    }

    [Fact]
    public void Each_preset_is_recognised_after_it_is_applied()
    {
        foreach (var preset in PriorityPresets.All)
        {
            var priorities = new SeatingPriorities();
            preset.Apply(priorities);

            Assert.Equal(preset.Name, PriorityPresets.Matching(priorities)?.Name);
        }
    }

    [Fact]
    public void A_preset_keeps_the_time_limit_and_the_floor_rule()
    {
        var priorities = new SeatingPriorities { TimeBudgetSeconds = 30, NeverSplitAcrossFloors = true, MinimumFragmentSize = 6 };

        PriorityPresets.All.Single(p => p.Name == "Keep groups together").Apply(priorities);

        Assert.Equal((30, true, 6), (priorities.TimeBudgetSeconds, priorities.NeverSplitAcrossFloors, priorities.MinimumFragmentSize));
        Assert.Equal(150, priorities.TableSplitPenalty);
    }

    [Fact]
    public void Weights_changed_by_hand_match_no_preset()
    {
        var priorities = new SeatingPriorities { NextToWeight = 11 };

        Assert.Null(PriorityPresets.Matching(priorities));
    }

    [Fact]
    public void Padding_the_preference_list_with_zeros_still_matches()
    {
        var priorities = new SeatingPriorities();
        priorities.PreferenceRankWeights.Add(0);

        Assert.Equal("Balanced", PriorityPresets.Matching(priorities)?.Name);
    }
}
