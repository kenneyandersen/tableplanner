using System.Text;
using QRCoder;

namespace TablePlanner.Export.Printing;

/// <summary>
/// QR codes for the guest page's web address, so guests can scan it from a poster or card. The code itself is made
/// by QRCoder; the SVG is drawn here, so it scales to any size and can be put straight into a template.
/// </summary>
public static class QrCode
{
    /// <summary>Medium error correction: still readable with some damage, without making the code much denser.</summary>
    private const QRCodeGenerator.ECCLevel Correction = QRCodeGenerator.ECCLevel.M;

    /// <summary>True for a web address a phone can open: http:// or https:// and a host.</summary>
    public static bool IsWebAddress(string? text) =>
        Uri.TryCreate(text?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0;

    /// <summary>The code as an SVG with a white quiet zone around it; it has no fixed size, so CSS decides how large it is.</summary>
    public static string Svg(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, Correction);
        var rows = data.ModuleMatrix;
        var size = rows.Count;
        var path = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            // One rectangle per run of dark modules in a row keeps the path short.
            for (var x = 0; x < size; x++)
            {
                if (!rows[y][x])
                    continue;
                var start = x;
                while (x + 1 < size && rows[y][x + 1])
                    x++;
                path.Append($"M{start} {y}h{x - start + 1}v1h-{x - start + 1}z");
            }
        }
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"qr-code\" viewBox=\"0 0 {size} {size}\" shape-rendering=\"crispEdges\">"
            + $"<rect width=\"{size}\" height=\"{size}\" fill=\"#ffffff\"/><path d=\"{path}\" fill=\"#000000\"/></svg>";
    }

    /// <summary>The code as a PNG image, for documents and messages.</summary>
    public static byte[] Png(string text, int pixelsPerModule = 20)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, Correction);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }

    /// <summary>The placeholders every template gets for the guest page's address and its QR code.</summary>
    internal static void AddTo(TemplateData data, string? guestPageUrl)
    {
        var url = IsWebAddress(guestPageUrl) ? guestPageUrl!.Trim() : "";
        data["GuestPageUrl"] = url;
        data["GuestPageQr"] = new RawHtml(url.Length > 0 ? Svg(url) : "");
    }

    internal static readonly IReadOnlyList<Placeholder> Placeholders =
    [
        new("GuestPageUrl", "The guest page's web address, if set on the Guest page tab (otherwise empty)."),
        new("GuestPageQr", "A QR code (SVG) for that address, or nothing. Size it with CSS, e.g. .qr-code { width: 30mm; }."),
    ];
}
