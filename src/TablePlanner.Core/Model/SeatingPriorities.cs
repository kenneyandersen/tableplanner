namespace TablePlanner.Core.Model;

public enum SolverMethod
{
    /// <summary>Many small random changes to the order the groups are seated in, keeping what improves the plan.</summary>
    Search,

    /// <summary>Builds the plan group by group, keeping the best partial plans at each step (beam search).</summary>
    Beam,
}

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

    /// <summary>
    /// Per person seated away from the main part of their group (at a table that is neither the main part's nor next to
    /// it). Grows with the distance: doubled at 10 m away, at most 4 times (30 m or another floor). Keeps a split
    /// group's parts close to each other.
    /// </summary>
    public double ApartPersonPenalty { get; set; } = 200;

    /// <summary>
    /// Like <see cref="ApartPersonPenalty"/>, for parent groups (a department of teams): per person away from the main
    /// part of their parent group, growing with the distance. For a top-level group; a group a level below counts double,
    /// so teams stay closer together than whole departments. Much lower than for a bottom group: a large department may
    /// spread over several tables or areas, and even floors, when it must.
    /// </summary>
    public double ParentApartPenalty { get; set; } = 40;

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

    /// <summary>How Make plan searches. Re-optimizing always improves the current plan with <see cref="SolverMethod.Search"/>.</summary>
    public SolverMethod Method { get; set; } = SolverMethod.Search;

    public SeatingPriorities Clone()
    {
        var copy = (SeatingPriorities)MemberwiseClone();
        copy.PreferenceRankWeights = [.. PreferenceRankWeights];
        return copy;
    }
}
