using System.Globalization;
using PdfSharp.Drawing;

namespace Ptw.Infrastructure.Printing;

internal enum TextAlign
{
    Left,
    Center,
    Right
}

/// <summary>
/// Millimetre-based drawing surface for the controlled PTW form. The form is a fixed grid rather than a
/// flowing document, so every primitive takes absolute millimetre coordinates measured from the top-left
/// of the page.
/// </summary>
internal sealed class FormCanvas : IDisposable
{
    private const double MmToPt = 72.0 / 25.4;
    private const double HairlineMm = 0.2;

    private readonly XGraphics _graphics;
    private readonly XPen _hairline;
    private readonly XPen _rule;
    private readonly XPen _checkMark;
    private readonly Dictionary<(double Size, bool Bold), XFont> _fonts = [];

    internal FormCanvas(XGraphics graphics, XColor accent)
    {
        _graphics = graphics;
        Accent = accent;
        _hairline = new XPen(XColors.Black, HairlineMm * MmToPt);
        _rule = new XPen(XColors.Black, 0.5 * MmToPt);
        _checkMark = new XPen(XColors.Black, 0.45);
    }

    internal XColor Accent { get; }

    internal static double Mm(double millimetres) => millimetres * MmToPt;

    internal void Box(double x, double y, double width, double height, bool heavy = false) =>
        _graphics.DrawRectangle(heavy ? _rule : _hairline, Mm(x), Mm(y), Mm(width), Mm(height));

    internal void Fill(double x, double y, double width, double height, XColor color) =>
        _graphics.DrawRectangle(new XSolidBrush(color), Mm(x), Mm(y), Mm(width), Mm(height));

    internal void Line(double x1, double y1, double x2, double y2, bool heavy = false) =>
        _graphics.DrawLine(heavy ? _rule : _hairline, Mm(x1), Mm(y1), Mm(x2), Mm(y2));

    /// <summary>Draws a dotted fill-in rule, used wherever the original form expects handwriting.</summary>
    internal void WritingRule(double x, double y, double width)
    {
        var pen = new XPen(XColors.Black, HairlineMm * MmToPt)
        {
            DashStyle = XDashStyle.Dot
        };
        _graphics.DrawLine(pen, Mm(x), Mm(y), Mm(x + width), Mm(y));
    }

    internal void Text(
        double x,
        double y,
        double width,
        double height,
        string text,
        double size = 5.2,
        bool bold = false,
        TextAlign align = TextAlign.Left,
        XColor? color = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var font = Font(size, bold);
        var rendered = Fit(text, font, width);
        var format = align switch
        {
            TextAlign.Center => XStringFormats.CenterLeft,
            TextAlign.Right => XStringFormats.CenterRight,
            _ => XStringFormats.CenterLeft
        };
        var rect = new XRect(Mm(x), Mm(y), Mm(width), Mm(height));
        if (align == TextAlign.Center)
        {
            format = XStringFormats.Center;
        }

        _graphics.DrawString(
            rendered,
            font,
            new XSolidBrush(color ?? XColors.Black),
            rect,
            format);
    }

