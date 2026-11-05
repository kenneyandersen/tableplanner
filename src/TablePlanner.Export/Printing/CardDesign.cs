using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TablePlanner.Export.Printing;

public enum CardItemKind
{
    /// <summary>Text, which may contain placeholders such as {{GroupName}} and sections such as {{#IsSplit}}…{{/IsSplit}}.</summary>
    Text,

    /// <summary>A filled rectangle, e.g. a band in the group's colour.</summary>
    Box,

    /// <summary>The QR code for the guest page; shown only when its address is set.</summary>
    QrCode,

    /// <summary>A picture such as a logo, kept as a data URL.</summary>
    Image,
}

public enum TextAlign
{
    Left,
    Center,
    Right,
}

public enum VerticalAlign
{
    Top,
    Middle,
    Bottom,
}

/// <summary>One thing on a card. Positions and sizes are millimetres from the card's top-left corner.</summary>
public sealed class CardItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CardItemKind Kind { get; set; } = CardItemKind.Text;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 60;
    public double Height { get; set; } = 15;

    /// <summary>For text: what is shown, with placeholders. Line breaks are kept.</summary>
    public string Text { get; set; } = "";

    /// <summary>In points.</summary>
    public double FontSize { get; set; } = 14;

    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Uppercase { get; set; }
    public TextAlign Align { get; set; } = TextAlign.Center;
    public VerticalAlign VerticalAlign { get; set; } = VerticalAlign.Middle;

    /// <summary>Text colour, or a box's fill, as "#rrggbb". Ignored when <see cref="UseGroupColour"/> is set.</summary>
    public string Colour { get; set; } = "#222222";

    /// <summary>Uses the colour of the card's group instead of <see cref="Colour"/>.</summary>
    public bool UseGroupColour { get; set; }

    /// <summary>For boxes: rounded corners, in millimetres.</summary>
    public double CornerRadius { get; set; }

    /// <summary>For images: the picture as a data URL ("data:image/…").</summary>
    public string? ImageUrl { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true, WriteIndented = false)]
[JsonSerializable(typeof(CardDesign))]
internal sealed partial class CardDesignJsonContext : JsonSerializerContext;

/// <summary>
/// A table card drawn in the card designer: items placed on an A5 card (210 × 148 mm, landscape). It is turned into an
/// ordinary table-card template (<see cref="ToTemplate"/>), so printing works exactly as with a hand-written one:
/// paper, tent cards and a card per seat all still apply.
/// </summary>
public sealed class CardDesign
{
    public const double CardWidth = 210;
    public const double CardHeight = 148;

    /// <summary>Fonts offered in the designer: a name and the CSS font stack.</summary>
    public static readonly IReadOnlyList<(string Name, string Css)> Fonts =
    [
        ("Sans serif", "\"Segoe UI\", \"Helvetica Neue\", Arial, sans-serif"),
        ("Serif", "Georgia, \"Times New Roman\", serif"),
        ("Rounded", "\"Trebuchet MS\", \"Segoe UI\", sans-serif"),
        ("Handwriting", "\"Segoe Script\", \"Brush Script MT\", cursive"),
    ];

    public string Font { get; set; } = Fonts[0].Name;

    public List<CardItem> Items { get; set; } = [];

    /// <summary>A design that looks like the built-in card template.</summary>
    public static CardDesign Default() => new()
    {
        Items =
        [
            new() { Kind = CardItemKind.Box, X = 0, Y = 0, Width = CardWidth, Height = 7, UseGroupColour = true },
            new() { X = 20, Y = 11, Width = 170, Height = 8, Text = "{{EventName}}", FontSize = 11, Uppercase = true, Colour = "#777777" },
            new() { X = 15, Y = 40, Width = 180, Height = 42, Text = "{{GroupName}}", FontSize = 40, Bold = true },
            new()
            {
                X = 20, Y = 84, Width = 170, Height = 12, FontSize = 16, Colour = "#444444",
                Text = "{{#IsSplit}}{{PeopleHere}} of {{PeopleCount}} people · also at "
                    + "{{#SeveralOtherTables}}tables{{/SeveralOtherTables}}{{^SeveralOtherTables}}table{{/SeveralOtherTables}} {{OtherTables}}{{/IsSplit}}"
                    + "{{^IsSplit}}{{PeopleCount}} {{#OnePerson}}person{{/OnePerson}}{{^OnePerson}}people{{/OnePerson}}{{/IsSplit}}",
            },
            new() { X = 20, Y = 124, Width = 170, Height = 12, Text = "Table {{TableNumber}} · {{FloorName}}", FontSize = 15, Colour = "#555555" },
            new() { X = 160, Y = 133, Width = 42, Height = 8, Text = "{{#SeatNumber}}Seat {{SeatNumber}}{{/SeatNumber}}", FontSize = 10, Align = TextAlign.Right, Colour = "#999999" },
        ],
    };

    public string ToJson() => JsonSerializer.Serialize(this, CardDesignJsonContext.Default.CardDesign);

    /// <summary>Reads a saved design; null when it cannot be read.</summary>
    public static CardDesign? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize(json, CardDesignJsonContext.Default.CardDesign);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public CardDesign Clone() => FromJson(ToJson())!;

    /// <summary>The CSS font stack of <see cref="Font"/>.</summary>
    public string FontCss => Fonts.FirstOrDefault(f => f.Name == Font).Css ?? Fonts[0].Css;

