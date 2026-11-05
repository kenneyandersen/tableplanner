using System.Globalization;
using System.Text;

namespace TablePlanner.Export.Printing;

/// <summary>
/// The values a template is filled with. Keys are placeholder names (case-insensitive); values are strings,
/// numbers, booleans, <see cref="RawHtml"/>, a nested <see cref="TemplateData"/>, or a list of them.
/// </summary>
public sealed class TemplateData() : Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

/// <summary>Markup made by the program (such as a floor plan SVG) that is inserted as it is, without escaping.</summary>
public sealed record RawHtml(string Html);

/// <summary>A template that cannot be read, e.g. a section that is never closed.</summary>
public sealed class TemplateFormatException(string message, int line) : Exception($"Line {line}: {message}")
{
    public int Line { get; } = line;
}

/// <summary>The filled-in template and anything that looked wrong, such as unknown placeholders.</summary>
public sealed record TemplateResult(string Html, IReadOnlyList<string> Warnings);

/// <summary>
/// Fills an HTML template with a small subset of Mustache:
/// <list type="bullet">
/// <item><c>{{Name}}</c>: the value, HTML-escaped (<see cref="RawHtml"/> is inserted as is).</item>
/// <item><c>{{{Name}}}</c>: the value without escaping.</item>
/// <item><c>{{#Name}}…{{/Name}}</c>: repeated for each item of a list, or shown once if the value is true,
///   a non-empty text or a non-zero number. Inside, the item's values are available, and so are the outer ones.</item>
/// <item><c>{{^Name}}…{{/Name}}</c>: shown only if the value is false, empty, zero or an empty list.</item>
/// <item><c>{{! comment }}</c>: left out.</item>
/// </list>
/// Unknown placeholders give an empty text and a warning, so a typo shows up in the preview instead of failing.
/// </summary>
public static class HtmlTemplate
{
    public static TemplateResult Render(string template, TemplateData data)
    {
        var nodes = Parse(template);
        var output = new StringBuilder(template.Length * 2);
        var warnings = new List<string>();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scopes = new List<TemplateData> { data };

        object? Lookup(string name, int line)
        {
            for (var i = scopes.Count - 1; i >= 0; i--)
                if (scopes[i].TryGetValue(name, out var value))
                    return value;
            if (reported.Add(name))
                warnings.Add($"Unknown placeholder {{{{{name}}}}} (line {line}).");
            return null;
        }

        void RenderNodes(IEnumerable<Node> list)
        {
            foreach (var node in list)
            {
                switch (node)
                {
                    case TextNode text:
                        output.Append(text.Text);
                        break;
                    case VariableNode variable:
                        var value = Lookup(variable.Name, variable.Line);
                        var formatted = Format(value);
                        output.Append(variable.Raw || value is RawHtml ? formatted : Escape(formatted));
                        break;
                    case SectionNode section:
                        var sectionValue = Lookup(section.Name, section.Line);
                        if (section.Inverted)
                        {
                            if (!IsTruthy(sectionValue))
                                RenderNodes(section.Children);
                        }
                        else if (sectionValue is IEnumerable<TemplateData> items)
                        {
                            foreach (var item in items)
                            {
                                scopes.Add(item);
                                RenderNodes(section.Children);
                                scopes.RemoveAt(scopes.Count - 1);
                            }
                        }
                        else if (sectionValue is TemplateData single)
                        {
                            scopes.Add(single);
                            RenderNodes(section.Children);
                            scopes.RemoveAt(scopes.Count - 1);
                        }
                        else if (IsTruthy(sectionValue))
                        {
                            RenderNodes(section.Children);
                        }
                        break;
                }
            }
        }

        RenderNodes(nodes);
        return new TemplateResult(output.ToString(), warnings);
    }

    /// <summary>Escapes text for use in HTML content and attribute values.</summary>
    public static string Escape(string text)
    {
        if (text.AsSpan().IndexOfAny("&<>\"'") < 0)
            return text;
        var builder = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => c.ToString(),
            });
        }
        return builder.ToString();
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s,
        RawHtml raw => raw.Html,
        bool b => b ? "true" : "",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable<TemplateData> => "",
        TemplateData => "",
        _ => value.ToString() ?? "",
    };

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        int i => i != 0,
        double d => d != 0,
        RawHtml raw => raw.Html.Length > 0,
        IEnumerable<TemplateData> items => items.Any(),
        _ => true,
    };

    private abstract record Node;

    private sealed record TextNode(string Text) : Node;

    private sealed record VariableNode(string Name, bool Raw, int Line) : Node;

    private sealed record SectionNode(string Name, bool Inverted, List<Node> Children, int Line) : Node;

    private static List<Node> Parse(string template)
    {
        var root = new List<Node>();
        var open = new Stack<SectionNode>();
        List<Node> Current() => open.Count == 0 ? root : open.Peek().Children;

        var position = 0;
        var line = 1;
        while (position < template.Length)
        {
            var start = template.IndexOf("{{", position, StringComparison.Ordinal);
            if (start < 0)
            {
                Current().Add(new TextNode(template[position..]));
                break;
            }
            if (start > position)
            {
                Current().Add(new TextNode(template[position..start]));
                line += CountLines(template, position, start);
            }

            var triple = start + 2 < template.Length && template[start + 2] == '{';
            var closer = triple ? "}}}" : "}}";
            var contentStart = start + (triple ? 3 : 2);
            var end = template.IndexOf(closer, contentStart, StringComparison.Ordinal);
            if (end < 0)
                throw new TemplateFormatException($"'{(triple ? "{{{" : "{{")}' is not closed with '{closer}'.", line);
            var content = template[contentStart..end].Trim();
            var tagLine = line;
            line += CountLines(template, start, end);
            position = end + closer.Length;

            var sigil = !triple && content.Length > 0 && "#^/!".Contains(content[0]) ? content[0] : '\0';
            var name = sigil == '\0' ? content : content[1..].Trim();
            if (sigil == '!')
                continue;
            if (name.Length == 0)
                throw new TemplateFormatException("A placeholder has no name.", tagLine);

            switch (sigil)
            {
                case '#' or '^':
                    var section = new SectionNode(name, sigil == '^', [], tagLine);
                    Current().Add(section);
                    open.Push(section);
                    break;
                case '/':
                    if (open.Count == 0)
                        throw new TemplateFormatException($"{{{{/{name}}}}} closes a section that was never opened.", tagLine);
                    if (!string.Equals(open.Peek().Name, name, StringComparison.OrdinalIgnoreCase))
                        throw new TemplateFormatException($"{{{{/{name}}}}} found where {{{{/{open.Peek().Name}}}}} was expected.", tagLine);
                    open.Pop();
                    break;
                default:
                    Current().Add(new VariableNode(name, triple, tagLine));
                    break;
            }
        }

        if (open.Count > 0)
            throw new TemplateFormatException($"The section {{{{#{open.Peek().Name}}}}} is never closed with {{{{/{open.Peek().Name}}}}}.", open.Peek().Line);
        return root;
    }

    private static int CountLines(string text, int from, int to)
    {
        var count = 0;
        for (var i = from; i < to; i++)
            if (text[i] == '\n')
                count++;
        return count;
    }
}
