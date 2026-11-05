using System.Globalization;
using System.Text;
using TablePlanner.Core.Geometry;
using TablePlanner.Core.Model;

namespace TablePlanner.Export.Printing;

/// <summary>
/// Draws one floor of a seating plan as an SVG for the poster: floor elements, tables, seats in their group's
/// colour, table numbers and group names. Coordinates are metres; the SVG has no fixed size, so the template's
/// CSS decides how large it is. Colours are presentation attributes, so template CSS can override them by class
/// (<c>element</c>, <c>table</c>, <c>seat</c>, <c>table-number</c>, <c>group-label</c>).
/// </summary>
public static class FloorPlanSvg
{
    /// <summary>Space around the drawn tables and elements, in metres.</summary>
    private const double Margin = 1;

    public static string Render(Floor floor, PrintedPlan plan, IReadOnlyDictionary<Guid, string> colours)
    {
        var bounds = floor.Tables.Select(TableGeometry.GetBounds).Concat(floor.Elements.Select(FloorElementGeometry.GetBounds)).ToList();
        // Seats stick out from the table top by about a chair's depth.
        var reach = TableGeometry.ChairOffset + 0.25;
        var left = bounds.Count == 0 ? 0 : bounds.Min(b => b.Left) - reach - Margin;
        var top = bounds.Count == 0 ? 0 : bounds.Min(b => b.Top) - reach - Margin;
        var width = bounds.Count == 0 ? 10 : bounds.Max(b => b.Right) + reach + Margin - left;
        var height = bounds.Count == 0 ? 10 : bounds.Max(b => b.Bottom) + reach + Margin - top;

        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"floor-plan\" viewBox=\"{N(left)} {N(top)} {N(width)} {N(height)}\" preserveAspectRatio=\"xMidYMid meet\" font-family=\"inherit\" text-anchor=\"middle\" dominant-baseline=\"central\">");

        foreach (var element in floor.Elements)
        {
            var b = FloorElementGeometry.GetBounds(element);
            var fontSize = Math.Min(0.45, Math.Min(b.Height, b.Width) * 0.45);
            svg.Append($"<g class=\"element kind-{element.Kind.ToString().ToLowerInvariant()}\">");
            svg.Append($"<rect x=\"{N(b.Left)}\" y=\"{N(b.Top)}\" width=\"{N(b.Width)}\" height=\"{N(b.Height)}\" rx=\"0.05\" fill=\"#eef1f5\" stroke=\"#8a96a8\" stroke-width=\"0.03\"/>");
            svg.Append($"<text x=\"{N(element.X)}\" y=\"{N(element.Y)}\" font-size=\"{N(fontSize)}\" fill=\"#44536a\">{HtmlTemplate.Escape(element.Label)}</text>");
            svg.Append("</g>");
        }

        foreach (var table in floor.Tables)
        {
            var b = TableGeometry.GetBounds(table);
            var seats = TableGeometry.GetSeats(table);
            svg.Append("<g class=\"table\">");
            svg.Append($"<rect x=\"{N(b.Left)}\" y=\"{N(b.Top)}\" width=\"{N(b.Width)}\" height=\"{N(b.Height)}\" rx=\"0.05\" fill=\"#f6eedf\" stroke=\"#8d6e45\" stroke-width=\"0.03\"/>");
            foreach (var seat in seats)
            {
                var fill = plan.GroupAt.TryGetValue(seat.Id, out var groupId) ? colours.GetValueOrDefault(groupId, "#cccccc")
                    : seat.IsDisabled ? "#d0d0d0" : "#ffffff";
                svg.Append($"<circle class=\"seat\" cx=\"{N(seat.Position.X)}\" cy=\"{N(seat.Position.Y)}\" r=\"0.22\" fill=\"{HtmlTemplate.Escape(fill)}\" stroke=\"#6b5638\" stroke-width=\"0.03\"/>");
            }
            var number = GroupLabels.TableNumberPosition(table);
            svg.Append($"<text class=\"table-number\" x=\"{N(number.X)}\" y=\"{N(number.Y)}\" font-size=\"{N(GroupLabels.TableNumberFontSize)}\" font-weight=\"700\" fill=\"#6d5534\">{table.Number}</text>");
            foreach (var label in GroupLabels.For(table, seats, plan.GroupAt, plan.Setup))
            {
                var turn = label.Vertical ? $" transform=\"rotate(90 {N(label.Position.X)} {N(label.Position.Y)})\"" : "";
                svg.Append($"<text class=\"group-label\" x=\"{N(label.Position.X)}\" y=\"{N(label.Position.Y)}\" font-size=\"{N(label.FontSize)}\" font-weight=\"600\" fill=\"#222222\"{turn}>{HtmlTemplate.Escape(label.Text)}</text>");
            }
            svg.Append("</g>");
        }

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
