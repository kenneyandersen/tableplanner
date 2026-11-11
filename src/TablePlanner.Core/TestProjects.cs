using TablePlanner.Core.Model;

namespace TablePlanner.Core;

/// <summary>
/// Made-up projects for trying the solver: a wedding of round tables, a company dinner on two floors, a club party in
/// separate rooms, a conference lunch, a large festival, and a company with departments, teams and subteams. Each gets
/// a number of people relative to its seats (<c>fill</c>: 0.95 is 95 % of the seats; above 1 there are too few seats),
/// random groups, preferences and some names. Not checked against the solver on purpose: they are for testing it.
/// </summary>
public static class TestProjects
{
    /// <summary>All of them, each with its own fill: from roomy to a little too full.</summary>
    public static List<EventProject> All(Random random)
    {
        var projects = new List<(string Name, Func<Random, double, EventProject> Make, double Fill)>
        {
            ("Wedding", Wedding, 0.97 + random.NextDouble() * 0.03),
            ("Company dinner", CompanyDinner, 0.92 + random.NextDouble() * 0.06),
            ("Club party", ClubParty, 1.0),
            ("Conference lunch", ConferenceLunch, 0.85 + random.NextDouble() * 0.1),
            ("Festival", Festival, 0.95 + random.NextDouble() * 0.04),
            ("Company with departments", CompanyLevels, 0.93 + random.NextDouble() * 0.06),
            ("Overbooked party", ClubParty, 1.03 + random.NextDouble() * 0.04),
        };
        return projects.Select(p =>
        {
            var project = p.Make(random, p.Fill);
            var seats = project.Setup.AllTables.Sum(t => t.AvailableSeatCount);
            var people = project.Setup.Groups.Sum(g => g.PeopleCount);
            project.Name = $"Test – {p.Name} ({people} people, {seats} seats)";
            AddNames(random, project.Setup, 0.4);
            return project;
        }).ToList();
    }

    /// <summary>The number of people for a project's seats and fill.</summary>
    private static int People(EventProject project, double fill) =>
        Math.Max(1, (int)Math.Round(project.Setup.AllTables.Sum(t => t.AvailableSeatCount) * fill));

    /// <summary>Names for about <paramref name="share"/> of the bottom groups: all of their people, or most of them.</summary>
    private static void AddNames(Random random, EventSetup setup, double share)
    {
        foreach (var group in setup.Groups.Where(g => g.PeopleCount > 0 && !setup.HasChildren(g.Id)))
        {
            if (random.NextDouble() >= share)
                continue;
            var named = random.NextDouble() < 0.6 ? group.PeopleCount : Math.Max(1, group.PeopleCount - random.Next(1, 3));
            for (var i = 0; i < named; i++)
                group.Members.Add(new Person { Name = $"{FirstNames[random.Next(FirstNames.Length)]} {LastNames[random.Next(LastNames.Length)]}" });
        }
    }

    private static readonly string[] FirstNames =
    [
        "Anna", "Bo", "Carl", "Dorte", "Erik", "Frida", "Gustav", "Hanne", "Ida", "Jonas", "Karen", "Lars", "Mette", "Niels",
        "Olga", "Peter", "Rikke", "Søren", "Tina", "Ulrik", "Vera", "William", "Yasmin", "Zara", "Emil", "Freja", "Oscar", "Alma",
    ];

    private static readonly string[] LastNames =
    [
        "Hansen", "Jensen", "Nielsen", "Pedersen", "Andersen", "Larsen", "Sørensen", "Rasmussen", "Holm", "Bach", "Lund",
        "Krog", "Dahl", "Berg", "Skov", "Møller", "Kjær", "Friis", "Ravn", "Lind",
    ];

    // ---- The venues ----

