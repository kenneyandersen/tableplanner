using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;

namespace TablePlanner.Core.Tests;

public class ProjectCopiesTests
{
    [Fact]
    public void A_copy_has_a_new_id_and_name_and_everything_else()
    {
        var source = ExampleProject.Create();
        source.LastExportedAt = DateTimeOffset.Now;
        source.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(source.Setup.AllTables.First().Id, 0), source.Setup.Groups[0].Id));

        var copy = ProjectCopies.Copy(source, "Copy");

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Copy", copy.Name);
        Assert.Null(copy.LastExportedAt);
        Assert.Equal(source.Setup.Groups.Select(g => (g.Id, g.Name)), copy.Setup.Groups.Select(g => (g.Id, g.Name)));
        Assert.Equal(source.CurrentAssignment.Seats, copy.CurrentAssignment.Seats);
        Assert.NotSame(source.Setup.Groups[0], copy.Setup.Groups[0]);
    }

    [Fact]
    public void Copy_names_count_up_past_names_in_use()
    {
        Assert.Equal("Party (copy)", ProjectCopies.CopyName("Party", ["Party"]));
        Assert.Equal("Party (copy 3)", ProjectCopies.CopyName("Party", ["Party", "party (COPY)", "Party (copy 2)"]));
    }
}

public class DuplicatePersonTests
{
    [Fact]
    public void The_same_name_twice_is_a_warning_naming_the_groups()
    {
        var project = new EventProject();
        project.Setup.Groups.AddRange([new Group { Name = "Sales", PeopleCount = 3 }, new Group { Name = "IT", PeopleCount = 3 }]);
        SetupOperations.AddPeople(project, project.Setup.Groups[0].Id, ["Ann Holm", "Bo"]);
        SetupOperations.AddPeople(project, project.Setup.Groups[1].Id, ["ann holm "]);

        var issue = Assert.Single(SetupValidator.Validate(project.Setup).Issues, i => i.Code == IssueCode.PersonNameDuplicate);

        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("2 times", issue.Message);
        Assert.Contains("'Sales', 'IT'", issue.Message);
    }
}
