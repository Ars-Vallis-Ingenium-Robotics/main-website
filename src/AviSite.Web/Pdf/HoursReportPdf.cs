using System.Globalization;
using AviSite.Web.Endpoints;
using AviSite.Web.Store;

namespace AviSite.Web.Pdf;

/// <summary>
/// The hours report as a real PDF, in the team's branding: header with logo, name and tagline; stat tiles; hours over
/// time; categories and teams; and (internal reports only) per-person tables. US Letter, page numbers, the brand footer.
/// </summary>
public static class HoursReportPdf
{
    private const double M = 48;                 // page margin
    private const double FooterTop = 792 - 52;   // content must end above this
    private static readonly PdfColor Text = new(16, 16, 17);
    private static readonly PdfColor Muted = new(92, 98, 112);
    private static readonly PdfColor Hairline = new(222, 226, 231);

    public static byte[] Render(HoursReportData r, PdfImage? logo)
    {
        var primary = PdfColor.FromHex(r.Brand.Primary) ?? PdfColor.Black;
        var accent = PdfColor.FromHex(r.Brand.Accent) ?? new PdfColor(12, 99, 230);
        var sponsor = r.Audience == ReportAudience.Sponsor;
        var title = sponsor ? "Hours summary" : "Hours report";
        var doc = new PdfDocument { Title = $"{r.Brand.Name} · {title} · {r.ScopeName} · {r.RangeLabel}", Author = r.Brand.Name };
        var page = doc.AddPage();
        var width = page.Width - 2 * M;
        var y = Header(page, r, logo, primary, title);

        // A new page gets a slim running header.
        double Ensure(double needed)
        {
            if (y + needed <= FooterTop - 8) return y;
            page = doc.AddPage();
            page.Text(M, M + 10, PdfText.Fit($"{r.Brand.Name} · {title} · {r.ScopeName} · {r.RangeLabel}", PdfFont.Bold, 9, width), PdfFont.Bold, 9, primary);
            page.Rect(M, M + 18, width, 1.5, primary);
            return y = M + 34;
        }

        // Stat tiles.
        var stats = new List<(string Value, string Label)>
        {
            (HoursText.Hours(r.Total), "hours"),
            (r.People.ToString(CultureInfo.InvariantCulture), r.People == 1 ? "person logged time" : "people logged time"),
            (r.Sessions.ToString(CultureInfo.InvariantCulture), r.Sessions == 1 ? "session" : "sessions"),
            (HoursText.Hours(r.People == 0 ? 0 : r.Total / r.People), "hours per person"),
        };
        const double gap = 10, tileH = 58;
        var tileW = (width - gap * (stats.Count - 1)) / stats.Count;
        for (var i = 0; i < stats.Count; i++)
        {
            var x = M + i * (tileW + gap);
            page.RoundRect(x, y, tileW, tileH, 4, primary.Tint(0.93));
            page.Text(x + 12, y + 30, stats[i].Value, PdfFont.Bold, 21, primary);
            page.Text(x + 12, y + 46, PdfText.Fit(stats[i].Label, PdfFont.Regular, 8.5, tileW - 20), PdfFont.Regular, 8.5, Muted);
        }
        y += tileH + 26;

        if (r.Total <= 0)
        {
            page.Text(M, y + 10, "No hours in this range yet.", PdfFont.Regular, 11, Muted);
            Footers(doc, r);
            return doc.Save();
        }

        // Hours over time.
        Ensure(200);
        y = Heading(page, y, $"Hours per {(r.Weekly ? "week" : "month")}", primary);
        y = Columns(page, M, y, width, 150, r.OverTime, accent) + 22;

        // Categories and teams, side by side.
        var cats = r.ByCategory; var teams = r.ByTeam;
        var rows = Math.Max(Math.Min(cats.Count, 10), Math.Min(teams.Count, 10));
        Ensure(40 + rows * 19);
        var half = (width - 24) / 2;
        var yc = Heading(page, y, "By category", primary);
        var endC = Bars(page, M, yc, half, cats, accent, 10, r.Total);
        var yt = Heading(page, y, "By team", primary, M + half + 24);
        var endT = Bars(page, M + half + 24, yt, half, teams, accent, 10, null);
        y = Math.Max(endC, endT) + 22;

        if (!sponsor)
        {
            // By person: rank, name, hours, share.
            Ensure(60);
            y = Heading(page, y, $"By person{(r.ByPerson.Count > 40 ? " (top 40)" : "")}", primary);
            var people = r.ByPerson.Take(40).ToList();
            var max = people.Count > 0 ? people.Max(p => p.Value) : 1;
            for (var i = 0; i < people.Count; i++)
            {
                if (y + 17 > FooterTop - 8) { Ensure(40); y = Heading(page, y, "By person (continued)", primary); }
                var (name, h) = people[i];
                page.Text(M, y + 11, (i + 1).ToString(CultureInfo.InvariantCulture), PdfFont.Regular, 8.5, Muted);
                page.Text(M + 22, y + 11, PdfText.Fit(name, PdfFont.Regular, 9, 170), PdfFont.Regular, 9, Text);
                var bw = Math.Max(2, (width - 22 - 180 - 60) * h / max);
                page.RoundRect(M + 202, y + 3, bw, 9, 2, accent);
                page.TextRight(M + width, y + 11, HoursText.Hours(h) + " h", PdfFont.Bold, 9, Text);
                page.Line(M, y + 16, M + width, y + 16, Hairline, 0.4);
                y += 17;
            }
            y += 20;

            // Everyone by category: the biggest categories as columns, the rest as "Other".
            var shown = r.ByCategory.Take(5).Select(c => c.Label).ToList();
            var other = r.ByCategory.Count > shown.Count;
            var headers = shown.Concat(other ? ["Other"] : Array.Empty<string>()).Append("Total").ToList();
            const double nameW = 150;
            var colW = (width - nameW) / headers.Count;
            void MatrixHead()
            {
                page.Rect(M, y, width, 20, primary.Tint(0.93));
                page.Text(M + 6, y + 13.5, "Person", PdfFont.Bold, 8, Text);
                for (var c = 0; c < headers.Count; c++)
                    page.TextRight(M + nameW + (c + 1) * colW - 6, y + 13.5, PdfText.Fit(headers[c], PdfFont.Bold, 8, colW - 8), PdfFont.Bold, 8, Text);
                y += 20;
            }
            Ensure(80);
            y = Heading(page, y, "Everyone, by category", primary);
            MatrixHead();
            foreach (var (person, cells, sum) in r.Matrix)
            {
                if (y + 16 > FooterTop - 8) { Ensure(60); y = Heading(page, y, "Everyone, by category (continued)", primary); MatrixHead(); }
                page.Text(M + 6, y + 11, PdfText.Fit(person, PdfFont.Regular, 8.5, nameW - 10), PdfFont.Regular, 8.5, Text);
                var values = cells.Take(shown.Count).ToList();
                if (other) values.Add(cells.Skip(shown.Count).Sum());
                values.Add(sum);
                for (var c = 0; c < values.Count; c++)
                {
                    var v = values[c];
                    if (v <= 0) continue;
                    page.TextRight(M + nameW + (c + 1) * colW - 6, y + 11, HoursText.Hours(v), c == values.Count - 1 ? PdfFont.Bold : PdfFont.Regular, 8.5, Text);
                }
                page.Line(M, y + 15.5, M + width, y + 15.5, Hairline, 0.4);
                y += 16;
            }
        }
        else
        {
            Ensure(40);
            page.Paragraph(M, y, width, $"{r.People} {(r.People == 1 ? "person" : "people")} logged {HoursText.Hours(r.Total)} hours across {r.ByTeam.Count} {(r.ByTeam.Count == 1 ? "team" : "teams")}, {r.RangeLabel.ToLowerInvariant()}.",
                PdfFont.Regular, 10, Text);
        }

        Footers(doc, r);
        return doc.Save();
    }

