using TablePlanner.Core.Model;
using TablePlanner.Core.Serialization;

namespace TablePlanner.Core.Tests;

public class ProjectSerializerTests
{
    private static EventProject RoundTrip(EventProject project)
    {
        var result = ProjectSerializer.Deserialize(ProjectSerializer.Serialize(project));
        Assert.True(result.Success, result.Error);
        return result.Project!;
    }

    [Fact]
    public void Example_project_survives_a_round_trip()
    {
        var original = ExampleProject.Create();

        var loaded = RoundTrip(original);

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Name, loaded.Name);
        Assert.Equal(original.Setup.Floors.Select(f => (f.Id, f.Name, f.Number)), loaded.Setup.Floors.Select(f => (f.Id, f.Name, f.Number)));
        Assert.Equal(
            original.Setup.AllTables.Select(t => (t.Id, t.Number, t.X, t.Y, t.Rotation, t.SeatsPerSide, t.EndSeats)),
            loaded.Setup.AllTables.Select(t => (t.Id, t.Number, t.X, t.Y, t.Rotation, t.SeatsPerSide, t.EndSeats)));
        Assert.Equal(
            original.Setup.Floors.SelectMany(f => f.Elements).Select(e => (e.Id, e.Kind, e.Label)),
            loaded.Setup.Floors.SelectMany(f => f.Elements).Select(e => (e.Id, e.Kind, e.Label)));
        Assert.Equal(original.Setup.Groups.Select(g => (g.Id, g.Name, g.PeopleCount)), loaded.Setup.Groups.Select(g => (g.Id, g.Name, g.PeopleCount)));
        Assert.Equal(original.Setup.Groups.Select(g => g.PreferredGroupIds), loaded.Setup.Groups.Select(g => g.PreferredGroupIds));
    }

    [Fact]
    public void Optional_fields_pins_assignments_and_priorities_survive_a_round_trip()
    {
        var project = ExampleProject.Create();
        var table = project.Setup.Floors[0].Tables[0];
        var group = project.Setup.Groups[^1];
        table.Rotation = Rotation.R270;
        table.DisabledSeats = [2, 5];
        group.Notes = "Wheelchair user";
        group.Colour = "#FF8800";
        group.DietaryInfo = "2 vegetarian";
        group.ContactPerson = "Kim, 12345678";
        project.Setup.Pins.Add(new Pin { GroupId = group.Id, Scope = PinScope.Seats, Seats = [new(table.Id, 0), new(table.Id, 1)] });
        project.CurrentAssignment.Seats.Add(new(new SeatId(table.Id, 0), group.Id));
        project.Priorities.NeverSplitAcrossFloors = true;
        project.Priorities.PreferenceRankWeights = [90, 20];
        project.LastExportedAt = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.FromHours(1));
        project.SavedAssignments.Add(new SavedAssignment
        {
            Name = "Draft 1",
            SavedAt = new DateTimeOffset(2026, 11, 4, 13, 0, 0, TimeSpan.Zero),
            Assignment = new SeatingAssignment { Seats = [new(new SeatId(table.Id, 1), group.Id)] },
        });

        var loaded = RoundTrip(project);

        var loadedTable = loaded.Setup.Floors[0].Tables[0];
        var loadedGroup = loaded.Setup.Groups[^1];
        Assert.Equal(Rotation.R270, loadedTable.Rotation);
        Assert.Equal([2, 5], loadedTable.DisabledSeats.Order());
        Assert.Equal(("Wheelchair user", "#FF8800", "2 vegetarian", "Kim, 12345678"),
            (loadedGroup.Notes, loadedGroup.Colour, loadedGroup.DietaryInfo, loadedGroup.ContactPerson));
        var pin = Assert.Single(loaded.Setup.Pins);
        Assert.Equal((PinScope.Seats, group.Id), (pin.Scope, pin.GroupId));
        Assert.Equal([new SeatId(table.Id, 0), new SeatId(table.Id, 1)], pin.Seats);
        Assert.Equal(group.Id, loaded.CurrentAssignment.GroupAt(new SeatId(table.Id, 0)));
        Assert.True(loaded.Priorities.NeverSplitAcrossFloors);
        Assert.Equal([90, 20], loaded.Priorities.PreferenceRankWeights);
        Assert.Equal(project.LastExportedAt, loaded.LastExportedAt);
        var saved = Assert.Single(loaded.SavedAssignments);
        Assert.Equal(("Draft 1", project.SavedAssignments[0].SavedAt), (saved.Name, saved.SavedAt));
        Assert.Equal(group.Id, saved.Assignment.GroupAt(new SeatId(table.Id, 1)));
    }

    [Fact]
    public void Json_uses_camel_case_names_and_enum_names()
    {
        var project = ExampleProject.Create();
        project.Setup.Floors[0].Tables[0].Rotation = Rotation.R90;

        var json = ProjectSerializer.Serialize(project);

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"rotation\": \"R90\"", json);
        Assert.Contains("\"kind\": \"Stairs\"", json);
    }

    [Fact]
    public void Missing_optional_sections_get_defaults()
    {
        var result = ProjectSerializer.Deserialize("""{ "schemaVersion": 1, "name": "Minimal" }""");

        Assert.True(result.Success, result.Error);
        Assert.Equal("Minimal", result.Project!.Name);
        Assert.Empty(result.Project.Setup.Groups);
        Assert.Empty(result.Project.SavedAssignments);
        Assert.Equal(new SeatingPriorities().FloorSplitPenalty, result.Project.Priorities.FloorSplitPenalty);
    }

    [Theory]
    [InlineData("""{ "name": "No version" }""")]
    [InlineData("""[1, 2, 3]""")]
    [InlineData("""{ "schemaVersion": "1" }""")]
    public void File_without_a_schema_version_is_rejected(string json)
    {
        var result = ProjectSerializer.Deserialize(json);

        Assert.False(result.Success);
        Assert.Contains("not a Table Planner project", result.Error);
    }

    [Fact]
    public void File_from_a_newer_version_is_rejected()
    {
        var result = ProjectSerializer.Deserialize($$"""{ "schemaVersion": {{EventProject.CurrentSchemaVersion + 1}} }""");

        Assert.False(result.Success);
        Assert.Contains("newer version", result.Error);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("""{ "schemaVersion": 1, "setup": { "groups": null } }""")]
    [InlineData("""{ "schemaVersion": 1, "setup": { "groups": [ { "peopleCount": "many" } ] } }""")]
    public void Invalid_content_returns_an_error_instead_of_throwing(string json)
    {
        var result = ProjectSerializer.Deserialize(json);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
