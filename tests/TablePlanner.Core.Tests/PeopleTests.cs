using TablePlanner.Core.Editing;
using TablePlanner.Core.Model;
using TablePlanner.Core.Validation;

namespace TablePlanner.Core.Tests;

public class PeopleTests
{
    private static (EventProject Project, Group Team) WithTeam(int people = 8)
    {
        var project = new EventProject();
        var team = new Group { Name = "Team", PeopleCount = people };
        project.Setup.Groups.Add(team);
        return (project, team);
    }

    [Fact]
    public void Names_can_be_fewer_than_the_people_and_more_names_raise_the_number()
    {
        var (project, team) = WithTeam(8);

        SetupOperations.AddPeople(project, team.Id, ["Ann", " Bo ", "", "Cy"]);

        Assert.Equal(["Ann", "Bo", "Cy"], team.Members.Select(m => m.Name));
        Assert.Equal(8, team.PeopleCount); // 3 named, 5 unnamed

        SetupOperations.AddPeople(project, team.Id, Enumerable.Range(1, 7).Select(i => $"P{i}"));

        Assert.Equal(10, team.Members.Count);
        Assert.Equal(10, team.PeopleCount);
        Assert.DoesNotContain(SetupValidator.Validate(project.Setup).Issues, i => i.Code == IssueCode.GroupNamesExceedPeople);
    }

    [Fact]
    public void Removing_a_person_makes_the_group_one_smaller()
    {
        var (project, team) = WithTeam(8);
        SetupOperations.AddPeople(project, team.Id, ["Ann", "Bo"]);

        SetupOperations.RemovePerson(project, team.Members[0].Id);

        Assert.Equal(["Bo"], team.Members.Select(m => m.Name));
        Assert.Equal(7, team.PeopleCount);
    }

    [Fact]
    public void Moving_a_person_takes_one_from_the_old_group_and_adds_one_to_the_new()
    {
        var (project, team) = WithTeam(4);
        var other = new Group { Name = "Other", PeopleCount = 2 };
        project.Setup.Groups.Add(other);
        SetupOperations.AddPeople(project, team.Id, ["Ann"]);

        SetupOperations.MovePersonToGroup(project, team.Members[0].Id, other.Id);

        Assert.Empty(team.Members);
        Assert.Equal(3, team.PeopleCount);
        Assert.Equal(["Ann"], other.Members.Select(m => m.Name));
        Assert.Equal(3, other.PeopleCount);
    }

    [Fact]
    public void People_added_to_a_parent_group_go_to_its_other_group()
    {
        var (project, team) = WithTeam(4);
        var sales = SetupOperations.GroupInto(project, [team.Id], "Sales");

        var holder = SetupOperations.AddPeople(project, sales.Id, ["Dana", "Eli"]);
        var again = SetupOperations.AddPeople(project, sales.Id, ["Finn"]);

        Assert.Equal("Sales – other", holder.Name);
        Assert.Same(holder, again);
        Assert.Equal(sales.Id, holder.ParentId);
        Assert.Equal(3, holder.PeopleCount);
        Assert.Empty(sales.Members);
        Assert.Equal(7, project.Setup.PeopleIn(sales));
        Assert.Empty(SetupValidator.Validate(project.Setup).Errors);
    }

    [Fact]
    public void A_bottom_group_that_gets_subgroups_hands_its_names_to_the_other_group()
    {
        var (project, team) = WithTeam(4);
        SetupOperations.AddPeople(project, team.Id, ["Ann", "Bo"]);
        var sub = new Group { Name = "Sub", PeopleCount = 2 };
        project.Setup.Groups.Add(sub);

        SetupOperations.SetParent(project, sub.Id, team.Id);

        var other = project.Setup.ChildrenOf(team.Id).Single(g => g != sub);
        Assert.Equal(["Ann", "Bo"], other.Members.Select(m => m.Name));
        Assert.Equal(4, other.PeopleCount);
        Assert.Empty(team.Members);
    }

    [Fact]
    public void Validation_warns_about_more_names_than_people_and_names_on_a_parent()
    {
        var (project, team) = WithTeam(1);
        team.Members = [new Person { Name = "A" }, new Person { Name = "B" }];
        var parent = new Group { Name = "P", PeopleCount = 0, Members = [new Person { Name = "C" }] };
        project.Setup.Groups.Add(parent);
        project.Setup.Groups.Add(new Group { Name = "Child", PeopleCount = 1, ParentId = parent.Id });

        var issues = SetupValidator.Validate(project.Setup).Issues;

        Assert.Contains(issues, i => i.Code == IssueCode.GroupNamesExceedPeople && i.Severity == IssueSeverity.Warning);
        Assert.Contains(issues, i => i.Code == IssueCode.GroupParentHasPeople);
    }

    [Fact]
    public void Names_survive_saving_and_loading()
    {
        var (project, team) = WithTeam(3);
        SetupOperations.AddPeople(project, team.Id, ["Ann", "Bo"]);

        var loaded = Serialization.ProjectSerializer.Deserialize(Serialization.ProjectSerializer.Serialize(project)).Project!;

        Assert.Equal(team.Members.Select(m => (m.Id, m.Name)), loaded.Setup.Groups.Single().Members.Select(m => (m.Id, m.Name)));
    }
}
