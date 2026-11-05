using TablePlanner.Core.Model;
using TablePlanner.Export.Printing;

namespace TablePlanner.Export.Tests;

public class QrCodeTests
{
    [Theory]
    [InlineData("https://example.org/party/", true)]
    [InlineData("http://example.org", true)]
    [InlineData("  https://example.org  ", true)]
    [InlineData("example.org", false)]
    [InlineData("ftp://example.org", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_web_addresses_get_a_code(string? text, bool expected)
    {
        Assert.Equal(expected, QrCode.IsWebAddress(text));
    }

    [Fact]
    public void The_svg_is_square_with_a_quiet_zone_and_starts_with_the_finder_pattern()
    {
        var svg = QrCode.Svg("https://example.org/party/");

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"qr-code\" viewBox=\"0 0 ", svg);
        var size = int.Parse(svg.Split("viewBox=\"0 0 ")[1].Split(' ')[0]);
        // Versions grow by 4 modules from 21; the quiet zone adds 4 on each side.
        Assert.Equal(0, (size - 8 - 21) % 4);
        Assert.Contains($"viewBox=\"0 0 {size} {size}\"", svg);
        Assert.Contains("<path d=\"M4 4h7v1h-7z", svg);
    }

    [Fact]
    public void The_png_is_a_png()
    {
        var png = QrCode.Png("https://example.org/");

        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Take(4));
    }

    [Fact]
    public void The_poster_shows_the_code_only_with_a_valid_address()
    {
        var project = new EventProject { Name = "Gala" };
        var plan = new PrintedPlan(project.Setup, project.CurrentAssignment);
        string Render(string? url) =>
            HtmlTemplate.Render(DefaultTemplates.For(PrintKind.Poster), Poster.BuildData(project.Name, plan, new PosterOptions(), guestPageUrl: url)).Html;

        Assert.Contains("class=\"qr-code\"", Render("https://example.org/gala/"));
        Assert.DoesNotContain("class=\"qr-code\"", Render("not an address"));
        Assert.DoesNotContain("class=\"qr-code\"", Render(null));
    }
}
