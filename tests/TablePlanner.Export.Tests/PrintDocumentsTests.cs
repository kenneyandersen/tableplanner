using TablePlanner.Core;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;
using TablePlanner.Export.Printing;

namespace TablePlanner.Export.Tests;

public class PrintDocumentsTests
{
    /// <summary>
    /// Two tables of 4 per side. Table 101: Zeta (2) on top seats 0–1, then Alpha's first 3 on top 2–3 and bottom 4.
    /// Table 102: the other 2 of Alpha.
    /// </summary>
    private static (EventProject Project, Table First, Table Second, Group Alpha, Group Zeta) Setup()
    {
        var first = new Table { Number = 101, SeatsPerSide = 4 };
        var second = new Table { Number = 102, SeatsPerSide = 4, Y = 3 };
        var alpha = new Group { Name = "Alpha", PeopleCount = 5 };
        var zeta = new Group { Name = "Zeta", PeopleCount = 2, Colour = "#123456" };
        var project = new EventProject { Name = "Gala" };
        project.Setup.Floors.Add(new Floor { Name = "Ground", Number = 1, Tables = [first, second] });
        project.Setup.Groups.AddRange([alpha, zeta]);
        void Seat(Table t, int i, Group g) => project.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(t.Id, i), g.Id));
        Seat(first, 2, alpha);
        Seat(first, 3, alpha);
        Seat(first, 4, alpha);
        Seat(first, 0, zeta);
        Seat(first, 1, zeta);
        Seat(second, 0, alpha);
        Seat(second, 1, alpha);
        return (project, first, second, alpha, zeta);
    }

    private static List<TemplateData> List(TemplateData data, string key) => Assert.IsAssignableFrom<IEnumerable<TemplateData>>(data[key]).ToList();

    [Fact]
    public void There_is_a_card_per_group_per_table_in_seating_order()
    {
        var (project, _, _, _, _) = Setup();

        var cards = List(TableCards.BuildData(project.Name, new PrintedPlan(project.Setup, project.CurrentAssignment)), "Cards");

        Assert.Equal(["Zeta 101", "Alpha 101", "Alpha 102"], cards.Select(c => $"{c["GroupName"]} {c["TableNumber"]}"));
        Assert.Equal([2, 3, 2], cards.Select(c => (int)c["PeopleHere"]!));
    }

    [Fact]
    public void A_split_groups_cards_name_its_other_tables()
    {
        var (project, _, _, _, _) = Setup();

        var cards = List(TableCards.BuildData(project.Name, new PrintedPlan(project.Setup, project.CurrentAssignment)), "Cards");

        Assert.Equal([false, true, true], cards.Select(c => (bool)c["IsSplit"]!));
        Assert.Equal(["", "102", "101"], cards.Select(c => (string)c["OtherTables"]!));
        Assert.Equal("#123456", cards[0]["Colour"]);
    }

    [Fact]
    public void Cards_are_put_two_to_a_sheet()
    {
        var (project, _, _, _, _) = Setup();

        var sheets = List(TableCards.BuildData(project.Name, new PrintedPlan(project.Setup, project.CurrentAssignment)), "Sheets");

        Assert.Equal([2, 1], sheets.Select(s => List(s, "Cards").Count));
    }

    [Fact]
    public void Seats_that_no_longer_exist_and_deleted_groups_are_left_out()
    {
        var (project, first, _, _, _) = Setup();
        project.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(first.Id, 99), project.Setup.Groups[0].Id));
        project.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(first.Id, 5), Guid.NewGuid()));
        project.CurrentAssignment.Seats.Add(new SeatAssignment(new SeatId(Guid.NewGuid(), 0), project.Setup.Groups[0].Id));

        var plan = new PrintedPlan(project.Setup, project.CurrentAssignment);

        Assert.Equal(7, plan.GroupAt.Count);
        Assert.Equal(5, plan.Seated(project.Setup.Groups[0]));
    }

    [Fact]
    public void The_poster_lists_seated_groups_by_name_and_letter()
    {
        var (project, _, _, _, _) = Setup();
        project.Setup.Groups.Add(new Group { Name = "beta", PeopleCount = 3 });

        var data = Poster.BuildData(project.Name, new PrintedPlan(project.Setup, project.CurrentAssignment), new PosterOptions());

        var groups = List(data, "Groups");
        Assert.Equal(["Alpha", "Zeta"], groups.Select(g => g["GroupName"]));
        Assert.Equal("101, 102", groups[0]["Tables"]);
        Assert.Equal(["A", "Z"], List(data, "Letters").Select(l => l["Letter"]));
        Assert.Equal(7, data["PeopleSeated"]);
        Assert.Equal(false, data["ShowFloorNames"]);
    }

    [Theory]
    [InlineData(PaperSize.A3, true, 420, 297, "landscape")]
    [InlineData(PaperSize.A4, false, 210, 297, "portrait")]
    public void The_poster_page_size_follows_the_paper_and_orientation(PaperSize paper, bool landscape, int width, int height, string orientation)
    {
        var (project, _, _, _, _) = Setup();

        var data = Poster.BuildData(project.Name, new PrintedPlan(project.Setup, project.CurrentAssignment), new PosterOptions(paper, landscape));

        Assert.Equal(width, data["PageWidthMm"]);
        Assert.Equal(height, data["PageHeightMm"]);
        Assert.Equal(orientation, data["Orientation"]);
        Assert.Equal(paper.ToString(), data["PaperSize"]);
    }

    [Fact]
    public void The_floor_plan_shows_seats_in_group_colours_and_escaped_names()
    {
        var (project, _, _, _, zeta) = Setup();
        zeta.Name = "<Z>&Co";

        var floor = project.Setup.Floors[0];
        var svg = FloorPlanSvg.Render(floor, new PrintedPlan(project.Setup, project.CurrentAssignment), GroupColours.For(project.Setup));

        Assert.StartsWith("<svg ", svg);
        Assert.Contains("fill=\"#123456\"", svg);
        Assert.Contains(">&lt;Z&gt;&amp;Co</text>", svg);
        Assert.Contains(">101</text>", svg);
        Assert.Equal(16, svg.Split("class=\"seat\"").Length - 1);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_background_image_is_on_the_poster_only_when_wanted(bool showOnPoster, bool expected)
    {
        var (project, _, _, _, _) = Setup();
        var floor = project.Setup.Floors[0];
        var imageId = Guid.NewGuid();
        floor.Background = new FloorBackground { ImageId = imageId, PixelWidth = 100, PixelHeight = 50, MetresPerPixel = 0.2, ShowOnPoster = showOnPoster };
        var images = new Dictionary<Guid, string> { [imageId] = "data:image/jpeg;base64,AAAA" };

        var data = Poster.BuildData(project.Name, new PrintedPlan(project.Setup, project.CurrentAssignment), new PosterOptions(), images);
        var svg = ((RawHtml)List(data, "Floors")[0]["FloorPlan"]!).Html;

        Assert.Equal(expected, svg.Contains("<image class=\"background-image\" href=\"data:image/jpeg;base64,AAAA\""));
        if (expected)
            Assert.Contains("width=\"20\" height=\"10\" opacity=\"0.3\"", svg);
    }

    /// <summary>A plan that seats every group of the example project, table by table.</summary>
    private static EventProject SeatedExample()
    {
        var project = ExampleProject.Create();
        var seats = project.Setup.AllTables.SelectMany(TableGeometry.GetSeats).Where(s => !s.IsDisabled).Select(s => s.Id).ToList();
        var next = 0;
        foreach (var group in project.Setup.Groups)
            for (var i = 0; i < group.PeopleCount && next < seats.Count; i++)
                project.CurrentAssignment.Seats.Add(new SeatAssignment(seats[next++], group.Id));
        return project;
    }

    [Theory]
    [InlineData(PrintKind.TableCards)]
    [InlineData(PrintKind.Poster)]
    public void The_built_in_templates_render_without_warnings(PrintKind kind)
    {
        var project = SeatedExample();
        var plan = new PrintedPlan(project.Setup, project.CurrentAssignment);
        var data = kind == PrintKind.TableCards ? TableCards.BuildData(project.Name, plan) : Poster.BuildData(project.Name, plan, new PosterOptions());

        var result = HtmlTemplate.Render(DefaultTemplates.For(kind), data);

        Assert.Empty(result.Warnings);
        Assert.Contains(HtmlTemplate.Escape(project.Setup.Groups[0].Name), result.Html);
        Assert.DoesNotContain("{{", result.Html);
    }

    [Theory]
    [InlineData(PrintKind.TableCards)]
    [InlineData(PrintKind.Poster)]
    public void The_placeholder_help_matches_the_data(PrintKind kind)
    {
        var project = SeatedExample();
        var plan = new PrintedPlan(project.Setup, project.CurrentAssignment);
        var (data, help) = kind == PrintKind.TableCards
            ? (TableCards.BuildData(project.Name, plan), TableCards.Placeholders)
            : (Poster.BuildData(project.Name, plan, new PosterOptions()), Poster.Placeholders);

        AssertMatches(help, data);

        static void AssertMatches(IReadOnlyList<Placeholder> help, TemplateData data)
        {
            Assert.Equal(help.Select(p => p.Name).Order(), data.Keys.Order());
            foreach (var placeholder in help.Where(p => p.Items is not null))
                AssertMatches(placeholder.Items!, List(data, placeholder.Name).First());
        }
    }
}
