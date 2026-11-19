using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace AviSite.Web.Pdf;

/// <summary>An sRGB color, 0–255 per channel.</summary>
public readonly record struct PdfColor(byte R, byte G, byte B)
{
    public static readonly PdfColor Black = new(16, 16, 17);
    public static readonly PdfColor White = new(255, 255, 255);

    /// <summary>"#0c63e6" → color. Null for anything else.</summary>
    public static PdfColor? FromHex(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#') return null;
        return int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? new PdfColor((byte)(v >> 16), (byte)(v >> 8), (byte)v) : null;
    }

    /// <summary>Mix with white: 0 = this color, 1 = white. For light tints behind text.</summary>
    public PdfColor Tint(double amount) => new(Mix(R, amount), Mix(G, amount), Mix(B, amount));
    private static byte Mix(byte c, double a) => (byte)Math.Round(c + (255 - c) * Math.Clamp(a, 0, 1));

    internal string Fill => $"{N(R / 255.0)} {N(G / 255.0)} {N(B / 255.0)} rg";
    internal string Stroke => $"{N(R / 255.0)} {N(G / 255.0)} {N(B / 255.0)} RG";
    internal static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
}

public enum PdfFont { Regular, Bold }

/// <summary>
/// A small, dependency-free PDF 1.4 writer: text in the standard Helvetica faces (not embedded; every viewer has them),
/// filled rectangles and rounded bars, lines, and PNG/JPEG images. Coordinates are points from the TOP-left of the page.
/// Enough for the hours reports; not a general layout engine.
/// </summary>
public sealed class PdfDocument
{
    public const double LetterWidth = 612, LetterHeight = 792;
    private readonly List<PdfPage> _pages = [];
    private readonly List<PdfImage> _images = [];
    public string Title { get; set; } = "";
    public string Author { get; set; } = "";

    public IReadOnlyList<PdfPage> Pages => _pages;

    public PdfPage AddPage(double width = LetterWidth, double height = LetterHeight)
    {
        var p = new PdfPage(this, width, height);
        _pages.Add(p);
        return p;
    }

    internal int Register(PdfImage image)
    {
        var i = _images.IndexOf(image);
        if (i >= 0) return i;
        _images.Add(image);
        return _images.Count - 1;
    }

