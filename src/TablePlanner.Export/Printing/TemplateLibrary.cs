using System.Text.Json;
using System.Text.Json.Serialization;

namespace TablePlanner.Export.Printing;

/// <summary>A template the user saved under a name, for one kind of print.</summary>
public sealed class SavedTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public PrintKind Kind { get; set; }
    public string Html { get; set; } = "";

    /// <summary>For table cards made in the card designer: the design (see <see cref="CardDesign"/>), so it opens again.</summary>
    public string? DesignJson { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    /// <summary>True when the template was made in the card designer and not edited by hand since.</summary>
    public bool IsUnchangedDesign => CardDesign.FromJson(DesignJson) is { } design && design.ToTemplate() == Html;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(TemplateLibrary))]
internal sealed partial class TemplateLibraryJsonContext : JsonSerializerContext;

/// <summary>
/// The user's print templates: any number per kind, each with a name, and which one is used for each kind. The
/// built-in templates are always there and cannot be changed; a kind with nothing selected uses its built-in one.
/// Kept in the browser, separately from projects, so templates are reused for every event.
/// </summary>
public sealed class TemplateLibrary
{
    public const string BuiltInName = "Built-in";

    public List<SavedTemplate> Templates { get; set; } = [];

    /// <summary>The template used for each kind; a kind that is missing uses the built-in template.</summary>
    public Dictionary<PrintKind, Guid> Selected { get; set; } = [];

    /// <summary>The saved templates of a kind, by name.</summary>
    public IReadOnlyList<SavedTemplate> For(PrintKind kind) =>
        Templates.Where(t => t.Kind == kind).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public SavedTemplate? Find(Guid id) => Templates.FirstOrDefault(t => t.Id == id);

    /// <summary>The selected saved template of a kind, or null for the built-in one.</summary>
    public SavedTemplate? SelectedTemplate(PrintKind kind) =>
        Selected.TryGetValue(kind, out var id) && Find(id) is { Kind: var k } t && k == kind ? t : null;

    /// <summary>The template text used for a kind.</summary>
    public string Html(PrintKind kind) => SelectedTemplate(kind)?.Html ?? DefaultTemplates.For(kind);

    /// <summary>Uses a saved template for its kind, or the built-in one for <paramref name="id"/> null.</summary>
    public void Select(PrintKind kind, Guid? id)
    {
        if (id is { } value && Find(value) is { } t && t.Kind == kind)
            Selected[kind] = value;
        else
            Selected.Remove(kind);
    }

    /// <summary>Saves a new template under a name not yet used for its kind, and selects it.</summary>
    public SavedTemplate Add(PrintKind kind, string name, string html, DateTimeOffset savedAt, string? designJson = null)
    {
        var template = new SavedTemplate { Kind = kind, Name = UniqueName(kind, name), Html = html, DesignJson = designJson, SavedAt = savedAt };
        Templates.Add(template);
        Selected[kind] = template.Id;
        return template;
    }

    public void Rename(Guid id, string name)
    {
        if (Find(id) is { } t)
            t.Name = UniqueName(t.Kind, name, except: id);
    }

    /// <summary>Removes a saved template; a kind that used it goes back to its built-in template.</summary>
    public void Delete(Guid id)
    {
        if (Find(id) is not { } t)
            return;
        Templates.Remove(t);
        if (Selected.TryGetValue(t.Kind, out var selected) && selected == id)
            Selected.Remove(t.Kind);
    }

    /// <summary>
    /// <paramref name="name"/>, or with " (2)", " (3)" … added when another template of the kind already has it.
    /// "Built-in" is taken too, and an empty name becomes "My template".
    /// </summary>
    public string UniqueName(PrintKind kind, string name, Guid? except = null)
    {
        var wanted = string.IsNullOrWhiteSpace(name) ? "My template" : name.Trim();
        var taken = Templates.Where(t => t.Kind == kind && t.Id != except).Select(t => t.Name)
            .Append(BuiltInName).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (!taken.Contains(wanted))
            return wanted;
        for (var i = 2; ; i++)
            if (!taken.Contains($"{wanted} ({i})"))
                return $"{wanted} ({i})";
    }

    public string ToJson() => JsonSerializer.Serialize(this, TemplateLibraryJsonContext.Default.TemplateLibrary);

    /// <summary>Reads a saved library; null when there is none or it cannot be read.</summary>
    public static TemplateLibrary? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize(json, TemplateLibraryJsonContext.Default.TemplateLibrary);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The library made from what earlier versions kept: one own template per kind (null = built-in) and one card
    /// design. Each own template becomes a saved, selected template; a design whose template is in use is kept with it.
    /// </summary>
    public static TemplateLibrary FromEarlierVersion(IReadOnlyDictionary<PrintKind, string?> ownTemplates, string? designJson, DateTimeOffset now)
    {
        var library = new TemplateLibrary();
        var design = CardDesign.FromJson(designJson);
        foreach (var (kind, html) in ownTemplates)
        {
            if (html is null)
                continue;
            var fromDesign = kind == PrintKind.TableCards && design is not null && design.ToTemplate() == html;
            library.Add(kind, fromDesign ? "My card design" : "My template", html, now, fromDesign ? designJson : null);
        }
        // A design that was saved but is not in use (its template was edited or reset) is kept too, unselected.
        if (design is not null && library.Templates.All(t => t.DesignJson is null))
        {
            var selected = library.SelectedTemplate(PrintKind.TableCards)?.Id;
            library.Add(PrintKind.TableCards, "My card design", design.ToTemplate(), now, designJson);
            library.Select(PrintKind.TableCards, selected);
        }
        return library;
    }
}
