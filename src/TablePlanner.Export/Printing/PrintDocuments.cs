using System.Globalization;
using TablePlanner.Core;
using TablePlanner.Core.Model;

namespace TablePlanner.Export.Printing;

/// <summary>A placeholder a template can use, for the help shown next to the template editor.</summary>
/// <param name="Items">For a list: the placeholders available inside <c>{{#Name}}…{{/Name}}</c>.</param>
public sealed record Placeholder(string Name, string Description, IReadOnlyList<Placeholder>? Items = null);

/// <summary>What the table cards are printed for.</summary>
public enum CardsFor
{
    /// <summary>One card for each group at each table.</summary>
    GroupAtTable,

    /// <summary>One card for each seated person, to put on their seat.</summary>
    Seat,
}

/// <param name="Folded">
/// Tent cards: one card per A4 sheet, printed twice with the top half upside down, so the sheet folded across the
/// middle stands on the table and reads from both sides. Otherwise flat cards, two to a sheet, to cut apart.
/// Only on A4: an A5 tent would make cards of A6.
/// </param>
/// <param name="Paper">A4 (two A5 cards per sheet) or A5 (one card per sheet, nothing to cut).</param>
public sealed record CardOptions(CardsFor For = CardsFor.GroupAtTable, bool Folded = false, PaperSize Paper = PaperSize.A4)
{
    /// <summary>Whether tent cards are printed: they need A4 paper.</summary>
    public bool IsFolded => Folded && Paper == PaperSize.A4;
}

/// <summary>Table cards: A5 cards, two to an A4 sheet (cut apart or folded into a tent card) or one per A5 sheet.</summary>
public static class TableCards
{
    /// <summary>An A4 sheet holds two A5 halves.</summary>
    public const int HalvesPerSheet = 2;

    public static readonly IReadOnlyList<Placeholder> Placeholders =
    [
        new("EventName", "The project's name."),
        new("CardCount", "How many cards there are."),
        new("SheetCount", "How many sheets that takes."),
        new("Paper", "A4 or A5."),
        new("PageSize", "For @page { size: … }: \"A4 portrait\" or \"A5 landscape\"."),
        new("SheetWidthMm", "The sheet's width in millimetres."),
        new("SheetHeightMm", "The sheet's height in millimetres."),
        new("CardHeightMm", "The height of one card (half an A4 sheet, or the whole A5 sheet)."),
        new("PerSeat", "True when there is a card for each seat (otherwise one per group at each table)."),
        new("Folded", "True for folded tent cards."),
        new("Sheets", "The sheets: on A4 two halves (two cards, or for tent cards one card twice), on A5 one card.",
        [
            new("Cards", "The halves of this sheet, top first: everything listed under Cards, and Flipped.",
                [new("Flipped", "True for the top half of a tent card, which is printed upside down."), .. CardItems]),
        ]),
        new("Cards", "All cards, by floor, table number and position along the table (for a card per seat: seat order).", CardItems),
        .. QrCode.Placeholders,
    ];

    private static IReadOnlyList<Placeholder> CardItems =>
    [
        new("GroupName", "The group's name."),
        new("PeopleCount", "How many people are in the group."),
        new("PeopleHere", "How many of them sit at this table."),
        new("OnePerson", "True when the group is one person (for \"person\" or \"people\")."),
        new("IsSplit", "True when the group sits at more than one table."),
        new("OtherTables", "The group's other table numbers, e.g. \"105, 201\"."),
        new("SeveralOtherTables", "True when there is more than one other table (for \"table\" or \"tables\")."),
        new("TableNumber", "The table's number."),
        new("SeatNumber", "For a card per seat: the seat's number, counted around the table from the top left; otherwise empty."),
        new("SeatNumbers", "The seats the card is for, e.g. \"3, 4, 5\" (one number for a card per seat)."),
        new("FloorName", "The floor the table is on."),
        new("Colour", "The group's colour, e.g. #4e79a7."),
        new("ContactPerson", "The group's contact person."),
        new("DietaryInfo", "The group's dietary info."),
        new("Notes", "The group's notes."),
    ];