    /// <summary>
    /// 17 round tables (10 and 8 seats) in a grid around a dance floor, and a long head table with its two outer seats
    /// disabled (178 seats). Families and friends of 2–12, with many preferences.
    /// </summary>
    public static EventProject Wedding(Random random, double fill)
    {
        var floor = new Floor { Name = "Hall", Number = 1 };
        floor.Elements.Add(new FloorElement { Kind = FloorElementKind.DanceFloor, Label = "Dance floor", X = 11, Y = 9, Width = 6, Depth = 5 });
        floor.Elements.Add(new FloorElement { Kind = FloorElementKind.Bar, Label = "Bar", X = 23, Y = 14, Width = 3, Depth = 1.5 });

        var head = new Table { Number = 1, SeatsPerSide = 8, X = 11, Y = 2.5 };
        head.DisabledSeats = [0, 7];
        floor.Tables.Add(head);
        var number = 2;
        // Two rows of round tables above and below, 3.4 m apart (adjacent), and a column on each side.
        foreach (var (x, y) in new[] { (5.0, 5.5), (8.4, 5.5), (13.6, 5.5), (17.0, 5.5) })
            floor.Tables.Add(Round(number++, 10, x, y));
        foreach (var (x, y) in new[] { (4.0, 9.0), (18.0, 9.0) })
            floor.Tables.Add(Round(number++, 8, x, y));
        foreach (var (x, y) in new[] { (5.0, 12.5), (8.4, 12.5), (11.8, 12.5), (15.2, 12.5), (18.6, 12.5) })
            floor.Tables.Add(Round(number++, 10, x, y));
        foreach (var (x, y) in new[] { (5.0, 15.9), (8.4, 15.9), (11.8, 15.9), (15.2, 15.9), (18.6, 15.9), (23.0, 9.0) })
            floor.Tables.Add(Round(number++, x == 23 ? 8 : 10, x, y));

        var project = new EventProject();
        project.Setup.Floors.Add(floor);
        project.Setup.Groups = Groups(random, People(project, fill), () => random.NextDouble() < 0.15 ? random.Next(8, 13) : random.Next(2, 7),
            FamilyNames, preferenceChance: 0.8, maxPreferences: 3);
        return project;
    }

    /// <summary>
    /// Two floors. Ground: a hall with 6 rows of two 20-seat tables end to end, and a side room (not adjacent) with
    /// 8 round tables of 8. Gallery: three rows of 16-seat tables and a separate corner table. A pillar blocks 4 seats.
    /// 458 seats; departments of 25–60 and small groups.
    /// </summary>
    public static EventProject CompanyDinner(Random random, double fill)
    {
        var ground = new Floor { Name = "Ground floor", Number = 1 };
        var number = 101;
        for (var row = 0; row < 6; row++)
            for (var col = 0; col < 2; col++)
                ground.Tables.Add(new Table { Number = number++, SeatsPerSide = 10, X = 5 + col * 6.5, Y = 3 + row * 2.5 });
        // The pillar between rows 2 and 3, at the left end of the right-hand tables.
        ground.Tables[3].DisabledSeats = [10, 11];
        ground.Tables[5].DisabledSeats = [0, 1];
        for (var i = 0; i < 8; i++)
            ground.Tables.Add(Round(number++, 8, 21 + (i % 4) * 3, 4 + (i / 4) * 3.2));
        ground.Elements.Add(new FloorElement { Kind = FloorElementKind.Stage, Label = "Stage", X = 8, Y = 19, Width = 6, Depth = 2.5 });
        ground.Elements.Add(new FloorElement { Kind = FloorElementKind.Stairs, Label = "Stairs", X = 18, Y = 19, Width = 2, Depth = 3 });

        var gallery = new Floor { Name = "Gallery", Number = 2 };
        number = 201;
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 3; col++)
                gallery.Tables.Add(new Table { Number = number++, SeatsPerSide = 8, X = 4 + col * 5.3, Y = 3 + row * 2.5 });
        gallery.Tables.Add(new Table { Number = number, SeatsPerSide = 6, EndSeats = 2, X = 24, Y = 9 });

