using System.Globalization;
using TablePlanner.Core;
using TablePlanner.Core.Model;

namespace TablePlanner.Export.Printing;

/// <summary>A placeholder a template can use, for the help shown next to the template editor.</summary>
/// <param name="Items">For a list: the placeholders available inside <c>{{#Name}}…{{/Name}}</c>.</param>
public sealed record Placeholder(string Name, string Description, IReadOnlyList<Placeholder>? Items = null);

/// <summary>Table cards: one A5 card per group per table, two cards to an A4 sheet.</summary>
public static class TableCards
{
    public const int CardsPerSheet = 2;

    public static readonly IReadOnlyList<Placeholder> Placeholders =
    [
        new("EventName", "The project's name."),
        new("CardCount", "How many cards there are."),
        new("Sheets", "The A4 sheets, each with up to two cards.", [new("Cards", "The cards on this sheet (see Cards).")]),
        new("Cards", "All cards, by floor, table number and position along the table.",
        [
            new("GroupName", "The group's name."),
            new("PeopleCount", "How many people are in the group."),
            new("PeopleHere", "How many of them sit at this table."),
            new("OnePerson", "True when the group is one person (for \"person\" or \"people\")."),
            new("IsSplit", "True when the group sits at more than one table."),
            new("OtherTables", "The group's other table numbers, e.g. \"105, 201\"."),
            new("SeveralOtherTables", "True when there is more than one other table (for \"table\" or \"tables\")."),
            new("TableNumber", "The table's number."),
            new("FloorName", "The floor the table is on."),
            new("Colour", "The group's colour, e.g. #4e79a7."),
            new("ContactPerson", "The group's contact person."),
            new("DietaryInfo", "The group's dietary info."),
            new("Notes", "The group's notes."),
        ]),
    ];

    public static TemplateData BuildData(string eventName, PrintedPlan plan)
    {
        var colours = GroupColours.For(plan.Setup);
        var cards = plan.Parts.Select(part =>
        {
            var others = plan.Parts.Where(p => p.Group == part.Group && p.Table != part.Table).Select(p => p.Table.Number).Distinct().ToList();
            return new TemplateData
            {
                ["GroupName"] = part.Group.Name,
                ["PeopleCount"] = part.Group.PeopleCount,
                ["PeopleHere"] = part.People,
                ["OnePerson"] = part.Group.PeopleCount == 1,
                ["IsSplit"] = others.Count > 0,
                ["OtherTables"] = string.Join(", ", others),
                ["SeveralOtherTables"] = others.Count > 1,
                ["TableNumber"] = part.Table.Number,
                ["FloorName"] = part.Floor.Name,
                ["Colour"] = colours.GetValueOrDefault(part.Group.Id, "#cccccc"),
                ["ContactPerson"] = part.Group.ContactPerson ?? "",
                ["DietaryInfo"] = part.Group.DietaryInfo ?? "",
                ["Notes"] = part.Group.Notes ?? "",
            };
        }).ToList();

        return new TemplateData
        {
            ["EventName"] = eventName,
            ["CardCount"] = cards.Count,
            ["Sheets"] = cards.Chunk(CardsPerSheet).Select(sheet => new TemplateData { ["Cards"] = sheet.ToList() }).ToList(),
            ["Cards"] = cards,
        };
    }
}

public enum PaperSize
{
    A3,
    A4,
}

/// <summary>What the poster shows and the paper it is printed on.</summary>
public sealed record PosterOptions(PaperSize Paper = PaperSize.A3, bool Landscape = true, bool ShowFloorPlans = true, bool ShowGroupList = true);

/// <summary>The poster: a floor plan page per floor and an alphabetical "find your table" list.</summary>
public static class Poster
{
    public static readonly IReadOnlyList<Placeholder> Placeholders =
    [
        new("EventName", "The project's name."),
        new("PaperSize", "A3 or A4, for @page { size: … }."),
        new("Orientation", "landscape or portrait."),
        new("PageWidthMm", "The page width in millimetres, in the chosen orientation."),
        new("PageHeightMm", "The page height in millimetres, in the chosen orientation."),
        new("ShowFloorPlans", "True when the floor plan pages are wanted."),
        new("ShowGroupList", "True when the group list is wanted."),
        new("ShowFloorNames", "True when there is more than one floor with tables."),
        new("GroupCount", "How many groups are seated."),
        new("PeopleSeated", "How many people are seated."),
        new("Floors", "The floors with tables.",
        [
            new("FloorName", "The floor's name."),
            new("FloorPlan", "The floor plan as an SVG picture, with group names on their tables."),
        ]),
        new("Groups", "The seated groups, by name.", GroupItems),
        new("Letters", "The seated groups, by first letter.",
        [
            new("Letter", "The first letter, or # for names that start with something else."),
            new("Groups", "The groups starting with this letter.", GroupItems),
        ]),
    ];

    private static IReadOnlyList<Placeholder> GroupItems =>
    [
        new("GroupName", "The group's name."),
        new("PeopleCount", "How many people are in the group."),
        new("Tables", "The group's table numbers, e.g. \"104, 105\"."),
        new("FloorName", "The floor (or floors) the group sits on."),
        new("Colour", "The group's colour."),
    ];

    public static TemplateData BuildData(string eventName, PrintedPlan plan, PosterOptions options)
    {
        var colours = GroupColours.For(plan.Setup);
        var (shortSide, longSide) = options.Paper == PaperSize.A3 ? (297, 420) : (210, 297);
        var floors = plan.Setup.Floors.Where(f => f.Tables.Count > 0).ToList();

        var comparer = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);
        var groups = plan.Parts.GroupBy(p => p.Group)
            .OrderBy(g => g.Key.Name, comparer)
            .Select(g => (Letter: LetterOf(g.Key.Name), Data: new TemplateData
            {
                ["GroupName"] = g.Key.Name,
                ["PeopleCount"] = g.Key.PeopleCount,
                ["Tables"] = string.Join(", ", g.Select(p => p.Table.Number).Distinct()),
                ["FloorName"] = string.Join(", ", g.Select(p => p.Floor.Name).Distinct()),
                ["Colour"] = colours.GetValueOrDefault(g.Key.Id, "#cccccc"),
            }))
            .ToList();

        return new TemplateData
        {
            ["EventName"] = eventName,
            ["PaperSize"] = options.Paper.ToString(),
            ["Orientation"] = options.Landscape ? "landscape" : "portrait",
            ["PageWidthMm"] = options.Landscape ? longSide : shortSide,
            ["PageHeightMm"] = options.Landscape ? shortSide : longSide,
            ["ShowFloorPlans"] = options.ShowFloorPlans,
            ["ShowGroupList"] = options.ShowGroupList,
            ["ShowFloorNames"] = floors.Count > 1,
            ["GroupCount"] = groups.Count,
            ["PeopleSeated"] = plan.Parts.Sum(p => p.People),
            ["Floors"] = floors.Select(f => new TemplateData
            {
                ["FloorName"] = f.Name,
                // Drawing is the slow part; skip it when the pages are not wanted.
                ["FloorPlan"] = new RawHtml(options.ShowFloorPlans ? FloorPlanSvg.Render(f, plan, colours) : ""),
            }).ToList(),
            ["Groups"] = groups.Select(g => g.Data).ToList(),
            ["Letters"] = groups.GroupBy(g => g.Letter)
                .Select(l => new TemplateData { ["Letter"] = l.Key, ["Groups"] = l.Select(g => g.Data).ToList() })
                .ToList(),
        };
    }

    private static string LetterOf(string name)
    {
        var first = name.TrimStart().FirstOrDefault();
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
    }
}
