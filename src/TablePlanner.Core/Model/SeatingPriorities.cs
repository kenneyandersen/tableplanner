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
    public double TableSplitPenalty { get; set; } = 50;
    public double DistantTableSplitPenalty { get; set; } = 100;
    public double FloorSplitPenalty { get; set; } = 1000;
    public bool NeverSplitAcrossFloors { get; set; }

    /// <summary>Avoid leaving fewer than this many people of a group alone at a table.</summary>
    public int MinimumFragmentSize { get; set; } = 4;

    /// <summary>Weight per preference rank (1st, 2nd, …). Falls steeply so first choices are fulfilled for everyone first.</summary>
    public List<double> PreferenceRankWeights { get; set; } = [100, 30, 10, 3, 1];

    public double MutualPreferenceBonus { get; set; } = 50;

    public double TimeBudgetSeconds { get; set; } = 8;
}
