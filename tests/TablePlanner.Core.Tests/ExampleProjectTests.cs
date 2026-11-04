namespace TablePlanner.Core.Tests;

public class ExampleProjectTests
{
    [Fact]
    public void Has_several_floors_with_tables_and_elements()
    {
        var project = ExampleProject.Create();

        Assert.True(project.Setup.Floors.Count >= 2);
        Assert.All(project.Setup.Floors, f => Assert.NotEmpty(f.Tables));
        Assert.All(project.Setup.Floors, f => Assert.NotEmpty(f.Elements));
    }

    [Fact]
    public void Tables_are_numbered_per_floor()
    {
        var project = ExampleProject.Create();

        foreach (var floor in project.Setup.Floors)
            Assert.Equal(
                Enumerable.Range(floor.Number * 100 + 1, floor.Tables.Count),
                floor.Tables.Select(t => t.Number));
    }

    [Fact]
    public void Has_enough_seats_for_everyone_and_groups_of_varied_sizes()
    {
        var setup = ExampleProject.Create().Setup;

        Assert.True(setup.AllTables.Sum(t => t.AvailableSeatCount) >= setup.Groups.Sum(g => g.PeopleCount));
        Assert.Contains(setup.Groups, g => g.PeopleCount <= 4);
        Assert.Contains(setup.Groups, g => g.PeopleCount >= 50);
        Assert.Contains(setup.Groups, g => g.PreferredGroupIds.Count > 0);
    }

    [Fact]
    public void Each_call_creates_an_independent_project()
    {
        Assert.NotEqual(ExampleProject.Create().Id, ExampleProject.Create().Id);
    }
}
