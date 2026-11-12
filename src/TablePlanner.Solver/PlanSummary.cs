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
    int PeopleApart,
    int SmallParts,
    bool BreaksHardRule,
    IReadOnlyList<PreferenceOutcome> Preferences)
{
    public bool IsPlaced => Placed == PeopleCount;

    public bool IsSplit => TableNumbers.Count > 1;
}

/// <summary>
/// How a parent group (a department of teams) is seated: where its groups sit, and how many of its people are away from
/// its main part (tables that are the same or next to each other).
/// </summary>
public sealed record ParentPlacement(
    Guid GroupId,
    string Name,
    int Level,
    int PeopleCount,
    int Placed,
    IReadOnlyList<int> TableNumbers,
    int FloorCount,
    bool IsSpread,
    int PeopleApart,
    IReadOnlyList<PreferenceOutcome> Preferences);

/// <summary>For one preference rank: how many groups have such a preference, and for how many it is met.</summary>
public sealed record PreferenceRankStats(int Rank, int Wanted, int Met);

public sealed record PlanIssue(IssueSeverity Severity, string Message, Guid? GroupId = null);

/// <summary>A readable summary of a plan: score, per-group placement, preference fulfilment and issues.</summary>
public sealed class PlanSummary
{
    private PlanSummary(double score, int peoplePlaced, int peopleTotal, IReadOnlyList<GroupPlacement> groups,
        IReadOnlyList<ParentPlacement> parentGroups, IReadOnlyList<PreferenceRankStats> preferenceStats, IReadOnlyList<PlanIssue> issues)
    {
        Score = score;
        PeoplePlaced = peoplePlaced;
        PeopleTotal = peopleTotal;
        Groups = groups;
        ParentGroups = parentGroups;
        PreferenceStats = preferenceStats;
        Issues = issues;
    }

    public double Score { get; }

    public int PeoplePlaced { get; }

    public int PeopleTotal { get; }

    public IReadOnlyList<GroupPlacement> Groups { get; }

    /// <summary>Parent groups (with seated groups below them); empty when the groups have no levels.</summary>
    public IReadOnlyList<ParentPlacement> ParentGroups { get; }

    public IReadOnlyList<PreferenceRankStats> PreferenceStats { get; }

    public IReadOnlyList<PlanIssue> Issues { get; }

    /// <summary>The issues weighed by tier, for comparing plans: one serious issue outweighs any number of warnings, and one warning any number of minor ones.</summary>
    public int IssueWeight => Issues.Sum(i => i.Severity switch { IssueSeverity.Error => 10_000, IssueSeverity.Warning => 100, _ => 1 });

    /// <summary>Only minor issues, or none.</summary>
    public bool OnlyMinorIssues => Issues.All(i => i.Severity == IssueSeverity.Minor);

    public IEnumerable<GroupPlacement> UnplacedGroups => Groups.Where(g => !g.IsPlaced);

    public int SplitGroups => Groups.Count(g => g.IsSplit);

    internal static PlanSummary Create(SeatingProblem problem, double score, IReadOnlyList<GroupPlacement> groups, IReadOnlyList<ParentPlacement>? parents = null)
    {
        parents ??= [];
        var stats = Enumerable.Range(1, Core.Model.Group.MaxPreferences)
            .Select(rank =>
            {
                var outcomes = groups.SelectMany(g => g.Preferences).Concat(parents.SelectMany(p => p.Preferences)).Where(p => p.Rank == rank).ToList();
                return new PreferenceRankStats(rank, outcomes.Count, outcomes.Count(o => o.IsMet));
            })
            .Where(s => s.Wanted > 0)
            .ToList();

        // Three tiers: serious (people without seats, a broken pin, a group split across floors or far apart, a main
        // group split across floors), warnings (a parent group spread over the room) and minor (a small part of a group
        // alone at a table). Listed serious first.
        var issues = new List<PlanIssue>();
        foreach (var g in groups)
        {
            if (g.Placed == 0)
                issues.Add(new(IssueSeverity.Error, $"'{g.Name}' ({g.PeopleCount}) has no seats.", g.GroupId));
            else if (g.Placed < g.PeopleCount)
                issues.Add(new(IssueSeverity.Error, $"Only {g.Placed} of {g.PeopleCount} in '{g.Name}' have seats.", g.GroupId));
            if (g.BreaksHardRule)
                issues.Add(new(IssueSeverity.Error, $"'{g.Name}' is not where its pin or the floor rule says.", g.GroupId));
            if (g.FloorCount > 1)
                issues.Add(new(IssueSeverity.Error, $"'{g.Name}' is split across {g.FloorCount} floors.", g.GroupId));
            else if (g.HasDistantParts)
                issues.Add(new(IssueSeverity.Error,
                    $"'{g.Name}' is split over tables that are not next to each other ({g.PeopleApart} of {g.Placed} away from the rest).", g.GroupId));
            else if (g.SmallParts > 0)
                issues.Add(new(IssueSeverity.Minor, $"'{g.Name}' has a small part of its group alone at a table.", g.GroupId));
        }

        // Parent groups: spread over floors (serious) or over parts of the room that are not next to each other.
        foreach (var p in parents.OrderBy(p => p.Level))
        {
            if (p.FloorCount > 1)
                issues.Add(new(IssueSeverity.Error, $"'{p.Name}' is split across {p.FloorCount} floors ({p.PeopleApart} of {p.Placed} away from the rest).", p.GroupId));
            else if (p.IsSpread)
                issues.Add(new(IssueSeverity.Warning, $"'{p.Name}' is spread over parts of the room that are not next to each other ({p.PeopleApart} of {p.Placed} away from the rest).", p.GroupId));
        }
        issues = [.. issues.OrderByDescending(i => i.Severity)];

        return new PlanSummary(score, groups.Sum(g => g.Placed), problem.PeopleCount, groups, parents, stats, issues);
    }
}

/// <summary>Table numbers as short text: runs of three or more are written as a range, e.g. "102, 108, 111–114".</summary>
public static class TableNumberText
{
    public static string Format(IEnumerable<int> numbers)
    {
        var sorted = numbers.Distinct().Order().ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1)
                j++;
            if (j - i >= 2)
                parts.Add($"{sorted[i]}–{sorted[j]}");
            else
                for (var k = i; k <= j; k++)
                    parts.Add(sorted[k].ToString());
            i = j + 1;
        }
        return string.Join(", ", parts);
    }
}
