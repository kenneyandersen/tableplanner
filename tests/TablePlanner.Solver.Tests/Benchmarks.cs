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
