using TablePlanner.Core.Validation;

namespace TablePlanner.Solver;

/// <summary>Where a preferred group ended up, seen from the group that wanted it.</summary>
public enum PreferenceCloseness
{
    SameTable,
    AdjacentTable,
    SameFloor,
    Apart,

    /// <summary>One of the two groups has no seats.</summary>
    NotPlaced,
}

public sealed record PreferenceOutcome(Guid PreferredGroupId, int Rank, PreferenceCloseness Closeness)
{
    /// <summary>A preference counts as met when the groups share a table or sit at adjacent tables.</summary>
    public bool IsMet => Closeness is PreferenceCloseness.SameTable or PreferenceCloseness.AdjacentTable;
}

/// <summary>How one group is seated in a plan.</summary>
public sealed record GroupPlacement(
    Guid GroupId,
    string Name,
    int PeopleCount,
    int Placed,
    IReadOnlyList<int> TableNumbers,
    int FloorCount,
    bool HasDistantParts,
    int SmallParts,
    bool BreaksHardRule,
    IReadOnlyList<PreferenceOutcome> Preferences)
{
    public bool IsPlaced => Placed == PeopleCount;

    public bool IsSplit => TableNumbers.Count > 1;
}

/// <summary>For one preference rank: how many groups have such a preference, and for how many it is met.</summary>
public sealed record PreferenceRankStats(int Rank, int Wanted, int Met);

public sealed record PlanIssue(IssueSeverity Severity, string Message, Guid? GroupId = null);

/// <summary>A readable summary of a plan: score, per-group placement, preference fulfilment and issues.</summary>
public sealed class PlanSummary
{
    private PlanSummary(double score, int peoplePlaced, int peopleTotal, IReadOnlyList<GroupPlacement> groups,
        IReadOnlyList<PreferenceRankStats> preferenceStats, IReadOnlyList<PlanIssue> issues)
    {
        Score = score;
        PeoplePlaced = peoplePlaced;
        PeopleTotal = peopleTotal;
        Groups = groups;
        PreferenceStats = preferenceStats;
        Issues = issues;
    }

    public double Score { get; }

    public int PeoplePlaced { get; }

    public int PeopleTotal { get; }

    public IReadOnlyList<GroupPlacement> Groups { get; }

    public IReadOnlyList<PreferenceRankStats> PreferenceStats { get; }

    public IReadOnlyList<PlanIssue> Issues { get; }

    public IEnumerable<GroupPlacement> UnplacedGroups => Groups.Where(g => !g.IsPlaced);

    public int SplitGroups => Groups.Count(g => g.IsSplit);

    internal static PlanSummary Create(SeatingProblem problem, double score, IReadOnlyList<GroupPlacement> groups)
    {
        var stats = Enumerable.Range(1, Core.Model.Group.MaxPreferences)
            .Select(rank =>
            {
                var outcomes = groups.SelectMany(g => g.Preferences).Where(p => p.Rank == rank).ToList();
                return new PreferenceRankStats(rank, outcomes.Count, outcomes.Count(o => o.IsMet));
            })
            .Where(s => s.Wanted > 0)
            .ToList();

        var issues = new List<PlanIssue>();
        foreach (var g in groups)
        {
            if (g.Placed == 0)
                issues.Add(new(IssueSeverity.Warning, $"'{g.Name}' ({g.PeopleCount}) has no seats.", g.GroupId));
            else if (g.Placed < g.PeopleCount)
                issues.Add(new(IssueSeverity.Warning, $"Only {g.Placed} of {g.PeopleCount} in '{g.Name}' have seats.", g.GroupId));
            if (g.BreaksHardRule)
                issues.Add(new(IssueSeverity.Error, $"'{g.Name}' is not where its pin or the floor rule says.", g.GroupId));
            if (g.FloorCount > 1)
                issues.Add(new(IssueSeverity.Warning, $"'{g.Name}' is split across {g.FloorCount} floors.", g.GroupId));
            else if (g.HasDistantParts)
                issues.Add(new(IssueSeverity.Warning, $"'{g.Name}' is split over tables that are not next to each other.", g.GroupId));
            else if (g.SmallParts > 0)
                issues.Add(new(IssueSeverity.Warning, $"'{g.Name}' has a small part of its group alone at a table.", g.GroupId));
        }

        return new PlanSummary(score, groups.Sum(g => g.Placed), problem.PeopleCount, groups, stats, issues);
    }
}
