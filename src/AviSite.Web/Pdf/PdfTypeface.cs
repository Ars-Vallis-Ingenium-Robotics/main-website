using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace AviSite.Web.Pdf;

/// <summary>
/// A TrueType font for <see cref="PdfDocument"/>: the file is embedded whole, text is written as glyph IDs, and widths
/// come from the font's own metrics. Fonts ship as embedded resources (Pdf/Fonts, made by tools/fonts/make_pdf_fonts.py).
/// </summary>
public sealed class PdfTypeface
{
    private static readonly ConcurrentDictionary<string, PdfTypeface> Cache = new();

    public string PdfName { get; }
    public byte[] Data { get; }
    public int UnitsPerEm { get; private set; } = 1000;
    public short Ascender { get; private set; }
    public short Descender { get; private set; }
    public short CapHeight { get; private set; }
    public short[] BBox { get; private set; } = [0, 0, 0, 0];
    public double ItalicAngle { get; private set; }
    /// <summary>PDF font flags: 32 = nonsymbolic (Latin text).</summary>
    public int Flags => 32;

    private ushort[] _advances = [];
    private readonly Dictionary<int, ushort> _cmap = [];

    private PdfTypeface(string name, byte[] data)
    {
        PdfName = name;
        Data = data;
        Parse();
    }

    /// <summary>A font file from Pdf/Fonts (e.g. "archivo-regular.ttf"), loaded once.</summary>
    public static PdfTypeface Embedded(string file) => Cache.GetOrAdd(file, f =>
    {
        using var s = typeof(PdfTypeface).Assembly.GetManifestResourceStream("AviSite.Web.Pdf.Fonts." + f)
            ?? throw new FileNotFoundException("Missing embedded PDF font", f);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return new PdfTypeface("AVI-" + Path.GetFileNameWithoutExtension(f).Replace('-', '_'), ms.ToArray());
    });

    public static PdfTypeface FromBytes(string name, byte[] ttf) => new(name, ttf);

    /// <summary>Font units → PDF glyph space (1000 per em).</summary>
    internal int Scale(int units) => (int)Math.Round(units * 1000.0 / UnitsPerEm);

    internal int Advance(ushort glyph) => _advances.Length == 0 ? 0 : _advances[Math.Min(glyph, _advances.Length - 1)];

    /// <summary>Stand-ins for punctuation a font may not have (the Latin subsets don't always include these).</summary>
    private static readonly Dictionary<char, string> Fallbacks = new()
    {
        ['–'] = "-", ['—'] = "-", ['‘'] = "'", ['’'] = "'", ['“'] = "\"", ['”'] = "\"", ['…'] = "...", ['›'] = ">", ['‹'] = "<",
        ['•'] = "*", ['·'] = "-", ['→'] = "->", ['←'] = "<-", ['×'] = "x", [' '] = " ", [' '] = " ", [' '] = " ",
    };