    private static double Header(PdfPage page, HoursReportData r, PdfImage? logo, PdfColor primary, string title)
    {
        var width = page.Width - 2 * M;
        var x = M;
        if (logo is not null) x += page.ImageFit(logo, M, M - 4, 150, 56) + 16;
        var rightW = 190;
        var nameW = M + width - rightW - 16 - x;
        page.Text(x, M + 20, PdfText.Fit(r.Brand.Name, PdfFont.Bold, 18, nameW), PdfFont.Bold, 18, primary);
        if (r.Brand.Tagline is { } tag) page.Paragraph(x, M + 25, nameW, tag, PdfFont.Regular, 9.5, Muted, 1.3, 2);
        page.TextRight(M + width, M + 10, title.ToUpperInvariant(), PdfFont.Bold, 8, Muted);
        // The full path if it fits ("AVI Robotics › Sequoia Marine Solutions"), else just the team's own name.
        var scope = PdfText.Width(r.ScopeName, PdfFont.Bold, 11) <= rightW || r.Team is null ? r.ScopeName : r.Access.Tree.Name(r.Team);
        page.TextRight(M + width, M + 26, PdfText.Fit(scope, PdfFont.Bold, 11, rightW), PdfFont.Bold, 11, Text);
        page.TextRight(M + width, M + 40, r.RangeLabel, PdfFont.Regular, 9.5, Muted);
        page.Rect(M, M + 64, width, 3, primary);
        return M + 84;
    }

    private static double Heading(PdfPage page, double y, string text, PdfColor primary, double x = M)
    {
        page.Text(x, y + 11, text, PdfFont.Bold, 11.5, primary);
        return y + 22;
    }

