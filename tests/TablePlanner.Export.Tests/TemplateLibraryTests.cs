using TablePlanner.Export.Printing;

namespace TablePlanner.Export.Tests;

public class TemplateLibraryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Without_a_selection_the_built_in_template_is_used()
    {
        var library = new TemplateLibrary();

        Assert.Equal(DefaultTemplates.For(PrintKind.Poster), library.Html(PrintKind.Poster));
        Assert.Null(library.SelectedTemplate(PrintKind.Poster));
    }

    [Fact]
    public void Several_templates_per_kind_and_one_is_selected()
    {
        var library = new TemplateLibrary();
        var big = library.Add(PrintKind.TableCards, "Big names", "<p>big</p>", Now);
        var small = library.Add(PrintKind.TableCards, "Small", "<p>small</p>", Now);

        Assert.Equal("<p>small</p>", library.Html(PrintKind.TableCards));
        library.Select(PrintKind.TableCards, big.Id);
        Assert.Equal("<p>big</p>", library.Html(PrintKind.TableCards));
        library.Select(PrintKind.TableCards, null);
        Assert.Equal(DefaultTemplates.For(PrintKind.TableCards), library.Html(PrintKind.TableCards));
        Assert.Equal(["Big names", "Small"], library.For(PrintKind.TableCards).Select(t => t.Name));
        Assert.Equal(small.Id, library.For(PrintKind.TableCards)[1].Id);
    }

    [Fact]
    public void Names_are_unique_per_kind_and_built_in_is_taken()
    {
        var library = new TemplateLibrary();
        library.Add(PrintKind.TableCards, "Gala", "", Now);

        Assert.Equal("gala (2)", library.Add(PrintKind.TableCards, "gala", "", Now).Name);
        Assert.Equal("Gala", library.Add(PrintKind.Poster, "Gala", "", Now).Name);
        Assert.Equal("Built-in (2)", library.UniqueName(PrintKind.Poster, "Built-in"));
        Assert.Equal("My template", library.UniqueName(PrintKind.Poster, "  "));
    }

    [Fact]
    public void Renaming_keeps_names_unique_but_may_keep_its_own()
    {
        var library = new TemplateLibrary();
        var a = library.Add(PrintKind.TableCards, "A", "", Now);
        library.Add(PrintKind.TableCards, "B", "", Now);

        library.Rename(a.Id, "A");
        Assert.Equal("A", a.Name);
        library.Rename(a.Id, "B");
        Assert.Equal("B (2)", a.Name);
    }

    [Fact]
    public void Deleting_the_selected_template_goes_back_to_the_built_in_one()
    {
        var library = new TemplateLibrary();
        var t = library.Add(PrintKind.GuestPage, "Mine", "<p/>", Now);

        library.Delete(t.Id);

        Assert.Empty(library.Templates);
        Assert.Null(library.SelectedTemplate(PrintKind.GuestPage));
    }

    [Fact]
    public void A_template_of_another_kind_cannot_be_selected()
    {
        var library = new TemplateLibrary();
        var poster = library.Add(PrintKind.Poster, "P", "<p/>", Now);

        library.Select(PrintKind.TableCards, poster.Id);

        Assert.Null(library.SelectedTemplate(PrintKind.TableCards));
    }

    [Fact]
    public void The_library_survives_saving_and_reading()
    {
        var library = new TemplateLibrary();
        library.Add(PrintKind.TableCards, "Cards", "<p>c</p>", Now, CardDesign.Default().ToJson());
        library.Add(PrintKind.Poster, "Poster", "<p>p</p>", Now);

        var read = TemplateLibrary.FromJson(library.ToJson())!;

        Assert.Equal(library.ToJson(), read.ToJson());
        Assert.Equal("<p>p</p>", read.Html(PrintKind.Poster));
        Assert.Null(TemplateLibrary.FromJson("nonsense"));
    }

    [Fact]
    public void Templates_from_the_earlier_version_become_named_and_selected()
    {
        var design = CardDesign.Default();
        var own = new Dictionary<PrintKind, string?>
        {
            [PrintKind.TableCards] = design.ToTemplate(),
            [PrintKind.Poster] = "<p>my poster</p>",
            [PrintKind.GuestPage] = null,
        };

        var library = TemplateLibrary.FromEarlierVersion(own, design.ToJson(), Now);

        Assert.Equal("My card design", library.SelectedTemplate(PrintKind.TableCards)!.Name);
        Assert.True(library.SelectedTemplate(PrintKind.TableCards)!.IsUnchangedDesign);
        Assert.Equal("<p>my poster</p>", library.Html(PrintKind.Poster));
        Assert.Null(library.SelectedTemplate(PrintKind.GuestPage));
    }

    [Fact]
    public void A_design_whose_template_was_edited_by_hand_is_kept_beside_it()
    {
        var design = CardDesign.Default();
        var own = new Dictionary<PrintKind, string?> { [PrintKind.TableCards] = "<p>edited</p>" };

        var library = TemplateLibrary.FromEarlierVersion(own, design.ToJson(), Now);

        Assert.Equal(["My card design", "My template"], library.For(PrintKind.TableCards).Select(t => t.Name));
        Assert.Equal("<p>edited</p>", library.Html(PrintKind.TableCards));
    }
}