    /// <param name="guestPageUrl">The guest page's web address, for a QR code; null or invalid gives none.</param>
    public static TemplateData BuildData(string eventName, PrintedPlan plan, CardOptions? options = null, string? guestPageUrl = null)
    {
        options ??= new CardOptions();
        var colours = GroupColours.For(plan.Setup);
        var perSeat = options.For == CardsFor.Seat;
        // A card per group part, or per seat: then each table's cards go around the table in seat order, so they
        // can be laid out one after the other whichever group sits where.
        var tableOrder = plan.Parts.Select(p => p.Table).Distinct().Select((table, i) => (table, i)).ToDictionary(x => x.table, x => x.i);
        var items = perSeat
            ? plan.Parts.SelectMany(part => part.SeatNumbers.Select(n => (Part: part, Seat: (int?)n)))
                .OrderBy(x => tableOrder[x.Part.Table]).ThenBy(x => x.Seat)
            : plan.Parts.Select(part => (Part: part, Seat: (int?)null));
        var cards = items.Select(item =>
        {
            var (part, seat) = item;
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
                ["SeatNumber"] = seat?.ToString(CultureInfo.InvariantCulture) ?? "",
                ["SeatNumbers"] = seat?.ToString(CultureInfo.InvariantCulture) ?? string.Join(", ", part.SeatNumbers),
                ["FloorName"] = part.Floor.Name,
                ["Colour"] = colours.GetValueOrDefault(part.Group.Id, "#cccccc"),
                ["ContactPerson"] = part.Group.ContactPerson ?? "",
                ["DietaryInfo"] = part.Group.DietaryInfo ?? "",
                ["Notes"] = part.Group.Notes ?? "",
            };
        }).ToList();

        // A tent card is the same card on both halves, the top one upside down. An A5 sheet holds one card.
        var a5 = options.Paper == PaperSize.A5;
        var sheets = options.IsFolded
            ? cards.Select(card => new TemplateData { ["Cards"] = new List<TemplateData> { WithFlipped(card, true), WithFlipped(card, false) } }).ToList()
            : cards.Chunk(a5 ? 1 : HalvesPerSheet).Select(sheet => new TemplateData { ["Cards"] = sheet.Select(c => WithFlipped(c, false)).ToList() }).ToList();

        var data = new TemplateData
        {
            ["EventName"] = eventName,
            ["CardCount"] = cards.Count,
            ["SheetCount"] = sheets.Count,
            ["Paper"] = a5 ? "A5" : "A4",
            ["PageSize"] = a5 ? "A5 landscape" : "A4 portrait",
            ["SheetWidthMm"] = 210,
            ["SheetHeightMm"] = a5 ? 148 : 297,
            ["CardHeightMm"] = a5 ? 148 : 148.5,
            ["PerSeat"] = perSeat,
            ["Folded"] = options.IsFolded,
            ["Sheets"] = sheets,
            ["Cards"] = cards,
        };
        QrCode.AddTo(data, guestPageUrl);
        return data;
    }

    private static TemplateData WithFlipped(TemplateData card, bool flipped)
    {
        var copy = new TemplateData();
        foreach (var (key, value) in card)
            copy[key] = value;
        copy["Flipped"] = flipped;
        return copy;
    }
}

public enum PaperSize
{
    A3,
    A4,
    A5,
    A2,
    A1,
}

public static class PaperSizes
{
    /// <summary>Short and long side in millimetres.</summary>
    public static (int Short, int Long) Millimetres(PaperSize paper) => paper switch
    {
        PaperSize.A1 => (594, 841),
        PaperSize.A2 => (420, 594),
        PaperSize.A3 => (297, 420),
        PaperSize.A5 => (148, 210),
        _ => (210, 297),
    };
}

/// <summary>What the poster shows and the paper it is printed on.</summary>
public sealed record PosterOptions(PaperSize Paper = PaperSize.A3, bool Landscape = true, bool ShowFloorPlans = true, bool ShowGroupList = true);

/// <summary>The poster: a floor plan page per floor and an alphabetical "find your table" list.</summary>
public static class Poster
{
    public static readonly IReadOnlyList<Placeholder> Placeholders =
    [
        new("EventName", "The project's name."),
        new("PaperSize", "A1, A2, A3 or A4."),
        new("Orientation", "landscape or portrait."),
        new("PageSize", "For @page { size: … }: the page size in millimetres, e.g. \"594mm 420mm\" (A2 landscape). Browsers don't all know A1 and A2 by name."),
        new("Scale", "How much bigger text and marks should be than on A3/A4: 1 on A3 and A4, 1.41 on A2, 2 on A1. Use as calc(12pt * {{Scale}})."),
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
        .. QrCode.Placeholders,
    ];

    internal static IReadOnlyList<Placeholder> GroupItems =>
    [
        new("GroupName", "The group's name."),
        new("GroupKey", "A short ID of the group, as in the floor plans' data-group attributes."),
        new("PeopleCount", "How many people are in the group."),
        new("Tables", "The group's table numbers, e.g. \"104, 105\"."),
        new("FloorName", "The floor (or floors) the group sits on."),
        new("Colour", "The group's colour."),
    ];

