using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;

namespace TablePlanner.Core.Tests;

public class TestProjectsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Test_projects_are_valid_with_about_as_many_people_as_seats(int seed)
    {
        var projects = TestProjects.All(new Random(seed));

        Assert.Equal(7, projects.Count);
        Assert.Equal(projects.Count, projects.Select(p => p.Name).Distinct().Count());
        foreach (var project in projects)
        {
            Assert.Empty(SetupValidator.Validate(project.Setup).Errors);
            var seats = project.Setup.AllTables.Sum(t => t.AvailableSeatCount);
            var people = project.Setup.Groups.Sum(g => g.PeopleCount);
            Assert.InRange(people / (double)seats, 0.8, 1.1);
            Assert.Contains(project.Setup.Groups, g => g.Members.Count > 0);
        }
        Assert.True(projects.Single(p => p.Name.StartsWith("Test – Overbooked")).Setup is var s && s.Groups.Sum(g => g.PeopleCount) > s.AllTables.Sum(t => t.AvailableSeatCount));
        Assert.Contains(projects, p => p.Setup.HasLevels());
    }
}