    /// <summary>Glyph IDs for <paramref name="text"/>, substituting characters the font lacks.</summary>
    public List<ushort> Glyphs(string text)
    {
        var list = new List<ushort>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length) cp = char.ConvertToUtf32(text[i], text[++i]);
            if (_cmap.TryGetValue(cp, out var g)) { list.Add(g); continue; }
            if (cp < 0x10000 && Fallbacks.TryGetValue((char)cp, out var alt))
            {
                foreach (var a in alt) list.Add(_cmap.GetValueOrDefault(a, _cmap.GetValueOrDefault('?')));
                continue;
            }
            list.Add(_cmap.GetValueOrDefault('?'));
        }
        return list;
    }

    public double Width(string text, double size)
    {
        double units = 0;
        foreach (var g in Glyphs(text)) units += Advance(g);
        return units * size / UnitsPerEm;
    }

    /// <summary>Maps the glyphs used back to text, so PDF text can be selected, copied and searched.</summary>
    internal string ToUnicodeCMap(IEnumerable<ushort> glyphs)
    {
        var reverse = new Dictionary<ushort, int>();
        foreach (var (cp, g) in _cmap.OrderBy(kv => kv.Key)) reverse.TryAdd(g, cp);
        var pairs = glyphs.Where(reverse.ContainsKey).Select(g => (g, reverse[g])).ToList();
        var sb = new StringBuilder();
        sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        foreach (var chunk in pairs.Chunk(100))
        {
            sb.Append(chunk.Length).Append(" beginbfchar\n");
            foreach (var (g, cp) in chunk)
            {
                var utf16 = char.ConvertFromUtf32(cp);
                sb.Append('<').Append(g.ToString("X4")).Append("> <").Append(string.Concat(utf16.Select(c => ((int)c).ToString("X4")))).Append(">\n");
            }
            sb.Append("endbfchar\n");
        }
        sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return sb.ToString();
    }

    // ---------- TrueType tables ----------

    private void Parse()
    {
        var b = Data.AsSpan();
        var tables = new Dictionary<string, (int Offset, int Length)>();
        int count = BinaryPrimitives.ReadUInt16BigEndian(b[4..]);
        for (var i = 0; i < count; i++)
        {
            var rec = b.Slice(12 + i * 16, 16);
            tables[Encoding.ASCII.GetString(rec[..4])] = ((int)BinaryPrimitives.ReadUInt32BigEndian(rec[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(rec[12..]));
        }
        Span<byte> T(string tag) => tables.TryGetValue(tag, out var t) ? Data.AsSpan(t.Offset, t.Length) : Span<byte>.Empty;
        static short S16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadInt16BigEndian(s[at..]);
        static ushort U16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt16BigEndian(s[at..]);

        var head = T("head");
        UnitsPerEm = U16(head, 18);
        BBox = [S16(head, 36), S16(head, 38), S16(head, 40), S16(head, 42)];
        var hhea = T("hhea");
        Ascender = S16(hhea, 4);
        Descender = S16(hhea, 6);
        int metrics = U16(hhea, 34);
        int glyphs = U16(T("maxp"), 4);
        var os2 = T("OS/2");
        CapHeight = os2.Length >= 90 && U16(os2, 0) >= 2 ? S16(os2, 88) : (short)(Ascender * 0.7);
        var post = T("post");
        if (post.Length >= 8) ItalicAngle = BinaryPrimitives.ReadInt32BigEndian(post[4..]) / 65536.0;

        var hmtx = T("hmtx");
        _advances = new ushort[glyphs];
        for (var g = 0; g < glyphs; g++) _advances[g] = U16(hmtx, Math.Min(g, metrics - 1) * 4);

        // cmap: prefer Windows Unicode full (3,10 format 12), else BMP (3,1 or 0,x format 4).
        var cmap = T("cmap");
        int best = -1, bestScore = 0;
        for (var i = 0; i < U16(cmap, 2); i++)
        {
            int platform = U16(cmap, 4 + i * 8), encoding = U16(cmap, 6 + i * 8);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(cmap[(8 + i * 8)..]);
            int format = U16(cmap, offset);
            var score = (platform, encoding, format) switch
            {
                (3, 10, 12) => 4, (0, _, 12) => 3, (3, 1, 4) => 2, (0, _, 4) => 1, _ => 0,
            };
            if (score > bestScore) { bestScore = score; best = offset; }
        }
        if (best < 0) return;
        var sub = cmap[best..];
        if (U16(sub, 0) == 4)
        {
            int segs = U16(sub, 6) / 2;
            for (var s = 0; s < segs; s++)
            {
                int end = U16(sub, 14 + s * 2), start = U16(sub, 16 + segs * 2 + s * 2);
                var delta = S16(sub, 16 + segs * 4 + s * 2);
                int rangeAt = 16 + segs * 6 + s * 2, range = U16(sub, rangeAt);
                for (var c = start; c <= end && c != 0xFFFF; c++)
                {
                    int g;
                    if (range == 0) g = (c + delta) & 0xFFFF;
                    else
                    {
                        g = U16(sub, rangeAt + range + (c - start) * 2);
                        if (g != 0) g = (g + delta) & 0xFFFF;
                    }
                    if (g != 0) _cmap.TryAdd(c, (ushort)g);
                }
            }
        }
        else
        {
            var groups = BinaryPrimitives.ReadUInt32BigEndian(sub[12..]);
            for (var i = 0; i < groups; i++)
            {
                var gr = sub.Slice(16 + i * 12, 12);
                uint start = BinaryPrimitives.ReadUInt32BigEndian(gr), end = BinaryPrimitives.ReadUInt32BigEndian(gr[4..]), g = BinaryPrimitives.ReadUInt32BigEndian(gr[8..]);
                for (var c = start; c <= end && c - start < 0x10000; c++) _cmap.TryAdd((int)c, (ushort)(g + c - start));
            }
        }
    }
}
