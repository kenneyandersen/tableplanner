using TablePlanner.Core.Model;
using TablePlanner.Export.Printing;

namespace TablePlanner.Export.Tests;

public class CardDesignTests
{
    /// <summary>One table with Alpha (3) on its first top seats.</summary>
    private static (EventProject Project, PrintedPlan Plan) Setup()
    {
        var table = new Table { Number = 101, SeatsPerSide = 4 };
        var alpha = new Group { Name = "Alpha & Co", PeopleCount = 3, Colour = "#123456" };
        var project = new EventProject { Name = "Gala" };
        project.Setup.Floors.Add(new Floor { Name = "Ground", Tables = [table] });
        project.Setup.Groups.Add(alpha);
        for (var i = 0; i < 3; i++)
            project.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(table.Id, i), alpha.Id));
        return (project, new PrintedPlan(project.Setup, project.CurrentAssignment));
    }

    private static string Render(CardDesign design, CardOptions? options = null, string? guestPageUrl = null)
    {
        var (project, plan) = Setup();
        var result = HtmlTemplate.Render(design.ToTemplate(), TableCards.BuildData(project.Name, plan, options, guestPageUrl));
        Assert.Empty(result.Warnings);
        return result.Html;
    }

    [Fact]
    public void The_default_design_renders_like_the_built_in_card()
    {
        var html = Render(CardDesign.Default());

        Assert.Contains("<span>Alpha &amp; Co</span>", html);
        Assert.Contains("<span>Table 101 · Ground</span>", html);
        Assert.Contains("<span>3 people</span>", html);
        Assert.Contains("background: #123456;", html);
        Assert.Contains("@page { size: A4 portrait; margin: 0; }", html);
    }

    [Fact]
    public void Cards_per_seat_and_tents_work_with_a_design_too()
    {
        var html = Render(CardDesign.Default(), new CardOptions(CardsFor.Seat, Folded: true));

        Assert.Equal(3, html.Split("<div class=\"card flipped\">").Length - 1);
        Assert.Contains("<span>Seat 2</span>", html);
        Assert.Contains("<span>3 people</span>", html);
    }

    [Fact]
    public void Literal_text_is_escaped_and_keeps_its_line_breaks()
    {
        var design = new CardDesign { Items = [new CardItem { Text = "<b>Welcome</b>\nto {{EventName}}" }] };

        var html = Render(design);

        Assert.Contains("<span>&lt;b&gt;Welcome&lt;/b&gt;<br>to Gala</span>", html);
    }

    [Fact]
    public void Items_are_placed_in_millimetres_with_their_style()
    {
        var item = new CardItem { X = 10.5, Y = 20, Width = 80, Height = 12.25, FontSize = 18, Bold = true, Align = TextAlign.Left, Text = "x" };

        var markup = CardDesign.ItemMarkup(item);

        Assert.Contains("left: 10.5mm; top: 20mm; width: 80mm; height: 12.25mm;", markup);
        Assert.Contains("font-size: 18pt;", markup);
        Assert.Contains("font-weight: 600;", markup);
        Assert.Contains("text-align: left;", markup);
    }

    [Theory]
    [InlineData("#abc", "#abc")]
    [InlineData("#a1b2c3", "#a1b2c3")]
    [InlineData("red; background: url(x)", "#222222")]
    [InlineData(null, "#222222")]
    public void Only_hex_colours_get_into_the_css(string? colour, string expected)
    {
        Assert.Equal(expected, CardDesign.SafeColour(colour));
    }

    [Fact]
    public void The_qr_code_shows_only_with_a_guest_page_address()
    {
        var design = new CardDesign { Items = [new CardItem { Kind = CardItemKind.QrCode, Width = 30, Height = 30 }] };

        Assert.Contains("class=\"qr-code\"", Render(design, guestPageUrl: "https://example.org/"));
        Assert.DoesNotContain("class=\"box qr\"", Render(design));
    }

    [Fact]
    public void Images_must_be_data_urls_of_pictures()
    {
        var picture = CardDesign.ItemMarkup(new CardItem { Kind = CardItemKind.Image, ImageUrl = "data:image/png;base64,AAAA" });
        var other = CardDesign.ItemMarkup(new CardItem { Kind = CardItemKind.Image, ImageUrl = "https://tracker.example/x.png" });

        Assert.Contains("src=\"data:image/png;base64,AAAA\"", picture);
        Assert.Contains("src=\"\"", other);
    }

    [Fact]
    public void A_design_survives_saving_and_reading()
    {
        var design = CardDesign.Default();
        design.Font = "Serif";
        design.Items[2].Italic = true;

        var read = CardDesign.FromJson(design.ToJson())!;

        Assert.Equal(design.ToJson(), read.ToJson());
        Assert.Null(CardDesign.FromJson("not json"));
        Assert.Contains("Georgia", read.ToTemplate());
    }
}
