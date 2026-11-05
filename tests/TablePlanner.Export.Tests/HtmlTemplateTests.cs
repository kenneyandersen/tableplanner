using TablePlanner.Export.Printing;

namespace TablePlanner.Export.Tests;

public class HtmlTemplateTests
{
    private static string Render(string template, TemplateData data)
    {
        var result = HtmlTemplate.Render(template, data);
        Assert.Empty(result.Warnings);
        return result.Html;
    }

    [Fact]
    public void Values_are_escaped_unless_raw()
    {
        var data = new TemplateData { ["Name"] = "<Tom & \"Jerry\">", ["Svg"] = new RawHtml("<svg/>") };

        Assert.Equal("&lt;Tom &amp; &quot;Jerry&quot;&gt; <svg/> <Tom & \"Jerry\">", Render("{{Name}} {{Svg}} {{{Name}}}", data));
    }

    [Fact]
    public void Names_are_case_insensitive_and_may_have_spaces()
    {
        Assert.Equal("7", Render("{{ peoplecount }}", new TemplateData { ["PeopleCount"] = 7 }));
    }

    [Fact]
    public void A_list_section_repeats_and_sees_outer_values()
    {
        var data = new TemplateData
        {
            ["Event"] = "Gala",
            ["Items"] = new List<TemplateData> { new() { ["Name"] = "A" }, new() { ["Name"] = "B" } },
        };

        Assert.Equal("[A at Gala][B at Gala]", Render("{{#Items}}[{{Name}} at {{Event}}]{{/Items}}", data));
    }

    [Theory]
    [InlineData(true, "yes")]
    [InlineData(false, "no")]
    public void Sections_show_on_true_and_inverted_sections_on_false(bool value, string expected)
    {
        Assert.Equal(expected, Render("{{#On}}yes{{/On}}{{^On}}no{{/On}}", new TemplateData { ["On"] = value }));
    }

    [Fact]
    public void Empty_texts_zero_and_empty_lists_count_as_false()
    {
        var data = new TemplateData { ["Text"] = "", ["Zero"] = 0, ["List"] = new List<TemplateData>() };

        Assert.Equal("123", Render("{{#Text}}x{{/Text}}{{^Text}}1{{/Text}}{{^Zero}}2{{/Zero}}{{#List}}x{{/List}}{{^List}}3{{/List}}", data));
    }

    [Fact]
    public void Comments_are_left_out()
    {
        Assert.Equal("ab", Render("a{{! a note }}b", new TemplateData()));
    }

    [Fact]
    public void Unknown_placeholders_give_an_empty_text_and_one_warning()
    {
        var result = HtmlTemplate.Render("x\n{{Nmae}} {{Nmae}}", new TemplateData { ["Name"] = "A" });

        Assert.Equal("x\n ", result.Html);
        Assert.Equal(["Unknown placeholder {{Nmae}} (line 2)."], result.Warnings);
    }

    [Theory]
    [InlineData("a\n{{#Items}}b", 2, "never closed")]
    [InlineData("{{/Items}}", 1, "never opened")]
    [InlineData("{{#A}}{{#B}}\n{{/A}}{{/B}}", 2, "was expected")]
    [InlineData("a\nb {{Name", 2, "not closed")]
    [InlineData("{{ }}", 1, "no name")]
    public void Broken_templates_report_the_line(string template, int line, string message)
    {
        var error = Assert.Throws<TemplateFormatException>(() => HtmlTemplate.Render(template, new TemplateData()));

        Assert.Equal(line, error.Line);
        Assert.Contains(message, error.Message);
    }
}
