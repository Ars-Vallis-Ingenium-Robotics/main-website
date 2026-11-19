using System.Buffers.Binary;
using System.IO.Compression;

namespace AviSite.Web.Pdf;

/// <summary>
/// An image ready for a PDF: JPEG passes through as-is; PNG is decoded (8-bit or 16-bit, gray / RGB / palette, with or
/// without alpha, not interlaced) into raw samples plus a separate alpha mask.
/// </summary>
public sealed class PdfImage
{
    public int Width { get; }
    public int Height { get; }
    /// <summary>1 (gray) or 3 (RGB).</summary>
    public int Channels { get; }
    public bool IsJpeg { get; }
    /// <summary>Compressed samples: JPEG bytes, or zlib-deflated rows without PNG filter bytes.</summary>
    internal byte[] Data { get; }
    /// <summary>Deflated 8-bit alpha, if the PNG had any transparency.</summary>
    internal byte[]? Alpha { get; }

    private PdfImage(int w, int h, int channels, bool jpeg, byte[] data, byte[]? alpha)
    {
        Width = w; Height = h; Channels = channels; IsJpeg = jpeg; Data = data; Alpha = alpha;
    }

    /// <summary>PNG or JPEG bytes → image, or null (with a reason) if it isn't one we can use.</summary>
    public static PdfImage? From(byte[] bytes, out string? problem)
    {
        problem = null;
        try
        {
            if (LooksLikePng(bytes)) return FromPng(bytes, out problem);
            if (LooksLikeJpeg(bytes)) return FromJpeg(bytes, out problem);
            problem = "Use a PNG or JPEG file.";
            return null;
        }
        catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            problem = "That image file looks damaged.";
            return null;
        }
    }

    public static bool LooksLikePng(ReadOnlySpan<byte> b) => b.Length > 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    public static bool LooksLikeJpeg(ReadOnlySpan<byte> b) => b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;

    /// <summary>Width and height without decoding the pixels, or null.</summary>
    public static (int W, int H)? Size(byte[] b)
    {
        if (LooksLikePng(b) && b.Length >= 24) return (BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(20)));
        if (LooksLikeJpeg(b) && JpegInfo(b) is { } j) return (j.W, j.H);
        return null;
    }

    private static PdfImage? FromJpeg(byte[] b, out string? problem)
    {
        problem = null;
        if (JpegInfo(b) is not { } info) { problem = "That JPEG couldn't be read."; return null; }
        if (info.Channels is not (1 or 3)) { problem = "CMYK JPEGs aren't supported; save it as RGB."; return null; }
        return new PdfImage(info.W, info.H, info.Channels, true, b, null);
    }

    /// <summary>Reads the frame header (SOF0–SOF15 except DHT/JPG/DAC).</summary>
    private static (int W, int H, int Channels)? JpegInfo(byte[] b)
    {
        var i = 2;
        while (i + 9 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            var marker = b[i + 1];
            if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
            var len = (b[i + 2] << 8) | b[i + 3];
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6], b[i + 9]);
            i += 2 + len;
        }
        return null;
    }

    private static PdfImage? FromPng(byte[] b, out string? problem)
    {
        problem = null;
        int w = 0, h = 0, depth = 0, color = 0, interlace = 0;
        byte[]? palette = null, trns = null;
        using var idat = new MemoryStream();
        var pos = 8;
        while (pos + 8 <= b.Length)
        {
            var len = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(pos));
            var type = System.Text.Encoding.ASCII.GetString(b, pos + 4, 4);
            var data = b.AsSpan(pos + 8, len);
            switch (type)
            {
                case "IHDR":
                    w = BinaryPrimitives.ReadInt32BigEndian(data); h = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    depth = data[8]; color = data[9]; interlace = data[12];
                    break;
                case "PLTE": palette = data.ToArray(); break;
                case "tRNS": trns = data.ToArray(); break;
                case "IDAT": idat.Write(data); break;
                case "IEND": pos = b.Length; continue;
            }
            pos += 12 + len;
        }
        if (w <= 0 || h <= 0 || (long)w * h > 25_000_000) { problem = "That PNG is too large or damaged."; return null; }
        if (interlace != 0) { problem = "Interlaced PNGs aren't supported; re-save it without interlacing."; return null; }
        var channels = color switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
        if (channels == 0 || depth is not (1 or 2 or 4 or 8 or 16) || (depth < 8 && color is not (0 or 3)) || (color == 3 && palette is null))
        {
            problem = "That PNG format isn't supported.";
            return null;
        }

        // Inflate and undo the per-row filters.
        idat.Position = 0;
        using var z = new ZLibStream(idat, CompressionMode.Decompress);
        using var raw = new MemoryStream();
        z.CopyTo(raw);
        var src = raw.GetBuffer();
        var bpp = Math.Max(1, channels * depth / 8);
        var stride = (w * channels * depth + 7) / 8;
        if (raw.Length < (long)(stride + 1) * h) { problem = "That PNG looks damaged."; return null; }
        var pixels = new byte[stride * h];
        for (var y = 0; y < h; y++)
        {
            var filter = src[y * (stride + 1)];
            var row = pixels.AsSpan(y * stride, stride);
            src.AsSpan(y * (stride + 1) + 1, stride).CopyTo(row);
            var prev = y > 0 ? pixels.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            for (var x = 0; x < stride; x++)
            {
                int a = x >= bpp ? row[x - bpp] : 0, up = y > 0 ? prev[x] : 0, c = x >= bpp && y > 0 ? prev[x - bpp] : 0;
                row[x] = filter switch
                {
                    0 => row[x],
                    1 => (byte)(row[x] + a),
                    2 => (byte)(row[x] + up),
                    3 => (byte)(row[x] + (a + up) / 2),
                    4 => (byte)(row[x] + Paeth(a, up, c)),
                    _ => throw new InvalidDataException("filter"),
                };
            }
        }

        // To 8-bit gray or RGB, plus alpha.
        var outChannels = color is 0 or 4 ? 1 : 3;
        var samples = new byte[w * h * outChannels];
        byte[]? alpha = null;
        var hasAlpha = color is 4 or 6 || trns is not null;
        if (hasAlpha) alpha = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var i = y * w + x;
                int Sample(int ch)
                {
                    if (depth == 16) return pixels[y * stride + (x * channels + ch) * 2];
                    if (depth == 8) return pixels[y * stride + x * channels + ch];
                    var bit = x * depth; var by = pixels[y * stride + bit / 8];
                    return (by >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
                }
                switch (color)
                {
                    case 0:
                    {
                        var v = Sample(0);
                        var g = depth < 8 ? v * 255 / ((1 << depth) - 1) : v;
                        samples[i] = (byte)g;
                        if (alpha is not null) alpha[i] = trns is { Length: >= 2 } && depth <= 8 && v == ((trns[0] << 8) | trns[1]) ? (byte)0 : (byte)255;
                        break;
                    }
                    case 2:
                        samples[i * 3] = (byte)Sample(0); samples[i * 3 + 1] = (byte)Sample(1); samples[i * 3 + 2] = (byte)Sample(2);
                        if (alpha is not null) alpha[i] = 255;
                        break;
                    case 3:
                    {
                        var p = Sample(0);
                        if (p * 3 + 2 >= palette!.Length) p = 0;
                        samples[i * 3] = palette[p * 3]; samples[i * 3 + 1] = palette[p * 3 + 1]; samples[i * 3 + 2] = palette[p * 3 + 2];
                        if (alpha is not null) alpha[i] = trns is not null && p < trns.Length ? trns[p] : (byte)255;
                        break;
                    }
                    case 4:
                        samples[i] = (byte)Sample(0); alpha![i] = (byte)Sample(1);
                        break;
                    case 6:
                        samples[i * 3] = (byte)Sample(0); samples[i * 3 + 1] = (byte)Sample(1); samples[i * 3 + 2] = (byte)Sample(2);
                        alpha![i] = (byte)Sample(3);
                        break;
                }
            }
        }
        if (alpha is not null && alpha.All(a => a == 255)) alpha = null;
        return new PdfImage(w, h, outChannels, false, PdfDocument.Deflate(samples), alpha is null ? null : PdfDocument.Deflate(alpha));
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
