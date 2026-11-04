using TablePlanner.Core.Model;

namespace TablePlanner.Core.Validation;

public enum Severity
{
    /// <summary>The solver can run, but not every rule can be met.</summary>
    Warning,

    /// <summary>The setup must be fixed before the solver can run.</summary>
    Error,
}

public enum IssueCode
{
    GroupNameMissing,
    GroupNameDuplicate,
    GroupSizeInvalid,
    TooManyPreferences,
    PreferenceUnknownGroup,
    PreferenceSelf,
    PreferenceDuplicate,
    TableSizeInvalid,
    TableEndSeatsInvalid,
    TableDimensionsInvalid,
    TableNumberDuplicate,
    DisabledSeatInvalid,
    FloorNameDuplicate,
    NotEnoughSeats,
    PinUnknownGroup,
    PinDuplicateGroup,
    PinTargetMissing,
    PinTargetTooSmall,
    PinSeatInvalid,
    PinSeatCountMismatch,
    PinSeatsOverlap,
    PinsExceedFloorCapacity,
}

/// <summary>A problem found in the setup. <see cref="SubjectId"/> is the group, table, floor or pin concerned.</summary>
public sealed record ValidationIssue(Severity Severity, IssueCode Code, string Message, Guid? SubjectId = null);

public sealed class ValidationResult
{
    public ValidationResult(IReadOnlyList<ValidationIssue> issues) => Issues = issues;

    public IReadOnlyList<ValidationIssue> Issues { get; }

    public bool HasErrors => Issues.Any(i => i.Severity == Severity.Error);

    public IEnumerable<ValidationIssue> Errors => Issues.Where(i => i.Severity == Severity.Error);

    public IEnumerable<ValidationIssue> Warnings => Issues.Where(i => i.Severity == Severity.Warning);
}

/// <summary>Checks a setup before solving, so impossible cases are reported up front with a clear message.</summary>
public static class SetupValidator
{
    public static ValidationResult Validate(EventSetup setup)
    {
        var issues = new List<ValidationIssue>();
        ValidateGroups(setup, issues);
        ValidateFloorsAndTables(setup, issues);
        ValidateCapacity(setup, issues);
        ValidatePins(setup, issues);
        return new ValidationResult(issues);
    }

