using TablePlanner.Core.Model;

namespace TablePlanner.Core.Editing;

/// <summary>What restoring a saved plan had to leave out because the setup changed since it was saved.</summary>
public sealed record RestoreReport(int SeatsDropped, int GroupsMissing, IReadOnlyList<string> GroupsWithWrongCount)
{
    public bool IsClean => SeatsDropped == 0 && GroupsMissing == 0 && GroupsWithWrongCount.Count == 0;
}

/// <summary>Edits to the seating plan, pins and saved plans. Like <see cref="SetupOperations"/>, each is one undoable step when run through <see cref="EditHistory"/>.</summary>
public static class PlanOperations
{
    /// <summary>
    /// Gives each listed group exactly the listed seats, taking them from whoever sat there; for a move one
    /// group, for a swap two. Groups not listed keep their other seats. A seat pin of a moved group follows it.
    /// </summary>
    public static void SeatGroups(EventProject project, IReadOnlyDictionary<Guid, IReadOnlyList<SeatId>> seatsByGroup)
    {
        var plan = project.CurrentAssignment;
        var targets = seatsByGroup.Values.SelectMany(s => s).ToHashSet();
        plan.Seats.RemoveAll(s => seatsByGroup.ContainsKey(s.GroupId) || targets.Contains(s.Seat));
        foreach (var (groupId, seats) in seatsByGroup)
        {
            plan.Seats.AddRange(seats.Select(s => new SeatAssignment(s, groupId)));
            if (project.Setup.Pins.FirstOrDefault(p => p.GroupId == groupId && p.Scope == PinScope.Seats) is { } pin)
                pin.Seats = [.. seats];
        }
    }

    /// <summary>Takes a group out of the plan, and drops its seat pin, which would otherwise put it back.</summary>
    public static void UnseatGroup(EventProject project, Guid groupId)
    {
        project.CurrentAssignment.Seats.RemoveAll(s => s.GroupId == groupId);
        project.Setup.Pins.RemoveAll(p => p.GroupId == groupId && p.Scope == PinScope.Seats);
    }

    public static void ReplacePlan(EventProject project, SeatingAssignment assignment) =>
        project.CurrentAssignment = assignment.Clone();

    // ---- Pins ----

    /// <summary>Pins a group to its current seats, so the solver leaves it there. Replaces any other pin of the group.</summary>
    public static bool Lock(EventProject project, Guid groupId)
    {
        var seats = project.CurrentAssignment.SeatsOf(groupId).ToList();
        if (seats.Count == 0)
            return false;
        SetPin(project, new Pin { GroupId = groupId, Scope = PinScope.Seats, Seats = seats });
        return true;
    }

    /// <summary>Locks every group that has seats. Returns how many were locked.</summary>
    public static int LockAll(EventProject project) =>
        project.CurrentAssignment.Seats.Select(s => s.GroupId).Distinct().ToList().Count(g => Lock(project, g));

    public static void PinToTable(EventProject project, Guid groupId, Guid tableId) =>
        SetPin(project, new Pin { GroupId = groupId, Scope = PinScope.Table, TableId = tableId });

    public static void PinToFloor(EventProject project, Guid groupId, Guid floorId) =>
        SetPin(project, new Pin { GroupId = groupId, Scope = PinScope.Floor, FloorId = floorId });

    public static void Unpin(EventProject project, Guid groupId) => project.Setup.Pins.RemoveAll(p => p.GroupId == groupId);

    /// <summary>Removes every seat pin (lock); table and floor pins stay. Returns how many were removed.</summary>
    public static int UnlockAll(EventProject project) => project.Setup.Pins.RemoveAll(p => p.Scope == PinScope.Seats);

    private static void SetPin(EventProject project, Pin pin)
    {
        project.Setup.Pins.RemoveAll(p => p.GroupId == pin.GroupId);
        project.Setup.Pins.Add(pin);
    }

    // ---- Saved plans ----

    public static SavedAssignment SavePlan(EventProject project, string name, DateTimeOffset savedAt, PlanStats? stats)
    {
        var saved = new SavedAssignment
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Plan {project.SavedAssignments.Count + 1}" : name.Trim(),
            SavedAt = savedAt,
            Priorities = project.Priorities.Clone(),
            Assignment = project.CurrentAssignment.Clone(),
            Stats = stats,
        };
        project.SavedAssignments.Add(saved);
        return saved;
    }

    /// <summary>
    /// Makes a saved plan the current plan. Seats that no longer exist (or are disabled) and groups that were
    /// deleted are left out; groups whose size changed are kept and reported.
    /// </summary>
    public static RestoreReport RestorePlan(EventProject project, Guid savedId)
    {
        var saved = project.SavedAssignments.FirstOrDefault(s => s.Id == savedId)
            ?? throw new ArgumentException("Unknown saved plan.", nameof(savedId));
        var setup = project.Setup;
        var kept = new List<SeatAssignment>();
        var taken = new HashSet<SeatId>();
        var seatsDropped = 0;
        var missingGroups = new HashSet<Guid>();
        foreach (var seat in saved.Assignment.Seats)
        {
            if (setup.FindGroup(seat.GroupId) is null)
            {
                missingGroups.Add(seat.GroupId);
                continue;
            }
            if (setup.FindTable(seat.Seat.TableId) is not { } table || !table.IsValidSeatIndex(seat.Seat.Index)
                || table.DisabledSeats.Contains(seat.Seat.Index) || !taken.Add(seat.Seat))
            {
                seatsDropped++;
                continue;
            }
            kept.Add(seat);
        }
        project.CurrentAssignment = new SeatingAssignment { Seats = kept };

        var wrongCount = kept.GroupBy(s => s.GroupId)
            .Select(g => (Group: setup.FindGroup(g.Key)!, Count: g.Count()))
            .Where(x => x.Group.PeopleCount != x.Count)
            .Select(x => x.Group.Name)
            .ToList();
        return new RestoreReport(seatsDropped, missingGroups.Count, wrongCount);
    }

    public static void RenamePlan(EventProject project, Guid savedId, string name)
    {
        if (project.SavedAssignments.FirstOrDefault(s => s.Id == savedId) is { } saved && !string.IsNullOrWhiteSpace(name))
            saved.Name = name.Trim();
    }

    public static void DeletePlan(EventProject project, Guid savedId) => project.SavedAssignments.RemoveAll(s => s.Id == savedId);
}