    public byte[] Save()
    {
        var objects = new List<byte[]>(); // object n is objects[n - 1]
        int Reserve() { objects.Add([]); return objects.Count; }
        void Set(int n, string s) => objects[n - 1] = Latin1(s);
        void SetStream(int n, string dict, byte[] data) =>
            objects[n - 1] = [.. Latin1($"{dict.TrimEnd('>', ' ')} /Length {data.Length} >>\nstream\n"), .. data, .. Latin1("\nendstream")];

        var catalog = Reserve();
        var pagesRoot = Reserve();
        var fontRegular = Reserve();
        var fontBold = Reserve();
        var info = Reserve();
        Set(fontRegular, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Set(fontBold, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        Set(info, $"<< /Title {PdfText.Literal(Title)} /Author {PdfText.Literal(Author)} /Producer (AVI Robotics site) " +
            $"/CreationDate (D:{DateTime.UtcNow:yyyyMMddHHmmss}Z) >>");

        var imageRefs = new List<int>();
        foreach (var img in _images)
        {
            int? smask = null;
            if (img.Alpha is { } alpha)
            {
                smask = Reserve();
                SetStream(smask.Value, $"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode >>", alpha);
            }
            var n = Reserve();
            var cs = img.Channels == 1 ? "/DeviceGray" : "/DeviceRGB";
            var filter = img.IsJpeg ? "/DCTDecode" : "/FlateDecode";
            SetStream(n, $"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace {cs} /BitsPerComponent 8 /Filter {filter}" +
                (smask is { } s ? $" /SMask {s} 0 R" : "") + " >>", img.Data);
            imageRefs.Add(n);
        }
        var xobjects = imageRefs.Count == 0 ? "" : " /XObject << " + string.Join(' ', imageRefs.Select((r, i) => $"/Im{i} {r} 0 R")) + " >>";
        var resources = $"<< /Font << /F1 {fontRegular} 0 R /F2 {fontBold} 0 R >>{xobjects} >>";

        var kids = new List<int>();
        foreach (var page in _pages)
        {
            var content = Reserve();
            SetStream(content, "<< /Filter /FlateDecode >>", Deflate(Latin1(page.Content)));
            var n = Reserve();
            Set(n, $"<< /Type /Page /Parent {pagesRoot} 0 R /MediaBox [0 0 {PdfColor.N(page.Width)} {PdfColor.N(page.Height)}] /Resources {resources} /Contents {content} 0 R >>");
            kids.Add(n);
        }
        Set(pagesRoot, $"<< /Type /Pages /Kids [{string.Join(' ', kids.Select(k => $"{k} 0 R"))}] /Count {kids.Count} >>");
        Set(catalog, $"<< /Type /Catalog /Pages {pagesRoot} 0 R >>");

        using var ms = new MemoryStream();
        void W(string s) { var b = Latin1(s); ms.Write(b); }
        W("%PDF-1.4\n%âãÏÓ\n");
        var offsets = new long[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = ms.Position;
            W($"{i + 1} 0 obj\n");
            ms.Write(objects[i]);
            W("\nendobj\n");
        }
        var xref = ms.Position;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    internal static byte[] Latin1(string s) => Encoding.Latin1.GetBytes(s);

    internal static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(data);
        return ms.ToArray();
    }
}

public sealed class PdfPage
{
    private readonly PdfDocument _doc;
    private readonly StringBuilder _c = new();
    public double Width { get; }
    public double Height { get; }
    internal string Content => _c.ToString();

    internal PdfPage(PdfDocument doc, double width, double height) { _doc = doc; Width = width; Height = height; }

    private static string N(double v) => PdfColor.N(v);
    private double Y(double top) => Height - top;

    public void Rect(double x, double y, double w, double h, PdfColor fill) =>
        _c.Append($"q {fill.Fill} {N(x)} {N(Y(y + h))} {N(w)} {N(h)} re f Q\n");

    public void StrokeRect(double x, double y, double w, double h, PdfColor color, double lineWidth = 0.75) =>
        _c.Append($"q {color.Stroke} {N(lineWidth)} w {N(x)} {N(Y(y + h))} {N(w)} {N(h)} re S Q\n");

    public void Line(double x1, double y1, double x2, double y2, PdfColor color, double lineWidth = 0.5) =>
        _c.Append($"q {color.Stroke} {N(lineWidth)} w {N(x1)} {N(Y(y1))} m {N(x2)} {N(Y(y2))} l S Q\n");

    /// <summary>A rectangle with all four corners rounded by <paramref name="r"/> (clamped to fit).</summary>
    public void RoundRect(double x, double y, double w, double h, double r, PdfColor fill)
    {
        r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
        if (r < 0.01) { Rect(x, y, w, h, fill); return; }
        const double k = 0.5523; // Bézier circle constant
        double x0 = x, x1 = x + w, yb = Y(y + h), yt = Y(y);
        _c.Append($"q {fill.Fill} {N(x0 + r)} {N(yb)} m {N(x1 - r)} {N(yb)} l ");
        _c.Append($"{N(x1 - r + r * k)} {N(yb)} {N(x1)} {N(yb + r - r * k)} {N(x1)} {N(yb + r)} c ");
        _c.Append($"{N(x1)} {N(yt - r)} l {N(x1)} {N(yt - r + r * k)} {N(x1 - r + r * k)} {N(yt)} {N(x1 - r)} {N(yt)} c ");
        _c.Append($"{N(x0 + r)} {N(yt)} l {N(x0 + r - r * k)} {N(yt)} {N(x0)} {N(yt - r + r * k)} {N(x0)} {N(yt - r)} c ");
        _c.Append($"{N(x0)} {N(yb + r)} l {N(x0)} {N(yb + r - r * k)} {N(x0 + r - r * k)} {N(yb)} {N(x0 + r)} {N(yb)} c f Q\n");
    }

    /// <summary>Text with its baseline at <paramref name="baseline"/> (points from the top).</summary>
    public void Text(double x, double baseline, string text, PdfFont font, double size, PdfColor color)
    {
        if (string.IsNullOrEmpty(text)) return;
        _c.Append($"BT {color.Fill} /{(font == PdfFont.Bold ? "F2" : "F1")} {N(size)} Tf {N(x)} {N(Y(baseline))} Td {PdfText.Literal(text)} Tj ET\n");
    }

    public void TextRight(double right, double baseline, string text, PdfFont font, double size, PdfColor color) =>
        Text(right - PdfText.Width(text, font, size), baseline, text, font, size, color);

    public void TextCenter(double center, double baseline, string text, PdfFont font, double size, PdfColor color) =>
        Text(center - PdfText.Width(text, font, size) / 2, baseline, text, font, size, color);

    /// <summary>Wrapped text starting at <paramref name="top"/>; returns the y just below the last line.</summary>
    public double Paragraph(double x, double top, double width, string text, PdfFont font, double size, PdfColor color, double leading = 1.35, int maxLines = int.MaxValue)
    {
        var y = top;
        foreach (var line in PdfText.Wrap(text, font, size, width).Take(maxLines))
        {
            y += size * leading;
            Text(x, y - size * (leading - 1) / 2 - size * 0.22, line, font, size, color);
        }
        return y;
    }

    public void Image(PdfImage image, double x, double y, double w, double h)
    {
        var i = _doc.Register(image);
        _c.Append($"q {N(w)} 0 0 {N(h)} {N(x)} {N(Y(y + h))} cm /Im{i} Do Q\n");
    }

    /// <summary>The image scaled to fit inside the box, aligned left and centered vertically. Returns the width used.</summary>
    public double ImageFit(PdfImage image, double x, double y, double maxW, double maxH)
    {
        var scale = Math.Min(maxW / image.Width, maxH / image.Height);
        var w = image.Width * scale; var h = image.Height * scale;
        Image(image, x, y + (maxH - h) / 2, w, h);
        return w;
    }
}

/// <summary>WinAnsi text: encoding, string literals, and Helvetica widths (from the Adobe AFM files) for layout.</summary>
public static class PdfText
{
    // Widths in 1/1000 em for characters 32..126.
    private static readonly short[] Regular =
    [
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
    ];
    private static readonly short[] Bold =
    [
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584,
    ];

    /// <summary>Unicode → WinAnsi (cp1252) for the punctuation the site uses. Anything else unmappable becomes '?'.</summary>
    private static readonly Dictionary<char, byte> Specials = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['„'] = 0x84, ['…'] = 0x85, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97,
        ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['™'] = 0x99, ['›'] = 0x9B, ['‹'] = 0x8B,
        ['→'] = (byte)'>', ['←'] = (byte)'<', [' '] = 0x20, [' '] = 0x20, [' '] = 0x20,
    };

    public static byte Encode(char c) =>
        c is >= ' ' and <= '~' ? (byte)c :
        Specials.TryGetValue(c, out var b) ? b :
        c is >= '¡' and <= 'ÿ' ? (byte)c : (byte)'?';

    private static double CharWidth(byte b, PdfFont font)
    {
        if (b is >= 32 and <= 126) return (font == PdfFont.Bold ? Bold : Regular)[b - 32];
        return b switch
        {
            0x95 => 350, 0x96 => 556, 0x97 => 1000, 0x85 => 1000, 0x91 or 0x92 => font == PdfFont.Bold ? 278 : 222,
            0x93 or 0x94 => font == PdfFont.Bold ? 500 : 333, 0xB7 => 278, 0x9B or 0x8B => 333, 0xD7 => 584, 0xB0 => 400,
            _ => 556,
        };
    }

    public static double Width(string text, PdfFont font, double size)
    {
        double w = 0;
        foreach (var c in text) w += CharWidth(Encode(c), font);
        return w * size / 1000;
    }

    /// <summary>A PDF string literal: (…) with \, ( and ) escaped; bytes ≥ 128 as octal.</summary>
    public static string Literal(string text)
    {
        var sb = new StringBuilder("(");
        foreach (var c in text)
        {
            var b = Encode(c);
            if (b is (byte)'(' or (byte)')' or (byte)'\\') sb.Append('\\').Append((char)b);
            else if (b >= 128) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else sb.Append((char)b);
        }
        return sb.Append(')').ToString();
    }

    /// <summary>Greedy word wrap; words longer than a line are cut.</summary>
    public static List<string> Wrap(string text, PdfFont font, double size, double width)
    {
        var lines = new List<string>();
        foreach (var para in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Width(candidate, font, size) <= width) { line = candidate; continue; }
                if (line.Length > 0) lines.Add(line);
                line = word;
                while (Width(line, font, size) > width && line.Length > 1)
                {
                    var cut = line.Length - 1;
                    while (cut > 1 && Width(line[..cut], font, size) > width) cut--;
                    lines.Add(line[..cut]);
                    line = line[cut..];
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Shortens <paramref name="text"/> with "…" to fit <paramref name="width"/>.</summary>
    public static string Fit(string text, PdfFont font, double size, double width)
    {
        if (Width(text, font, size) <= width) return text;
        var n = text.Length;
        while (n > 0 && Width(text[..n] + "…", font, size) > width) n--;
        return text[..n].TrimEnd() + "…";
    }
}