    /// <summary>Draws wrapped text inside a box, returning the height in millimetres actually used.</summary>
    internal double Paragraph(
        double x,
        double y,
        double width,
        string text,
        double size = 5.2,
        double lineHeight = 3.4,
        int maxLines = 3)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var font = Font(size, false);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (_graphics.MeasureString(candidate, font).Width > Mm(width) && current.Length > 0)
            {
                lines.Add(current);
                current = word;
                if (lines.Count == maxLines)
                {
                    break;
                }
            }
            else
            {
                current = candidate;
            }
        }

        if (lines.Count < maxLines && current.Length > 0)
        {
            lines.Add(current);
        }

        for (var index = 0; index < lines.Count; index++)
        {
            Text(x, y + (index * lineHeight), width, lineHeight, lines[index], size);
        }

        return lines.Count * lineHeight;
    }

    /// <summary>Section header strip, filled with the permit class accent colour.</summary>
    internal void Band(double x, double y, double width, double height, string title, string? trailing = null)
    {
        Fill(x, y, width, height, Accent);
        Text(x + 1.2, y, width - 2.4, height, title, 5.4, bold: true, color: XColors.White);
        if (!string.IsNullOrEmpty(trailing))
        {
            Text(x + 1.2, y, width - 2.4, height, trailing, 5.0, bold: true, TextAlign.Right, XColors.White);
        }
    }

    /// <summary>An empty square of the same size the original form uses for tick boxes.</summary>
    internal void CheckBox(double x, double y, bool ticked = false, double size = 2.4)
    {
        _graphics.DrawRectangle(_hairline, Mm(x), Mm(y), Mm(size), Mm(size));
        if (!ticked)
        {
            return;
        }

        CheckMark(x, y, size);
    }

    /// <summary>Draws a tick inside an existing controlled-template checkbox.</summary>
    internal void CheckMark(double x, double y, double size = 2.4)
    {
        _graphics.DrawLine(
            _checkMark,
            Mm(x + (size * 0.15)),
            Mm(y + (size * 0.55)),
            Mm(x + (size * 0.42)),
            Mm(y + (size * 0.82)));
        _graphics.DrawLine(
            _checkMark,
            Mm(x + (size * 0.42)),
            Mm(y + (size * 0.82)),
            Mm(x + (size * 0.88)),
            Mm(y + (size * 0.18)));
    }

    /// <summary>A tick box followed by its label, the repeating unit of Bagian 1, 4, 5 and 7.</summary>
    internal void CheckItem(
        double x,
        double y,
        double width,
        double height,
        string label,
        bool ticked = false,
        double size = 4.8)
    {
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        var boxSize = 2.4;
        CheckBox(x, y + ((height - boxSize) / 2), ticked, boxSize);
        Text(x + boxSize + 1.0, y, width - boxSize - 1.0, height, label, size);
    }

    /// <summary>A caption with a dotted rule for the value, used across Bagian 2, 3, 6, 9 and 10.</summary>
    internal void Field(
        double x,
        double y,
        double width,
        double height,
        string caption,
        string? value = null,
        double captionWidth = 0,
        double size = 4.8)
    {
        var labelWidth = captionWidth > 0 ? captionWidth : width * 0.45;
        Text(x, y, labelWidth, height, caption, size);
        var valueX = x + labelWidth;
        var valueWidth = width - labelWidth;
        if (string.IsNullOrWhiteSpace(value))
        {
            WritingRule(valueX, y + height - 0.9, valueWidth);
        }
        else
        {
            Text(valueX, y, valueWidth, height, value, size, bold: true);
        }
    }

    internal void Watermark(double pageWidth, double pageHeight, string text)
    {
        var state = _graphics.Save();
        _graphics.TranslateTransform(Mm(pageWidth / 2), Mm(pageHeight / 2));
        _graphics.RotateTransform(-30);
        var font = Font(48, true);
        var brush = new XSolidBrush(XColor.FromArgb(38, 192, 0, 0));
        _graphics.DrawString(text, font, brush, new XPoint(0, 0), XStringFormats.Center);
        _graphics.Restore(state);
    }

    internal void DrawImage(XImage image, double x, double y, double width, double height) =>
        _graphics.DrawImage(image, Mm(x), Mm(y), Mm(width), Mm(height));

    internal double MeasureMm(string text, double size, bool bold) =>
        _graphics.MeasureString(text, Font(size, bold)).Width / MmToPt;

    private XFont Font(double size, bool bold)
    {
        var key = (size, bold);
        if (_fonts.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var font = new XFont(
            EmbeddedFormFontResolver.FamilyName,
            size,
            bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
        _fonts[key] = font;
        return font;
    }

    /// <summary>
    /// Shrinks then truncates a label so a long controlled item never bleeds into the neighbouring cell.
    /// </summary>
    private string Fit(string text, XFont font, double widthMm)
    {
        var limit = Mm(widthMm);
        if (_graphics.MeasureString(text, font).Width <= limit)
        {
            return text;
        }

        for (var length = text.Length - 1; length > 1; length--)
        {
            var candidate = string.Concat(text.AsSpan(0, length), "…");
            if (_graphics.MeasureString(candidate, font).Width <= limit)
            {
                return candidate;
            }
        }

        return text[..1];
    }

    internal static string Wib(DateTimeOffset value, string format) =>
        TimeZoneInfo
            .ConvertTime(value, PrintTimeZone.Jakarta)
            .ToString(format, CultureInfo.InvariantCulture);

    public void Dispose() => _graphics.Dispose();
}

internal static class PrintTimeZone
{
    /// <summary>
    /// Asia/Jakarta has no daylight saving, so a fixed +07:00 offset is exact and avoids depending on
    /// time zone database identifiers that differ between Windows and Linux hosts.
    /// </summary>
    internal static readonly TimeZoneInfo Jakarta =
        TimeZoneInfo.CreateCustomTimeZone("PTW-WIB", TimeSpan.FromHours(7), "WIB", "WIB");
}
