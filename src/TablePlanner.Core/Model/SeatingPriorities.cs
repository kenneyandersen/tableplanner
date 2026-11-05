namespace TablePlanner.Core.Model;

/// <summary>
/// Weights and limits for the solver, saved with the project.
/// Default values are a starting point and will be tuned together with the solver.
/// </summary>
public sealed class SeatingPriorities
{
    // Closeness between two people of the same group (higher = closer).
    public double NextToWeight { get; set; } = 10;
    public double AcrossWeight { get; set; } = 10;
    public double DiagonalWeight { get; set; } = 6;
    public double BackToBackWeight { get; set; } = 5;
    public double SameTableWeight { get; set; } = 2;
    public double AdjacentTableWeight { get; set; } = 1;

    // Penalties for splitting a group.

    /// <summary>Per table a group uses beyond the first.</summary>
    public double TableSplitPenalty { get; set; } = 50;

    /// <summary>Extra, per part of a group that is not at a table adjacent to the rest of the group.</summary>
    public double DistantTableSplitPenalty { get; set; } = 100;

    /// <summary>Per floor a group uses beyond the first.</summary>
    public double FloorSplitPenalty { get; set; } = 1000;

    /// <summary>Makes splitting across floors a hard rule rather than a penalty.</summary>
    public bool NeverSplitAcrossFloors { get; set; }

    /// <summary>Avoid leaving fewer than this many people of a group alone at a table (so 1–4 with the default).</summary>
    public int MinimumFragmentSize { get; set; } = 5;

    /// <summary>
    /// For each part of a split group with fewer than <see cref="MinimumFragmentSize"/> people: per person it is short.
    /// So 4 people at a separate table cost this once, 1 person alone four times: the smaller the part, the worse.
    /// </summary>
    public double SmallFragmentPenalty { get; set; } = 150;

    /// <summary>Per empty seat between two occupied seats on the same side of a table. Empty seats belong at the table ends.</summary>
    public double EmptySeatGapPenalty { get; set; } = 5;

    /// <summary>When re-optimizing: per person who ends up at a different table than in the current plan.</summary>
    public double MovedPersonPenalty { get; set; } = 20;

    /// <summary>Weight per preference rank (1st, 2nd, …). Falls steeply so first choices are fulfilled for everyone first.</summary>
    public List<double> PreferenceRankWeights { get; set; } = [200, 60, 20, 6, 2];

    /// <summary>Extra when two groups that prefer each other sit together.</summary>
    public double MutualPreferenceBonus { get; set; } = 50;

    /// <summary>The solver stops after this time and returns the best plan found.</summary>
    public double TimeBudgetSeconds { get; set; } = 8;

    public SeatingPriorities Clone()
    {
        var copy = (SeatingPriorities)MemberwiseClone();
        copy.PreferenceRankWeights = [.. PreferenceRankWeights];
        return copy;
    }
}
