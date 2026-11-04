using TablePlanner.Core.Model;

namespace TablePlanner.Core;

/// <summary>A ready-made project for trying the app, and as test and benchmark data.</summary>
public static class ExampleProject
{
    public static EventProject Create()
    {
        var ground = new Floor { Name = "Ground floor", Number = 1 };
        // Three rows of three tables, end to end. Rows are 2.5 m apart, so neighbouring rows sit back to back.
        foreach (var y in new[] { 4.0, 6.5, 9.0 })
            foreach (var x in new[] { 4.0, 10.5, 17.0 })
                AddTable(ground, x, y, seatsPerSide: 10);
        ground.Elements.AddRange(
        [
            Element(FloorElementKind.Stage, "Stage", 10.5, 0.75, 8, 1.5),
            Element(FloorElementKind.Bar, "Bar", 22, 6.5, 1, 4),
            Element(FloorElementKind.Buffet, "Buffet", 10.5, 12, 6, 1),
            Element(FloorElementKind.Entrance, "Entrance", 0.5, 12, 1, 2),
            Element(FloorElementKind.Stairs, "Stairs to 1st floor", 22, 12, 2, 2),
        ]);

        var first = new Floor { Name = "1st floor", Number = 2 };
        foreach (var y in new[] { 3.0, 5.5 })
            foreach (var x in new[] { 4.0, 10.0 })
                AddTable(first, x, y, seatsPerSide: 8, endSeats: 2);
        first.Elements.AddRange(
        [
            Element(FloorElementKind.Stairs, "Stairs to ground floor", 14, 8, 2, 2),
            Element(FloorElementKind.Toilets, "Toilets", 1, 8, 2, 1.5),
        ]);

        var groups = new[]
        {
            ("Sales", 50), ("Engineering", 40), ("Support", 32), ("Marketing", 24), ("Finance", 18),
            ("Board", 12), ("Smith family", 10), ("Jensen family", 8), ("Hansen family", 8), ("Old classmates", 6),
            ("Neighbours", 6), ("Book club", 5), ("Nielsen family", 4), ("Andersen family", 4), ("Larsen family", 4),
        }.Select(g => new Group { Name = g.Item1, PeopleCount = g.Item2 }).ToList();

        Prefer(groups, "Sales", "Marketing", "Support");
        Prefer(groups, "Marketing", "Sales");
        Prefer(groups, "Engineering", "Support", "Board");
        Prefer(groups, "Finance", "Board");
        Prefer(groups, "Smith family", "Jensen family", "Neighbours");
        Prefer(groups, "Jensen family", "Smith family", "Hansen family");
        Prefer(groups, "Nielsen family", "Andersen family", "Larsen family");
        Prefer(groups, "Andersen family", "Nielsen family");

        return new EventProject
        {
            Name = "Example party",
            Setup = new EventSetup { Floors = [ground, first], Groups = groups },
        };
    }

    private static void AddTable(Floor floor, double x, double y, int seatsPerSide, int endSeats = 0) =>
        floor.Tables.Add(new Table
        {
            Number = floor.NextTableNumber(),
            X = x,
            Y = y,
            SeatsPerSide = seatsPerSide,
            EndSeats = endSeats,
        });

    private static FloorElement Element(FloorElementKind kind, string label, double x, double y, double width, double depth, Rotation rotation = Rotation.R0) =>
        new() { Kind = kind, Label = label, X = x, Y = y, Width = width, Depth = depth, Rotation = rotation };

    private static void Prefer(List<Group> groups, string name, params string[] preferred)
    {
        var group = groups.Single(g => g.Name == name);
        group.PreferredGroupIds = preferred.Select(p => groups.Single(g => g.Name == p).Id).ToList();
    }
}
