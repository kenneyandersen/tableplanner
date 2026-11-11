using System.Xml.Linq;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Core.Tests;

public class ElementDrawingTests
{
    public static IEnumerable<object[]> Kinds() => Enum.GetValues<FloorElementKind>().Select(k => new object[] { k });

    private static FloorElement Element(FloorElementKind kind, Rotation rotation = Rotation.R0, bool round = false)
    {
        var (w, d) = FloorElementGeometry.DefaultSize(kind);
        return new FloorElement { Kind = kind, Label = kind.ToString(), X = 10, Y = 5, Width = w, Depth = d, Rotation = rotation, IsRound = round };
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_draws_valid_svg_inside_its_box_and_keeps_its_label_inside(FloorElementKind kind)
    {
        var element = Element(kind, round: kind == FloorElementKind.Pillar);
        var svg = ElementDrawing.Svg(element);

        if (kind is FloorElementKind.Custom or FloorElementKind.Text)
            Assert.Equal("", svg);
        else
        {
            var root = XElement.Parse($"<svg>{svg}</svg>");
            Assert.NotEmpty(root.Descendants());
        }
        var b = FloorElementGeometry.GetBounds(element);
        var (x, y, size) = ElementDrawing.Label(element);
        Assert.InRange(x, b.Left, b.Right);
        Assert.InRange(y, b.Top, b.Bottom);
        Assert.InRange(size, 0.01, kind == FloorElementKind.Text ? 3 : 0.45);
        Assert.StartsWith("#", ElementDrawing.Fill(kind));
    }

    [Fact]
    public void Elements_with_a_direction_turn_with_their_rotation_and_icons_stay_upright()
    {
        Assert.Contains("rotate(90 10 5)", ElementDrawing.Svg(Element(FloorElementKind.Stairs, Rotation.R90)));
        Assert.Contains("rotate(180 10 5)", ElementDrawing.Svg(Element(FloorElementKind.Bar, Rotation.R180)));
        Assert.DoesNotContain("rotate", ElementDrawing.Svg(Element(FloorElementKind.Toilets, Rotation.R90)));
    }

    [Fact]
    public void A_square_pillar_is_just_its_filled_box_and_a_round_one_an_ellipse()
    {
        Assert.Equal("", ElementDrawing.Svg(Element(FloorElementKind.Pillar)));
        Assert.Contains("<ellipse", ElementDrawing.Svg(Element(FloorElementKind.Pillar, round: true)));
    }

    [Fact]
    public void Stairs_get_a_step_for_every_30_cm()
    {
        var stairs = Element(FloorElementKind.Stairs); // 2 × 3 m

        var lines = XElement.Parse($"<svg>{ElementDrawing.Svg(stairs)}</svg>").Descendants("line").Count();

        Assert.Equal(9 + 1, lines); // 10 steps make 9 lines between them, and the arrow
    }
}

public class ElementGallery
{
    /// <summary>Writes every kind of element, upright and turned, to an HTML page for looking at: set TABLEPLANNER_GALLERY to the file path.</summary>
    [Fact]
    public void Write_a_gallery_when_asked()
    {
        if (Environment.GetEnvironmentVariable("TABLEPLANNER_GALLERY") is not { Length: > 0 } path)
            return;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        var svg = new System.Text.StringBuilder();
        var kinds = Enum.GetValues<FloorElementKind>();
        for (var i = 0; i < kinds.Length; i++)
            foreach (var (rotation, column) in new[] { (Rotation.R0, 0), (Rotation.R90, 1) })
            {
                var (w, d) = FloorElementGeometry.DefaultSize(kinds[i]);
                var element = new FloorElement
                {
                    Kind = kinds[i], Label = FloorElementGeometry.DisplayName(kinds[i]), Width = Math.Max(w, 2), Depth = Math.Max(d, 1.2),
                    X = 4 + column * 8, Y = 3 + i * 6.5, Rotation = rotation, IsRound = false,
                };
                var b = FloorElementGeometry.GetBounds(element);
                var label = ElementDrawing.Label(element);
                svg.Append($"<rect x=\"{b.Left}\" y=\"{b.Top}\" width=\"{b.Width}\" height=\"{b.Height}\" fill=\"{ElementDrawing.Fill(element.Kind)}\" stroke=\"#8a96a8\" stroke-width=\"0.03\"/>");
                svg.Append(ElementDrawing.Svg(element));
                svg.Append($"<text x=\"{label.X}\" y=\"{label.Y}\" font-size=\"{label.FontSize}\" text-anchor=\"middle\" dominant-baseline=\"middle\" fill=\"#33435a\">{element.Label}</text>");
            }
        File.WriteAllText(path, $"<!doctype html><body style=\"margin:0\"><svg viewBox=\"-1 -1 18 {kinds.Length * 6.5 + 2}\" width=\"720\" style=\"font-family:sans-serif\">{svg}</svg></body>");
    }
}

public class ElementLabelTests
{
    [Fact]
    public void A_label_can_have_its_own_size_and_be_moved()
    {
        var element = new FloorElement { Kind = FloorElementKind.Buffet, X = 10, Y = 5, Width = 4, Depth = 1 };
        var (x, y, size) = ElementDrawing.Label(element);
        Assert.Equal((10.0, 5.0), (x, y));
        Assert.InRange(size, 0.1, 0.45);

        element.LabelSize = 1.2;
        element.LabelOffsetX = 0.5;
        element.LabelOffsetY = 1.5;

        Assert.Equal((10.5, 6.5, 1.2), ElementDrawing.Label(element));
    }

    [Fact]
    public void A_text_element_has_no_box_and_a_label_that_fills_it()
    {
        var text = new FloorElement { Kind = FloorElementKind.Text, Label = "🎉 Party", X = 0, Y = 0, Width = 3, Depth = 1 };

        Assert.Equal("none", ElementDrawing.Fill(FloorElementKind.Text));
        Assert.Equal("", ElementDrawing.Svg(text));
        Assert.Equal(0.7, ElementDrawing.Label(text).FontSize, 6);
    }

    [Fact]
    public void Labels_get_an_outline_to_stay_readable_over_drawings()
    {
        var buffet = new FloorElement { Kind = FloorElementKind.Buffet };

        Assert.Contains("paint-order=\"stroke\"", ElementDrawing.LabelHalo(buffet, 0.4));
        Assert.Contains("stroke=\"#ffffff\"", ElementDrawing.LabelHalo(buffet, 0.4));
    }
}
