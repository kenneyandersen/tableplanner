using System.Reflection;

namespace TablePlanner.Export.Printing;

public enum PrintKind
{
    TableCards,
    Poster,
    GuestPage,
}

/// <summary>The built-in templates, used until the user saves their own (Printing/Templates/*.html).</summary>
public static class DefaultTemplates
{
    public static string For(PrintKind kind) => Read(kind switch
    {
        PrintKind.TableCards => "cards.html",
        PrintKind.Poster => "poster.html",
        _ => "guest.html",
    });

    private static string Read(string fileName)
    {
        var assembly = typeof(DefaultTemplates).Assembly;
        using var stream = assembly.GetManifestResourceStream($"TablePlanner.Export.Printing.Templates.{fileName}")
            ?? throw new InvalidOperationException($"Built-in template {fileName} is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
