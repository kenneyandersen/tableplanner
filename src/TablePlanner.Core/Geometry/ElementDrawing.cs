using System.Globalization;
using System.Text;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Geometry;

/// <summary>
/// The simple drawing of a floor element by kind (steps on stairs, stools at a bar, a door swing at an entrance …), as
/// SVG in floor coordinates (metres), drawn over the element's box and under its label. One drawing for all three
/// renderers (layout editor, plan page, poster and guest page), with its own colours, so it needs no stylesheet.
/// <para>
/// Elements with a direction are drawn in their own frame and turned with them: the width runs left to right and the
/// "front" is the bottom side (where people stand at a bar, the audience side of a stage, the room side of a door).
/// Icons (toilets, cloakroom, DJ booth, photo booth) stay upright, above the label.
/// </para>
/// </summary>
public static class ElementDrawing
{
    private const string Line = "#6b7a8f";
    private const double Thin = 0.03;

    /// <summary>The fill of the element's box, by kind (light, so tables and groups stand out).</summary>
    public static string Fill(FloorElementKind kind) => kind switch
    {
        FloorElementKind.Pillar => "#8d96a3",
        FloorElementKind.Stairs => "#ececec",
        FloorElementKind.Bar or FloorElementKind.Buffet => "#f4e6d4",
        FloorElementKind.Stage or FloorElementKind.DanceFloor or FloorElementKind.DjBooth => "#ede3f3",
        FloorElementKind.Entrance or FloorElementKind.Exit => "#e1f1e3",
        FloorElementKind.Toilets or FloorElementKind.Cloakroom => "#e3edf6",
        FloorElementKind.PhotoBooth => "#f6eadf",
        FloorElementKind.Text => "none",
        _ => "#e3eaf3",
    };

    /// <summary>Whether the label goes below the middle, under an upright icon.</summary>
    private static bool HasIcon(FloorElementKind kind) =>
        kind is FloorElementKind.Toilets or FloorElementKind.Cloakroom or FloorElementKind.DjBooth or FloorElementKind.PhotoBooth;

    /// <summary>
    /// Where the label goes and how big: the middle (or under an icon), moved by the element's label offset; its own size
    /// if set, otherwise fitted to the element (a text element's fills its box).
    /// </summary>
    public static (double X, double Y, double FontSize) Label(FloorElement element)
    {
        var b = FloorElementGeometry.GetBounds(element);
        var (x, y) = (element.X + element.LabelOffsetX, element.Y + element.LabelOffsetY);
        double size;
        if (element.Kind == FloorElementKind.Text)
            size = Math.Min(b.Height * 0.7, 3);
        else if (!HasIcon(element.Kind) || b.Height < 1)
            size = Math.Min(0.45, Math.Min(b.Height, b.Width) * 0.45);
        else
        {
            size = Math.Min(Math.Min(0.45, Math.Min(b.Height, b.Width) * 0.45), b.Height * 0.18);
            y += b.Height * 0.3;
        }
        return (x, y, element.LabelSize is > 0 and var own ? own : size);
    }

    /// <summary>
    /// The outline drawn around a label's letters (SVG text attributes), so it stays readable over the element's drawing:
    /// white, or dark on a pillar.
    /// </summary>
    public static string LabelHalo(FloorElement element, double fontSize) =>
        $"stroke=\"{HaloColour(element)}\" stroke-width=\"{N(HaloWidth(fontSize))}\" stroke-linejoin=\"round\" paint-order=\"stroke\"";

    /// <summary>The colour of the outline around a label's letters (see <see cref="LabelHalo"/>).</summary>
    public static string HaloColour(FloorElement element) => element.Kind == FloorElementKind.Pillar ? "#5d6673" : "#ffffff";

    /// <summary>The width of the outline around a label's letters (see <see cref="LabelHalo"/>).</summary>
    public static double HaloWidth(double fontSize) => Math.Round(fontSize * 0.22, 3);