    /// <summary>Column chart: hairline gridlines at clean values, columns in the accent color, the tallest one labeled.</summary>
    private static double Columns(PdfPage page, double x, double y, double w, double h, List<Components.Shared.ColumnChart.Point> points, PdfColor accent)
    {
        if (points.Count == 0) return y;
        const double left = 30, bottom = 16;
        var peak = Math.Max(points.Max(p => p.Value), 1);
        var step = NiceStep(peak / 4);
        var top = Math.Ceiling(peak / step) * step;
        double Y(double v) => y + (h - bottom) * (1 - v / top);
        for (var t = 0.0; t <= top + 1e-9; t += step)
        {
            page.Line(x + left, Y(t), x + w, Y(t), Hairline, t == 0 ? 0.8 : 0.4);
            page.TextRight(x + left - 5, Y(t) + 3, t.ToString(step < 1 ? "0.#" : "0", CultureInfo.InvariantCulture), PdfFont.Regular, 7, Muted);
        }
        var slot = (w - left) / points.Count;
        var bar = Math.Min(22, slot * 0.7);
        var every = (int)Math.Ceiling(points.Count / 14.0);
        var maxIndex = points.FindIndex(p => p.Value == points.Max(q => q.Value));
        for (var i = 0; i < points.Count; i++)
        {
            var cx = x + left + slot * i + slot / 2;
            var v = points[i].Value;
            var top0 = Y(v);
            if (Y(0) - top0 > 0.5) page.RoundRect(cx - bar / 2, top0, bar, Y(0) - top0, Math.Min(3, bar / 2), accent);
            if (Y(0) - top0 > 0.5 && Y(0) - top0 > 3) page.Rect(cx - bar / 2, Y(0) - 3, bar, 3, accent); // square base
            if (i == maxIndex) page.TextCenter(cx, top0 - 4, HoursText.Hours(v), PdfFont.Bold, 7.5, Text);
            if (i % every == 0) page.TextCenter(cx, y + h - 3, points[i].Short, PdfFont.Regular, 7, Muted);
        }
        return y + h;
    }

    private static double NiceStep(double raw)
    {
        if (raw <= 0) return 1;
        var mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var n = raw / mag;
        return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * mag;
    }

    /// <summary>Labeled horizontal bars, biggest first; "+ n more" when cut. With a total, each row shows its share.</summary>
    private static double Bars(PdfPage page, double x, double y, double w, List<(string Label, double Value)> rows, PdfColor accent, int max, double? total)
    {
        if (rows.Count == 0) { page.Text(x, y + 10, "None", PdfFont.Regular, 9, Muted); return y + 16; }
        var peak = rows.Max(r => r.Value);
        const double valueW = 64;
        // Labels get the room they need, up to 45% of the column.
        var labelW = Math.Min(w * 0.45, rows.Take(max).Max(r => PdfText.Width(r.Label, PdfFont.Regular, 8.5)) + 10);
        foreach (var (label, v) in rows.Take(max))
        {
            page.Text(x, y + 10, PdfText.Fit(label, PdfFont.Regular, 8.5, labelW - 6), PdfFont.Regular, 8.5, Text);
            var bw = Math.Max(2, (w - labelW - valueW) * v / peak);
            page.RoundRect(x + labelW, y + 2, bw, 10, 2, accent);
            var value = HoursText.Hours(v) + " h" + (total is { } t && t > 0 ? $"  {Math.Round(v * 100 / t):0}%" : "");
            page.TextRight(x + w, y + 10, value, PdfFont.Regular, 8, Text);
            y += 19;
        }
        if (rows.Count > max) { page.Text(x, y + 9, $"+ {rows.Count - max} more", PdfFont.Regular, 8, Muted); y += 15; }
        return y;
    }

    private static void Footers(PdfDocument doc, HoursReportData r)
    {
        var generated = $"Generated {r.Generated.ToPacific().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture)} Pacific";
        for (var i = 0; i < doc.Pages.Count; i++)
        {
            var p = doc.Pages[i];
            var width = p.Width - 2 * M;
            p.Line(M, FooterTop + 6, M + width, FooterTop + 6, Hairline, 0.6);
            var left = r.Brand.Footer is { } f ? f : generated;
            p.Paragraph(M, FooterTop + 9, width - 90, left, PdfFont.Regular, 7.5, Muted, 1.3, 2);
            p.TextRight(M + width, FooterTop + 20, $"Page {i + 1} of {doc.Pages.Count}", PdfFont.Regular, 7.5, Muted);
            if (r.Brand.Footer is not null) p.TextRight(M + width, FooterTop + 30, generated, PdfFont.Regular, 6.5, Muted);
        }
    }
}
