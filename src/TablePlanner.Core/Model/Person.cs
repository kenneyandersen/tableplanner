namespace TablePlanner.Core.Model;

/// <summary>
/// A named person in a group. Optional: a group's number of people (<see cref="Group.PeopleCount"/>) is what is seated;
/// names say who some (or all) of them are. People are not given seats of their own.
/// </summary>
public sealed class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
}