    /// <summary>The element's drawing, without its box and label. Empty for kinds that are just a box.</summary>
    public static string Svg(FloorElement element)
    {
        var svg = new StringBuilder();
        var (w, d) = (element.Width, element.Depth);
        var (left, top) = (element.X - w / 2, element.Y - d / 2);
        var turned = element.Angle;
        var b = FloorElementGeometry.GetBounds(element);

        void Open() => svg.Append(turned == 0
            ? "<g class=\"symbol\" pointer-events=\"none\">"
            : $"<g class=\"symbol\" pointer-events=\"none\" transform=\"rotate({N(turned)} {N(element.X)} {N(element.Y)})\">");
        void Upright() => svg.Append("<g class=\"symbol\" pointer-events=\"none\">");

        switch (element.Kind)
        {
            case FloorElementKind.Stairs:
            {
                // Steps across the stairs, and an arrow up (towards the top side).
                Open();
                var steps = Math.Max(2, (int)(d / 0.3));
                for (var i = 1; i < steps; i++)
                    LineTo(svg, left, top + d * i / steps, left + w, top + d * i / steps, Line, Thin);
                var x = element.X;
                LineTo(svg, x, top + d * 0.85, x, top + d * 0.15, "#4b5a6e", 0.05);
                var head = Math.Min(w, d) * 0.12;
                svg.Append($"<path d=\"M{N(x - head)} {N(top + d * 0.15 + head)} L{N(x)} {N(top + d * 0.15)} L{N(x + head)} {N(top + d * 0.15 + head)}\" fill=\"none\" stroke=\"#4b5a6e\" stroke-width=\"0.05\"/>");
                break;
            }
            case FloorElementKind.Bar:
            {
                // The counter along the back, stools in front.
                Open();
                Box(svg, left, top, w, d * 0.45, "#d9c09c", Line);
                var r = Math.Min(0.16, d * 0.15);
                var count = Math.Max(1, (int)(w / 0.6));
                for (var i = 0; i < count; i++)
                    Circle(svg, left + w * (i + 0.5) / count, top + d * 0.75, r, "#ffffff", Line);
                break;
            }
            case FloorElementKind.Buffet:
            {
                // Dishes along the table.
                Open();
                var r = Math.Min(0.16, d * 0.3);
                var count = Math.Max(1, (int)(w / 0.55));
                for (var i = 0; i < count; i++)
                {
                    var cx = left + w * (i + 0.5) / count;
                    if (i % 2 == 0)
                        Circle(svg, cx, element.Y, r, "#ffffff", Line);
                    else
                        Box(svg, cx - r * 1.2, element.Y - r * 0.8, r * 2.4, r * 1.6, "#ffffff", Line);
                }
                break;
            }
            case FloorElementKind.Stage:
                // A thick front edge.
                Open();
                LineTo(svg, left, top + d, left + w, top + d, "#6a4f85", 0.12);
                break;
            case FloorElementKind.DanceFloor:
            {
                // A light chequered floor.
                Open();
                var size = Math.Max(0.5, Math.Max(w, d) / 12);
                for (var y = 0; y * size < d - 1e-9; y++)
                    for (var x = y % 2; x * size < w - 1e-9; x += 2)
                        svg.Append($"<rect x=\"{N(left + x * size)}\" y=\"{N(top + y * size)}\" width=\"{N(Math.Min(size, w - x * size))}\" height=\"{N(Math.Min(size, d - y * size))}\" fill=\"#8f72a6\" fill-opacity=\"0.15\"/>");
                break;
            }
            case FloorElementKind.Entrance:
            case FloorElementKind.Exit:
            {
                // A door in the back wall (top side), opening into the room: the leaf and its swing.
                Open();
                var leaf = Math.Min(w * 0.8, Math.Max(d * 2, 0.9));
                var hingeX = element.X - leaf / 2;
                LineTo(svg, hingeX, top, hingeX, top + Math.Min(leaf, d), "#3f7a48", 0.04);
                var reach = Math.Min(leaf, d);
                svg.Append($"<path d=\"M{N(hingeX)} {N(top + reach)} A{N(leaf)} {N(reach)} 0 0 0 {N(hingeX + leaf)} {N(top)}\" fill=\"none\" stroke=\"#3f7a48\" stroke-width=\"{N(Thin)}\" stroke-dasharray=\"0.1 0.06\"/>");
                if (element.Kind == FloorElementKind.Exit)
                {
                    // An arrow out through the door.
                    var x = element.X + leaf * 0.25;
                    LineTo(svg, x, top + d * 0.8, x, top + d * 0.2, "#2e9d47", 0.06);
                    var head = Math.Min(0.15, d * 0.2);
                    svg.Append($"<path d=\"M{N(x - head)} {N(top + d * 0.2 + head)} L{N(x)} {N(top + d * 0.2)} L{N(x + head)} {N(top + d * 0.2 + head)}\" fill=\"none\" stroke=\"#2e9d47\" stroke-width=\"0.06\"/>");
                }
                break;
            }
            case FloorElementKind.Pillar:
                // Filled; the box itself is drawn in the pillar colour, so only a round pillar needs a shape.
                if (element.IsRound)
                {
                    Open();
                    svg.Append($"<ellipse cx=\"{N(element.X)}\" cy=\"{N(element.Y)}\" rx=\"{N(w / 2)}\" ry=\"{N(d / 2)}\" fill=\"#8d96a3\" stroke=\"#5d6673\" stroke-width=\"{N(Thin)}\"/>");
                }
                else
                    return "";
                break;
            case FloorElementKind.Toilets:
            {
                // Two figures.
                Upright();
                var s = IconSize(b);
                var cy = IconCentre(element, b);
                Figure(svg, element.X - s * 0.28, cy, s);
                Figure(svg, element.X + s * 0.28, cy, s, dress: true);
                break;
            }
            case FloorElementKind.Cloakroom:
            {
                // A rail with hangers.
                Upright();
                var s = IconSize(b);
                var cy = IconCentre(element, b) - s * 0.3;
                LineTo(svg, element.X - s, cy, element.X + s, cy, "#4b5a6e", 0.05);
                for (var i = -2; i <= 2; i++)
                {
                    var x = element.X + i * s * 0.4;
                    svg.Append($"<path d=\"M{N(x)} {N(cy)} L{N(x - s * 0.17)} {N(cy + s * 0.3)} L{N(x + s * 0.17)} {N(cy + s * 0.3)} Z\" fill=\"none\" stroke=\"#4b5a6e\" stroke-width=\"{N(Thin)}\"/>");
                }
                break;
            }
            case FloorElementKind.DjBooth:
            {
                // Two turntables and a mixer on the desk.
                Upright();
                var s = IconSize(b);
                var cy = IconCentre(element, b);
                Box(svg, element.X - s * 1.1, cy - s * 0.45, s * 2.2, s * 0.9, "#ffffff", Line);
                Circle(svg, element.X - s * 0.62, cy, s * 0.33, "#d6cde0", Line);
                Circle(svg, element.X + s * 0.62, cy, s * 0.33, "#d6cde0", Line);
                Box(svg, element.X - s * 0.14, cy - s * 0.3, s * 0.28, s * 0.6, "#8f72a6", Line);
                break;
            }
            case FloorElementKind.PhotoBooth:
            {
                // A camera.
                Upright();
                var s = IconSize(b);
                var cy = IconCentre(element, b);
                Box(svg, element.X - s * 0.25, cy - s * 0.55, s * 0.5, s * 0.2, "#5d6673", "#5d6673");
                Box(svg, element.X - s * 0.7, cy - s * 0.38, s * 1.4, s * 0.85, "#5d6673", "#5d6673");
                Circle(svg, element.X, cy + s * 0.05, s * 0.28, "#ffffff", "#5d6673");
                break;
            }
            default:
                return "";
        }
        svg.Append("</g>");
        return svg.ToString();
    }

