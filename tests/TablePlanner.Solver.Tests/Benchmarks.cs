using TablePlanner.Core;
using TablePlanner.Core.Model;

namespace TablePlanner.Solver.Tests;

/// <summary>Setups for solver tests and benchmarks: small, large, nearly full and impossible.</summary>
internal static class Benchmarks
{
    public static EventProject Example() => ExampleProject.Create();

    /// <summary>About 700 people in ~110 groups of 2–50 on two floors (36 tables of 20), with random preferences.</summary>
    public static EventProject Large(int seed = 1, double fill = 0.95)
    {
        var random = new Random(seed);
        var project = new EventProject { Name = "Large" };
        for (var f = 0; f < 2; f++)
        {
            var floor = new Floor { Name = $"Floor {f + 1}", Number = f + 1 };
            for (var row = 0; row < 6; row++)
                for (var col = 0; col < 3; col++)
                    floor.Tables.Add(new Table { Number = floor.NextTableNumber(), SeatsPerSide = 10, X = 4 + col * 6.5, Y = 4 + row * 2.5 });
            project.Setup.Floors.Add(floor);
        }

        var seats = project.Setup.AllTables.Sum(t => t.SeatCount);
        var people = (int)(seats * fill);
        var sizes = new List<int>();
        while (sizes.Sum() < people)
        {
            var size = random.NextDouble() < 0.1 ? random.Next(20, 51) : random.Next(2, 12);
            sizes.Add(Math.Min(size, people - sizes.Sum()));
        }
        var groups = sizes.Select((s, i) => new Group { Name = $"G{i + 1}", PeopleCount = s }).ToList();
        foreach (var group in groups)
        {
            var count = random.Next(0, 4);
            group.PreferredGroupIds = groups.Where(g => g != group).OrderBy(_ => random.Next()).Take(count).Select(g => g.Id).ToList();
        }
        project.Setup.Groups = groups;
        return project;
    }

    /// <summary>
    /// A venue with separate areas (192 seats): a main hall of 2 × 3 tables, a side room with a row of 3 tables 6 m
    /// away, and two single tables in corners. Groups of 2–30, about 95 % full, random preferences.
    /// </summary>
    public static EventProject Scattered(int seed = 1, double fill = 0.95)
    {
        var random = new Random(seed);
        var floor = new Floor { Name = "Hall", Number = 1 };
        foreach (var y in new[] { 4.0, 6.5 })
            foreach (var x in new[] { 4.0, 10.5, 17.0 })
                floor.Tables.Add(new Table { Number = floor.NextTableNumber(), SeatsPerSide = 10, X = x, Y = y });
        foreach (var x in new[] { 4.0, 9.3, 14.6 })
            floor.Tables.Add(new Table { Number = floor.NextTableNumber(), SeatsPerSide = 8, X = x, Y = 14 });
        floor.Tables.Add(new Table { Number = floor.NextTableNumber(), SeatsPerSide = 6, X = 26, Y = 3 });
        floor.Tables.Add(new Table { Number = floor.NextTableNumber(), SeatsPerSide = 6, X = 26, Y = 15 });

        var project = new EventProject { Name = "Scattered" };
        project.Setup.Floors.Add(floor);
        var people = (int)(project.Setup.AllTables.Sum(t => t.SeatCount) * fill);
        var sizes = new List<int>();
        while (sizes.Sum() < people)
        {
            var size = random.NextDouble() < 0.2 ? random.Next(12, 31) : random.Next(2, 10);
            sizes.Add(Math.Min(size, people - sizes.Sum()));
        }
        var groups = sizes.Select((s, i) => new Group { Name = $"G{i + 1}", PeopleCount = s }).ToList();
        foreach (var group in groups)
            group.PreferredGroupIds = groups.Where(g => g != group).OrderBy(_ => random.Next()).Take(random.Next(0, 3)).Select(g => g.Id).ToList();
        project.Setup.Groups = groups;
        return project;
    }

    /// <summary>
    /// Group levels: 5 departments of 2–4 teams (team sizes 3–14, some teams split into 2 subteams), about 90 % full,
    /// in a venue of two floors, each with a main room of 2 × 3 tables of 20 and a separate side room of 2 tables of 16.
    /// No preferences, so only keeping the levels together matters.
    /// </summary>
    public static EventProject Departments(int seed = 1, double fill = 0.9)
    {
        var random = new Random(seed);
        var project = new EventProject { Name = "Departments" };
        for (var f = 0; f < 2; f++)
        {
            var floor = new Floor { Name = $"Floor {f + 1}", Number = f + 1 };
            foreach (var y in new[] { 4.0, 6.5 })
                foreach (var x in new[] { 4.0, 10.5, 17.0 })
                    floor.Tables.Add(new Table { Number = floor.NextTableNumber(), SeatsPerSide = 10, X = x, Y = y });
            foreach (var x in new[] { 4.0, 9.3 })
                floor.Tables.Add(new Table { Number = floor.NextTableNumber(), SeatsPerSide = 8, X = x, Y = 14 });
            project.Setup.Floors.Add(floor);
        }

        var seats = project.Setup.AllTables.Sum(t => t.SeatCount);
        var people = 0;
        var groups = project.Setup.Groups;
        for (var d = 0; d < 5; d++)
        {
            var department = new Group { Name = $"Dept {d + 1}", PeopleCount = 0 };
            groups.Add(department);
            for (var t = 0; t < 2 + random.Next(3); t++)
            {
                var team = new Group { Name = $"Dept {d + 1} team {t + 1}", ParentId = department.Id, PeopleCount = random.Next(3, 15) };
                groups.Add(team);
                if (random.NextDouble() < 0.3)
                {
                    // Split into two subteams.
                    var half = team.PeopleCount / 2;
                    groups.Add(new Group { Name = $"{team.Name} a", ParentId = team.Id, PeopleCount = half });
                    groups.Add(new Group { Name = $"{team.Name} b", ParentId = team.Id, PeopleCount = team.PeopleCount - half });
                    team.PeopleCount = 0;
                }
                people += Math.Max(team.PeopleCount, 0) + groups.Where(g => g.ParentId == team.Id).Sum(g => g.PeopleCount);
            }
        }
        // Fill up with groups without a department, to the wanted share of seats.
        for (var i = 1; people < seats * fill; i++)
        {
            var size = Math.Min(random.Next(2, 9), (int)(seats * fill) - people + 1);
            groups.Add(new Group { Name = $"Guests {i}", PeopleCount = size });
            people += size;
        }
        return project;
    }

    /// <summary>The example with groups grown until there is only 1 seat to spare.</summary>
    public static EventProject NearlyFull()
    {
        var project = ExampleProject.Create();
        var seats = project.Setup.AllTables.Sum(t => t.AvailableSeatCount);
        var extra = seats - 1 - project.Setup.Groups.Sum(g => g.PeopleCount);
        project.Setup.Groups.Add(new Group { Name = "Late additions", PeopleCount = extra });
        return project;
    }

    /// <summary>The example with 40 more people than seats.</summary>
    public static EventProject TooFewSeats()
    {
        var project = ExampleProject.Create();
        var seats = project.Setup.AllTables.Sum(t => t.AvailableSeatCount);
        var people = project.Setup.Groups.Sum(g => g.PeopleCount);
        project.Setup.Groups.Add(new Group { Name = "Extra 1", PeopleCount = (seats - people) + 20 });
        project.Setup.Groups.Add(new Group { Name = "Extra 2", PeopleCount = 20 });
        return project;
    }
}
