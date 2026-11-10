namespace TablePlanner.Core.Model;

/// <summary>A named set of solver weights to start from instead of setting each number.</summary>
public sealed record PriorityPreset(string Name, string Description, Action<SeatingPriorities> Apply);

/// <summary>
/// Ready-made priorities. A preset sets only the weights: the time limit, the floor rule and the size of a "small
/// part" are the user's own choices and are kept.
/// </summary>
public static class PriorityPresets
{
    public static readonly IReadOnlyList<PriorityPreset> All =
    [
        new("Balanced", "The defaults: groups together, first choices next, other wishes after that.", p => Weights(p, new SeatingPriorities())),
        new("Keep groups together", "Splitting a group costs much more; preferences count for less.", p =>
        {
            Weights(p, new SeatingPriorities());
            (p.TableSplitPenalty, p.DistantTableSplitPenalty, p.FloorSplitPenalty) = (150, 300, 3000);
            p.SmallFragmentPenalty = 400;
            p.ApartPersonPenalty = 600;
            p.ParentApartPenalty = 120;
            p.PreferenceRankWeights = [100, 30, 10, 3, 1];
            p.MutualPreferenceBonus = 25;
        }),
        new("Preferences first", "Wishes to sit with other groups count much more; groups split a little more easily.", p =>
        {
            Weights(p, new SeatingPriorities());
            (p.TableSplitPenalty, p.DistantTableSplitPenalty) = (30, 60);
            p.ApartPersonPenalty = 100;
            p.PreferenceRankWeights = [400, 150, 50, 15, 5];
            p.MutualPreferenceBonus = 100;
        }),
        new("Few changes", "For re-optimizing a plan that is already shared: moving people to other tables costs much more.", p =>
        {
            Weights(p, new SeatingPriorities());
            p.MovedPersonPenalty = 100;
        }),
    ];

    /// <summary>The preset whose weights <paramref name="priorities"/> has, or null when they were changed by hand.</summary>
    public static PriorityPreset? Matching(SeatingPriorities priorities) => All.FirstOrDefault(preset =>
    {
        var expected = priorities.Clone();
        preset.Apply(expected);
        return SameWeights(expected, priorities);
    });

    private static void Weights(SeatingPriorities target, SeatingPriorities source)
    {
        target.NextToWeight = source.NextToWeight;
        target.AcrossWeight = source.AcrossWeight;
        target.DiagonalWeight = source.DiagonalWeight;
        target.BackToBackWeight = source.BackToBackWeight;
        target.SameTableWeight = source.SameTableWeight;
        target.AdjacentTableWeight = source.AdjacentTableWeight;
        target.TableSplitPenalty = source.TableSplitPenalty;
        target.DistantTableSplitPenalty = source.DistantTableSplitPenalty;
        target.ApartPersonPenalty = source.ApartPersonPenalty;
        target.ParentApartPenalty = source.ParentApartPenalty;
        target.FloorSplitPenalty = source.FloorSplitPenalty;
        target.SmallFragmentPenalty = source.SmallFragmentPenalty;
        target.EmptySeatGapPenalty = source.EmptySeatGapPenalty;
        target.MovedPersonPenalty = source.MovedPersonPenalty;
        target.PreferenceRankWeights = [.. source.PreferenceRankWeights];
        target.MutualPreferenceBonus = source.MutualPreferenceBonus;
    }

    private static bool SameWeights(SeatingPriorities a, SeatingPriorities b) =>
        a.NextToWeight == b.NextToWeight && a.AcrossWeight == b.AcrossWeight && a.DiagonalWeight == b.DiagonalWeight
        && a.BackToBackWeight == b.BackToBackWeight && a.SameTableWeight == b.SameTableWeight && a.AdjacentTableWeight == b.AdjacentTableWeight
        && a.TableSplitPenalty == b.TableSplitPenalty && a.DistantTableSplitPenalty == b.DistantTableSplitPenalty
        && a.ApartPersonPenalty == b.ApartPersonPenalty
        && a.ParentApartPenalty == b.ParentApartPenalty
        && a.FloorSplitPenalty == b.FloorSplitPenalty && a.SmallFragmentPenalty == b.SmallFragmentPenalty
        && a.EmptySeatGapPenalty == b.EmptySeatGapPenalty && a.MovedPersonPenalty == b.MovedPersonPenalty
        && a.MutualPreferenceBonus == b.MutualPreferenceBonus
        // The dialog pads the list to five ranks with zeros, so trailing zeros do not count.
        && a.PreferenceRankWeights.AsEnumerable().Reverse().SkipWhile(w => w == 0).SequenceEqual(b.PreferenceRankWeights.AsEnumerable().Reverse().SkipWhile(w => w == 0));
}