        var project = new EventProject();
        project.Setup.Floors.AddRange([ground, gallery]);
        project.Setup.Groups = Groups(random, People(project, fill), () => random.NextDouble() < 0.25 ? random.Next(25, 61) : random.Next(2, 9),
            DepartmentNames, preferenceChance: 0.5, maxPreferences: 2);
        return project;
    }

    /// <summary>
    /// 220 seats in three separate areas of long tables of different sizes (a main room, a long
    /// vertical row and a small back room) with a few disabled seats by the doors.
    /// </summary>
    public static EventProject ClubParty(Random random, double fill)
    {
        var floor = new Floor { Name = "Clubhouse", Number = 1 };
        var number = 1;
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 2; col++)
                floor.Tables.Add(new Table { Number = number++, SeatsPerSide = 12, EndSeats = col == 0 ? 1 : 0, X = 5 + col * 7.7, Y = 3 + row * 2.5 });
        floor.Tables[0].DisabledSeats = [24];
        // A long vertical row by the windows: three tables end to end.
        for (var i = 0; i < 3; i++)
            floor.Tables.Add(new Table { Number = number++, SeatsPerSide = 8, Rotation = Rotation.R90, X = 22, Y = 3.5 + i * 5.3 });
        floor.Tables[^1].DisabledSeats = [7, 15];
        // The back room.
        for (var i = 0; i < 2; i++)
            floor.Tables.Add(new Table { Number = number++, SeatsPerSide = 7, X = 5 + i * 4.7, Y = 15 });
        floor.Elements.Add(new FloorElement { Kind = FloorElementKind.Bar, Label = "Bar", X = 17, Y = 13, Width = 4, Depth = 1.5 });
        floor.Elements.Add(new FloorElement { Kind = FloorElementKind.Entrance, Label = "Entrance", X = 1, Y = 10, Width = 1, Depth = 2 });

        var project = new EventProject();
        project.Setup.Floors.Add(floor);
        project.Setup.Groups = Groups(random, People(project, fill), () => random.NextDouble() < 0.2 ? random.Next(12, 26) : random.Next(4, 11),
            TeamNames, preferenceChance: 0.3, maxPreferences: 2);
        return project;
    }

    /// <summary>
    /// 16 round tables of 6 and 8 (110 seats, two disabled by a column) for small groups of
    /// 1–6 with many mutual preferences.
    /// </summary>
    public static EventProject ConferenceLunch(Random random, double fill)
    {
        var floor = new Floor { Name = "Foyer", Number = 1 };
        var number = 1;
        for (var row = 0; row < 4; row++)
            for (var col = 0; col < 4; col++)
                floor.Tables.Add(Round(number++, (row + col) % 2 == 0 ? 8 : 6, 3 + col * 3.6, 3 + row * 3.6));
        floor.Tables[5].DisabledSeats = [3, 4];
        floor.Elements.Add(new FloorElement { Kind = FloorElementKind.Buffet, Label = "Buffet", X = 18, Y = 8, Width = 2, Depth = 8, Rotation = Rotation.R0 });

        var project = new EventProject();
        project.Setup.Floors.Add(floor);
        var groups = Groups(random, People(project, fill), () => random.Next(1, 7), CompanyNames, preferenceChance: 0.9, maxPreferences: 4);
        // Make about half of the preferences mutual.
        foreach (var group in groups)
            foreach (var id in group.PreferredGroupIds.ToList())
            {
                var other = groups.First(g => g.Id == id);
                if (random.NextDouble() < 0.5 && !other.PreferredGroupIds.Contains(group.Id) && other.PreferredGroupIds.Count < Group.MaxPreferences)
                    other.PreferredGroupIds.Add(group.Id);
            }
        project.Setup.Groups = groups;
        return project;
    }

    /// <summary>
    /// Large: three floors with 54 long tables of 16–24 and 12 round ones, scattered disabled seats, about
    /// 1165 seats; groups of 2–30 and a few of 50–90 (larger than any table).
    /// </summary>
    public static EventProject Festival(Random random, double fill)
    {
        var project = new EventProject();
        var number = 0;
        for (var f = 0; f < 3; f++)
        {
            var floor = new Floor { Name = f == 0 ? "Main hall" : $"Level {f}", Number = f + 1 };
            number = (f + 1) * 100 + 1;
            var rows = f == 0 ? 8 : 5;
            var perSide = f == 2 ? 8 : 10 + f;
            for (var row = 0; row < rows; row++)
                for (var col = 0; col < 3; col++)
                {
                    var table = new Table { Number = number++, SeatsPerSide = perSide, X = 4 + col * (perSide * 0.6 + 0.6), Y = 3 + row * 2.5 };
                    if (random.NextDouble() < 0.12)
                        table.DisabledSeats = [random.Next(table.SeatCount)];
                    floor.Tables.Add(table);
                }
            // A cluster of round tables away from the rows.
            var roundX = 4 + 3 * (perSide * 0.6 + 0.6) + 4;
            for (var i = 0; i < (f == 1 ? 6 : 3); i++)
                floor.Tables.Add(Round(number++, 10, roundX + (i % 3) * 3.4, 4 + (i / 3) * 3.4));
            floor.Elements.Add(new FloorElement { Kind = FloorElementKind.Stairs, Label = "Stairs", X = roundX + 3, Y = 14, Width = 2, Depth = 3 });
            project.Setup.Floors.Add(floor);
        }

        project.Setup.Groups = Groups(random, People(project, fill), () => random.NextDouble() switch
        {
            < 0.04 => random.Next(50, 91),
            < 0.25 => random.Next(12, 31),
            _ => random.Next(2, 12),
        }, TeamNames, preferenceChance: 0.4, maxPreferences: 3);
        return project;
    }

    /// <summary>
    /// Group levels: 6 departments with 2–4 teams of 6–20 (some teams in 2 subteams) and a few groups without a
    /// department; 467 seats on two floors (a hall of long tables of 22 and a side room of round tables
    /// of 10; upstairs rows of tables of 16, 3 seats disabled). Preferences between departments and between teams.
    /// </summary>
    public static EventProject CompanyLevels(Random random, double fill)
    {
        var ground = new Floor { Name = "Ground floor", Number = 1 };
        var number = 101;
        for (var row = 0; row < 5; row++)
            for (var col = 0; col < 2; col++)
                ground.Tables.Add(new Table { Number = number++, SeatsPerSide = 11, X = 5 + col * 7.2, Y = 3 + row * 2.5 });
        for (var i = 0; i < 6; i++)
            ground.Tables.Add(Round(number++, 10, 22 + (i % 3) * 3.4, 4 + (i / 3) * 3.4));
        var upstairs = new Floor { Name = "First floor", Number = 2 };
        number = 201;
        for (var row = 0; row < 4; row++)
            for (var col = 0; col < 3; col++)
                upstairs.Tables.Add(new Table { Number = number++, SeatsPerSide = 8, X = 4 + col * 5.4, Y = 3 + row * 2.5 });
        upstairs.Tables[0].DisabledSeats = [0, 8];
        upstairs.Tables[5].DisabledSeats = [15];

        var project = new EventProject();
        project.Setup.Floors.AddRange([ground, upstairs]);
        var groups = project.Setup.Groups;
        var target = People(project, fill);
        var people = 0;
        foreach (var name in new[] { "Sales", "Marketing", "Finance", "IT", "Operations", "HR" })
        {
            var department = new Group { Name = name, PeopleCount = 0 };
            groups.Add(department);
            var teams = 2 + random.Next(3);
            for (var t = 1; t <= teams; t++)
            {
                var team = new Group { Name = $"{name} team {t}", ParentId = department.Id, PeopleCount = random.Next(6, 21) };
                groups.Add(team);
                if (random.NextDouble() < 0.3)
                {
                    var half = team.PeopleCount / 2;
                    groups.Add(new Group { Name = $"{team.Name} A", ParentId = team.Id, PeopleCount = half });
                    groups.Add(new Group { Name = $"{team.Name} B", ParentId = team.Id, PeopleCount = team.PeopleCount - half });
                    people += team.PeopleCount;
                    team.PeopleCount = 0;
                }
                else
                    people += team.PeopleCount;
            }
        }
        foreach (var name in new[] { "Board", "Partners", "Guests", "Press", "Alumni", "Suppliers", "Interns", "Speakers" })
        {
            if (people >= target)
                break;
            var size = Math.Min(people >= target - 20 ? target - people : random.Next(4, 13), target - people);
            groups.Add(new Group { Name = name, PeopleCount = size });
            people += size;
        }
        if (people < target)
            groups.Add(new Group { Name = "Late additions", PeopleCount = target - people });

        // Preferences between groups that were made (at a low fill some of the last ones may not be).
        void Prefer(string from, params string[] to)
        {
            if (groups.FirstOrDefault(g => g.Name == from) is { } group)
                group.PreferredGroupIds = to.Select(n => groups.FirstOrDefault(g => g.Name == n)).OfType<Group>().Select(g => g.Id).ToList();
        }
        Prefer("Sales", "Marketing");
        Prefer("IT", "Operations", "Finance");
        Prefer("Board", "Partners");
        Prefer("Partners", "Sales");
        Prefer("HR team 1", "Finance team 1");
        Prefer("Marketing team 2", "Sales team 1");
        return project;
    }

    // ---- Helpers ----

    private static Table Round(int number, int seats, double x, double y) =>
        new() { Number = number, Shape = TableShape.Round, RoundSeats = seats, X = x, Y = y };

    /// <summary>Groups with sizes from <paramref name="size"/> until <paramref name="people"/> are reached exactly, with random preferences.</summary>
    private static List<Group> Groups(Random random, int people, Func<int> size, string[] names, double preferenceChance, int maxPreferences)
    {
        var groups = new List<Group>();
        var total = 0;
        while (total < people)
        {
            var count = Math.Min(size(), people - total);
            var name = groups.Count < names.Length ? names[groups.Count] : $"{names[groups.Count % names.Length]} {groups.Count / names.Length + 1}";
            groups.Add(new Group { Name = name, PeopleCount = count });
            total += count;
        }
        groups = groups.OrderBy(_ => random.Next()).ToList();
        foreach (var group in groups)
            if (random.NextDouble() < preferenceChance)
                group.PreferredGroupIds = groups.Where(g => g != group).OrderBy(_ => random.Next())
                    .Take(random.Next(1, maxPreferences + 1)).Select(g => g.Id).ToList();
        return groups;
    }

    private static readonly string[] FamilyNames =
    [
        "Bride's family", "Groom's family", "Hansen family", "Jensen family", "Nielsen family", "Pedersen family",
        "Andersen family", "Christensen family", "Larsen family", "Sørensen family", "Rasmussen family", "Jørgensen family",
        "Petersen family", "Madsen family", "Kristensen family", "Olsen family", "Thomsen family", "Christiansen family",
        "Poulsen family", "Johansen family", "Møller family", "Mortensen family", "School friends", "University friends",
        "Football team", "Choir", "Neighbours", "Colleagues of the bride", "Colleagues of the groom", "Book club",
        "Scouts", "Riding club", "Old classmates", "Cousins", "Godparents", "Running club", "Band", "Travel friends",
        "Chess club", "Gym friends", "Dance class", "Knitting club", "Handball team", "Village friends", "Sailing club",
        "Wine club", "Board game night", "Climbing friends", "Exchange students", "Childhood friends",
    ];

    private static readonly string[] DepartmentNames =
    [
        "Sales", "Marketing", "Finance", "IT", "HR", "Legal", "Production", "Logistics", "Customer service", "R&D",
        "Management", "Facilities", "Purchasing", "Quality", "Design", "Support", "Data", "Security", "Training",
        "Communications", "Partners", "Board", "Interns", "Alumni", "Guests", "Press", "Suppliers", "Retirees",
    ];

    private static readonly string[] TeamNames =
    [
        "Seniors", "Juniors", "Veterans", "Ladies", "Old boys", "Youth A", "Youth B", "Committee", "Volunteers", "Coaches",
        "Parents", "Sponsors", "Band", "Kitchen crew", "Bar crew", "Referees", "Fan club", "Founders", "Newcomers",
        "Tennis", "Padel", "Badminton", "Swimming", "Rowing", "Cycling", "Running", "Yoga", "Pilates", "Boxing", "Judo",
        "Darts", "Bowling", "Golf", "Hockey", "Rugby", "Cricket", "Archery", "Fencing", "Skiing", "Climbing",
    ];

    private static readonly string[] CompanyNames =
    [
        "Acme", "Globex", "Initech", "Umbrella", "Hooli", "Vandelay", "Wonka", "Stark", "Wayne", "Tyrell", "Cyberdyne",
        "Soylent", "Massive Dynamic", "Gringotts", "Oscorp", "Aperture", "Black Mesa", "Monarch", "Pied Piper", "Dunder",
        "Nakatomi", "Weyland", "Virtucon", "Prestige", "Sterling", "Gekko", "Duff", "Krusty", "Bluth", "Los Pollos",
    ];
}