    /// <summary>The size of an upright icon: fits above the label.</summary>
    private static double IconSize(Rect b) => Math.Min(b.Width * 0.3, b.Height * (b.Height < 1 ? 0.35 : 0.28));

    private static double IconCentre(FloorElement element, Rect b) => b.Height < 1 ? element.Y : element.Y - b.Height * 0.12;

    /// <summary>A simple person: a head and a body (a skirt shape when <paramref name="dress"/>).</summary>
    private static void Figure(StringBuilder svg, double x, double cy, double s, bool dress = false)
    {
        Circle(svg, x, cy - s * 0.45, s * 0.13, "#4b5a6e", "#4b5a6e");
        var body = dress
            ? $"M{N(x)} {N(cy - s * 0.28)} L{N(x - s * 0.2)} {N(cy + s * 0.35)} L{N(x + s * 0.2)} {N(cy + s * 0.35)} Z"
            : $"M{N(x - s * 0.13)} {N(cy - s * 0.28)} L{N(x + s * 0.13)} {N(cy - s * 0.28)} L{N(x + s * 0.13)} {N(cy + s * 0.35)} L{N(x - s * 0.13)} {N(cy + s * 0.35)} Z";
        svg.Append($"<path d=\"{body}\" fill=\"#4b5a6e\"/>");
    }

    private static void LineTo(StringBuilder svg, double x1, double y1, double x2, double y2, string colour, double width) =>
        svg.Append($"<line x1=\"{N(x1)}\" y1=\"{N(y1)}\" x2=\"{N(x2)}\" y2=\"{N(y2)}\" stroke=\"{colour}\" stroke-width=\"{N(width)}\"/>");

    private static void Circle(StringBuilder svg, double x, double y, double r, string fill, string stroke) =>
        svg.Append($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"{N(r)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{N(Thin)}\"/>");

    private static void Box(StringBuilder svg, double x, double y, double w, double h, string fill, string stroke) =>
        svg.Append($"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"0.04\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{N(Thin)}\"/>");

    private static string N(double value) => Math.Round(value, 3).ToString(CultureInfo.InvariantCulture);
}