    /// <summary>
    /// The markup of one card's items, as template text: placeholders stay in, literal text is escaped.
    /// Used both for the template and for the designer's preview.
    /// </summary>
    public string ItemsMarkup(bool forPreview = false)
    {
        var html = new StringBuilder();
        foreach (var item in Items)
            html.Append(ItemMarkup(item, forPreview));
        return html.ToString();
    }

    /// <summary>One item as template text, absolutely placed in millimetres on the card.</summary>
    public static string ItemMarkup(CardItem item, bool forPreview = false)
    {
        var box = FormattableString.Invariant($"left: {item.X:0.##}mm; top: {item.Y:0.##}mm; width: {Math.Max(1, item.Width):0.##}mm; height: {Math.Max(1, item.Height):0.##}mm;");
        var colour = item.UseGroupColour ? "{{Colour}}" : SafeColour(item.Colour);
        var id = forPreview ? $" data-item=\"{item.Id:N}\"" : "";
        switch (item.Kind)
        {
            case CardItemKind.Box:
                return $"<div class=\"box\"{id} style=\"{box} background: {colour};{Invariant($" border-radius: {Math.Max(0, item.CornerRadius):0.##}mm;")}\"></div>";
            case CardItemKind.QrCode:
                var qr = $"<div class=\"box qr\"{id} style=\"{box}\">{{{{GuestPageQr}}}}</div>";
                // In the designer the item is shown even without an address, so it can be placed.
                return forPreview ? qr : $"{{{{#GuestPageUrl}}}}{qr}{{{{/GuestPageUrl}}}}";
            case CardItemKind.Image:
                var src = item.ImageUrl is { } url && url.StartsWith("data:image/", StringComparison.Ordinal) ? HtmlTemplate.Escape(url) : "";
                return $"<img class=\"box image\"{id} style=\"{box}\" src=\"{src}\" alt=\"\">";
            default:
                var style = new StringBuilder(box);
                style.Append(Invariant($" font-size: {Math.Clamp(item.FontSize, 4, 200):0.##}pt;"));
                style.Append($" color: {colour};");
                style.Append($" text-align: {item.Align.ToString().ToLowerInvariant()};");
                style.Append($" justify-content: {item.VerticalAlign switch { VerticalAlign.Top => "flex-start", VerticalAlign.Bottom => "flex-end", _ => "center" }};");
                if (item.Bold) style.Append(" font-weight: 600;");
                if (item.Italic) style.Append(" font-style: italic;");
                if (item.Uppercase) style.Append(" text-transform: uppercase; letter-spacing: 0.06em;");
                // The text is the user's literal text with placeholders: escaping it leaves {{…}} as they are.
                var text = HtmlTemplate.Escape(item.Text).Replace("\r\n", "\n").Replace("\n", "<br>");
                return $"<div class=\"text\"{id} style=\"{style}\"><span>{text}</span></div>";
        }
    }

    /// <summary>The whole table-card template for this design.</summary>
    public string ToTemplate()
    {
        var items = ItemsMarkup();
        return """
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <title>Table cards · {{EventName}}</title>
            {{! Made with the card designer. Editing this HTML by hand is fine, but saving the design again replaces it. }}
            <style>
              @page { size: {{PageSize}}; margin: 0; }
              * { box-sizing: border-box; }
              html, body { margin: 0; padding: 0; }
              body { font-family: {{CssFont}}; color: #222; -webkit-print-color-adjust: exact; print-color-adjust: exact; }
              .sheet { width: {{SheetWidthMm}}mm; height: {{SheetHeightMm}}mm; overflow: hidden; break-after: page; }
              .sheet:last-child { break-after: auto; }
              .card { position: relative; width: 210mm; height: {{CardHeightMm}}mm; overflow: hidden; }
              .card + .card { border-top: 0.3mm dashed #bbb; }
              .card + .card::before { content: "cut"; position: absolute; z-index: 1; top: 1mm; left: 4mm; font-size: 7pt; color: #999; }
              .folded .card + .card::before { content: "fold"; }
              .card.flipped { transform: rotate(180deg); }
              .text { position: absolute; display: flex; flex-direction: column; overflow: hidden; line-height: 1.15; overflow-wrap: anywhere; }
              .box { position: absolute; }
              .image { object-fit: contain; }
              .qr .qr-code { width: 100%; height: 100%; display: block; }
              .empty { padding: 20mm; font-size: 14pt; }
              @media screen {
                body { background: #d9d9d9; padding: 8mm 0; }
                .sheet { margin: 0 auto 8mm; background: #fff; box-shadow: 0 1mm 4mm rgba(0, 0, 0, 0.25); }
                .empty { background: #fff; width: 210mm; margin: 0 auto; }
              }
            </style>
            </head>
            <body>
            {{#Sheets}}
            <section class="sheet{{#Folded}} folded{{/Folded}}">
              {{#Cards}}
              <div class="card{{#Flipped}} flipped{{/Flipped}}">
            {{ITEMS}}
              </div>
              {{/Cards}}
            </section>
            {{/Sheets}}
            {{^Cards}}<p class="empty">No groups are seated yet. Make a plan first.</p>{{/Cards}}
            </body>
            </html>

            """.Replace("{{CssFont}}", FontCss).Replace("{{ITEMS}}", items);
    }

    private static readonly Regex HexColour = new("^#[0-9a-fA-F]{3}([0-9a-fA-F]{3})?$");

    /// <summary>Only plain hex colours get into the CSS.</summary>
    public static string SafeColour(string? colour) => colour is not null && HexColour.IsMatch(colour) ? colour : "#222222";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