    private static void ValidateGroups(EventSetup setup, List<ValidationIssue> issues)
    {
        var groupIds = setup.Groups.Select(g => g.Id).ToHashSet();

        foreach (var group in setup.Groups)
        {
            var label = string.IsNullOrWhiteSpace(group.Name) ? "A group" : $"Group '{group.Name}'";

            if (string.IsNullOrWhiteSpace(group.Name))
                issues.Add(Error(IssueCode.GroupNameMissing, "A group has no name.", group.Id));
            if (group.PeopleCount <= 0)
                issues.Add(Error(IssueCode.GroupSizeInvalid, $"{label} must have at least 1 person.", group.Id));
            if (group.PreferredGroupIds.Count > Group.MaxPreferences)
                issues.Add(Error(IssueCode.TooManyPreferences, $"{label} has more than {Group.MaxPreferences} preferences.", group.Id));
            if (group.PreferredGroupIds.Any(id => !groupIds.Contains(id)))
                issues.Add(Error(IssueCode.PreferenceUnknownGroup, $"{label} prefers a group that does not exist.", group.Id));
            if (group.PreferredGroupIds.Contains(group.Id))
                issues.Add(Error(IssueCode.PreferenceSelf, $"{label} lists itself as a preference.", group.Id));
            if (group.PreferredGroupIds.Distinct().Count() != group.PreferredGroupIds.Count)
                issues.Add(Error(IssueCode.PreferenceDuplicate, $"{label} lists the same preference more than once.", group.Id));
        }

        var duplicateNames = setup.Groups
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .GroupBy(g => g.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(d => d.Count() > 1);
        foreach (var duplicate in duplicateNames)
            issues.Add(Error(IssueCode.GroupNameDuplicate, $"Group name '{duplicate.Key}' is used {duplicate.Count()} times.", duplicate.First().Id));
    }

    private static void ValidateFloorsAndTables(EventSetup setup, List<ValidationIssue> issues)
    {
        foreach (var duplicate in setup.Floors.GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(d => d.Count() > 1))
            issues.Add(Warning(IssueCode.FloorNameDuplicate, $"Floor name '{duplicate.Key}' is used {duplicate.Count()} times.", duplicate.First().Id));

        foreach (var table in setup.AllTables)
        {
            if (table.SeatsPerSide < 1)
                issues.Add(Error(IssueCode.TableSizeInvalid, $"Table {table.Number} must have at least 1 seat per side.", table.Id));
            if (table.EndSeats is < 0 or > Table.MaxEndSeats)
                issues.Add(Error(IssueCode.TableEndSeatsInvalid, $"Table {table.Number} must have 0, 1 or 2 end seats.", table.Id));
            if (table.SeatSpacing <= 0 || table.Width <= 0)
                issues.Add(Error(IssueCode.TableDimensionsInvalid, $"Table {table.Number} must have a positive seat spacing and width.", table.Id));
            if (table.DisabledSeats.Any(i => !table.IsValidSeatIndex(i)))
                issues.Add(Warning(IssueCode.DisabledSeatInvalid, $"Table {table.Number} has disabled seats that do not exist.", table.Id));
        }

        foreach (var duplicate in setup.AllTables.GroupBy(t => t.Number).Where(d => d.Count() > 1))
            issues.Add(Error(IssueCode.TableNumberDuplicate, $"Table number {duplicate.Key} is used {duplicate.Count()} times.", duplicate.First().Id));
    }

    private static void ValidateCapacity(EventSetup setup, List<ValidationIssue> issues)
    {
        var people = setup.Groups.Where(g => g.PeopleCount > 0).Sum(g => g.PeopleCount);
        var seats = setup.AllTables.Sum(t => t.AvailableSeatCount);
        if (people > seats)
            issues.Add(Warning(IssueCode.NotEnoughSeats,
                $"{people} people but only {seats} seats: {people - seats} seats short. Not all groups can be placed."));
    }

    private static void ValidatePins(EventSetup setup, List<ValidationIssue> issues)
    {
        foreach (var duplicate in setup.Pins.GroupBy(p => p.GroupId).Where(d => d.Count() > 1))
            issues.Add(Error(IssueCode.PinDuplicateGroup, $"{GroupLabel(setup, duplicate.Key)} is pinned more than once.", duplicate.Key));

        var pinnedSeatsByFloor = new Dictionary<Guid, int>();

        foreach (var pin in setup.Pins)
        {
            var group = setup.FindGroup(pin.GroupId);
            if (group is null)
            {
                issues.Add(Error(IssueCode.PinUnknownGroup, "A pin refers to a group that does not exist.", pin.Id));
                continue;
            }

            var label = GroupLabel(setup, group.Id);
            switch (pin.Scope)
            {
                case PinScope.Floor:
                    var floor = setup.Floors.FirstOrDefault(f => f.Id == pin.FloorId);
                    if (floor is null)
                    {
                        issues.Add(Error(IssueCode.PinTargetMissing, $"{label} is pinned to a floor that does not exist.", pin.Id));
                        break;
                    }
                    if (floor.Tables.Sum(t => t.AvailableSeatCount) < group.PeopleCount)
                        issues.Add(Error(IssueCode.PinTargetTooSmall, $"{label} ({group.PeopleCount}) does not fit on floor '{floor.Name}'.", pin.Id));
                    AddPinned(pinnedSeatsByFloor, floor.Id, group.PeopleCount);
                    break;

                case PinScope.Table:
                    var table = setup.FindTable(pin.TableId ?? Guid.Empty);
                    if (table is null)
                    {
                        issues.Add(Error(IssueCode.PinTargetMissing, $"{label} is pinned to a table that does not exist.", pin.Id));
                        break;
                    }
                    if (table.AvailableSeatCount < group.PeopleCount)
                        issues.Add(Error(IssueCode.PinTargetTooSmall, $"{label} ({group.PeopleCount}) does not fit at table {table.Number} ({table.AvailableSeatCount} seats).", pin.Id));
                    AddPinned(pinnedSeatsByFloor, FloorOf(setup, table.Id)!.Id, group.PeopleCount);
                    break;

                case PinScope.Seats:
                    var invalid = pin.Seats.Any(s => setup.FindTable(s.TableId) is not { } t || !t.IsValidSeatIndex(s.Index) || t.DisabledSeats.Contains(s.Index));
                    if (invalid)
                        issues.Add(Error(IssueCode.PinSeatInvalid, $"{label} is pinned to seats that do not exist or are disabled.", pin.Id));
                    if (pin.Seats.Distinct().Count() != group.PeopleCount)
                        issues.Add(Error(IssueCode.PinSeatCountMismatch, $"{label} has {group.PeopleCount} people but is pinned to {pin.Seats.Distinct().Count()} seats.", pin.Id));
                    foreach (var byFloor in pin.Seats.Distinct().Select(s => FloorOf(setup, s.TableId)).OfType<Floor>().GroupBy(f => f.Id))
                        AddPinned(pinnedSeatsByFloor, byFloor.Key, byFloor.Count());
                    break;
            }
        }

        var overlapping = setup.Pins.Where(p => p.Scope == PinScope.Seats)
            .SelectMany(p => p.Seats.Distinct().Select(s => (Seat: s, p.GroupId)))
            .GroupBy(x => x.Seat)
            .Where(g => g.Select(x => x.GroupId).Distinct().Count() > 1);
        foreach (var seat in overlapping)
            issues.Add(Error(IssueCode.PinSeatsOverlap, $"Seat {seat.Key.Index} at table {setup.FindTable(seat.Key.TableId)?.Number} is pinned to more than one group."));

        foreach (var (floorId, pinned) in pinnedSeatsByFloor)
        {
            var floor = setup.Floors.First(f => f.Id == floorId);
            var capacity = floor.Tables.Sum(t => t.AvailableSeatCount);
            if (pinned > capacity)
                issues.Add(Error(IssueCode.PinsExceedFloorCapacity, $"Groups pinned to floor '{floor.Name}' need {pinned} seats, but it has {capacity}.", floorId));
        }
    }

    private static void AddPinned(Dictionary<Guid, int> pinnedByFloor, Guid floorId, int seats) =>
        pinnedByFloor[floorId] = pinnedByFloor.GetValueOrDefault(floorId) + seats;

    private static Floor? FloorOf(EventSetup setup, Guid tableId) =>
        setup.Floors.FirstOrDefault(f => f.Tables.Any(t => t.Id == tableId));

    private static string GroupLabel(EventSetup setup, Guid groupId) =>
        setup.FindGroup(groupId) is { } g && !string.IsNullOrWhiteSpace(g.Name) ? $"Group '{g.Name}'" : "A group";

    private static ValidationIssue Error(IssueCode code, string message, Guid? subject = null) => new(Severity.Error, code, message, subject);

    private static ValidationIssue Warning(IssueCode code, string message, Guid? subject = null) => new(Severity.Warning, code, message, subject);
}