    /// <param name="images">The project's images by ID (data URLs), for floor backgrounds shown on the poster.</param>
    /// <param name="guestPageUrl">The guest page's web address, for a QR code; null or invalid gives none.</param>
    public static TemplateData BuildData(string eventName, PrintedPlan plan, PosterOptions options, IReadOnlyDictionary<Guid, string>? images = null,
        string? guestPageUrl = null)
    {
        var colours = GroupColours.For(plan.Setup);
        var (shortSide, longSide) = PaperSizes.Millimetres(options.Paper);
        var (width, height) = options.Landscape ? (longSide, shortSide) : (shortSide, longSide);
        var groups = GroupList(plan, colours);

        var data = new TemplateData
        {
            ["EventName"] = eventName,
            ["PaperSize"] = options.Paper.ToString(),
            ["Orientation"] = options.Landscape ? "landscape" : "portrait",
            ["PageSize"] = $"{width}mm {height}mm",
            ["Scale"] = options.Paper switch { PaperSize.A1 => "2", PaperSize.A2 => "1.41", _ => "1" },
            ["PageWidthMm"] = width,
            ["PageHeightMm"] = height,
            ["ShowFloorPlans"] = options.ShowFloorPlans,
            ["ShowGroupList"] = options.ShowGroupList,
            ["ShowFloorNames"] = plan.Setup.Floors.Count(f => f.Tables.Count > 0) > 1,
            ["GroupCount"] = groups.Count,
            ["PeopleSeated"] = plan.Parts.Sum(p => p.People),
            // Drawing is the slow part; skip it when the pages are not wanted.
            ["Floors"] = FloorList(plan, colours, images, options.ShowFloorPlans),
            ["Groups"] = groups.Select(g => g.Data).ToList(),
            ["Letters"] = groups.GroupBy(g => g.Letter)
                .Select(l => new TemplateData { ["Letter"] = l.Key, ["Groups"] = l.Select(g => g.Data).ToList() })
                .ToList(),
        };
        QrCode.AddTo(data, guestPageUrl);
        return data;
    }

    /// <summary>The seated groups by name, each with its first letter.</summary>
    internal static List<(string Letter, TemplateData Data)> GroupList(PrintedPlan plan, IReadOnlyDictionary<Guid, string> colours)
    {
        var comparer = StringComparer.Create(CultureInfo.InvariantCulture, ignoreCase: true);
        return plan.Parts.GroupBy(p => p.Group)
            .OrderBy(g => g.Key.Name, comparer)
            .Select(g => (LetterOf(g.Key.Name), new TemplateData
            {
                ["GroupName"] = g.Key.Name,
                ["GroupKey"] = FloorPlanSvg.GroupKey(g.Key.Id),
                ["PeopleCount"] = g.Key.PeopleCount,
                ["Tables"] = string.Join(", ", g.Select(p => p.Table.Number).Distinct()),
                ["FloorName"] = string.Join(", ", g.Select(p => p.Floor.Name).Distinct()),
                ["Colour"] = colours.GetValueOrDefault(g.Key.Id, "#cccccc"),
            }))
            .ToList();
    }

    /// <summary>The floors with tables, with their floor plans (empty when <paramref name="draw"/> is false).</summary>
    internal static List<TemplateData> FloorList(PrintedPlan plan, IReadOnlyDictionary<Guid, string> colours, IReadOnlyDictionary<Guid, string>? images, bool draw) =>
        plan.Setup.Floors.Where(f => f.Tables.Count > 0).Select(f => new TemplateData
        {
            ["FloorName"] = f.Name,
            ["FloorPlan"] = new RawHtml(draw
                ? FloorPlanSvg.Render(f, plan, colours, f.Background is { } b ? images?.GetValueOrDefault(b.ImageId) : null)
                : ""),
        }).ToList();

    private static string LetterOf(string name)
    {
        var first = name.TrimStart().FirstOrDefault();
        return char.IsLetter(first) ? char.ToUpperInvariant(first).ToString() : "#";
    }
}

/// <summary>
/// The guest page: one self-contained HTML file to put on any web host, where guests search for their group on their
/// phone and see their table on the floor plan. Its template has a small script for the search.
/// </summary>
public static class GuestPage
{
    public static readonly IReadOnlyList<Placeholder> Placeholders =
    [
        new("EventName", "The project's name."),
        new("ShowFloorNames", "True when there is more than one floor with tables."),
        new("GroupCount", "How many groups are seated."),
        new("Floors", "The floors with tables.",
        [
            new("FloorName", "The floor's name."),
            new("FloorPlan", "The floor plan as an SVG picture; tables have data-table and seats data-group attributes."),
        ]),
        new("Groups", "The seated groups, by name.", Poster.GroupItems),
    ];

    /// <param name="images">The project's images by ID, for floor backgrounds set to show on the poster.</param>
    public static TemplateData BuildData(string eventName, PrintedPlan plan, IReadOnlyDictionary<Guid, string>? images = null)
    {
        var colours = GroupColours.For(plan.Setup);
        var groups = Poster.GroupList(plan, colours);
        return new TemplateData
        {
            ["EventName"] = eventName,
            ["ShowFloorNames"] = plan.Setup.Floors.Count(f => f.Tables.Count > 0) > 1,
            ["GroupCount"] = groups.Count,
            ["Floors"] = Poster.FloorList(plan, colours, images, draw: true),
            ["Groups"] = groups.Select(g => g.Data).ToList(),
        };
    }
}
