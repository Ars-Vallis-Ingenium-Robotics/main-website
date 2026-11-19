using System.Text.RegularExpressions;
using AviSite.Web.Pdf;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>
/// Report branding (/admin/branding): each organization's logo, colors, name, tagline and report footer, and the switch
/// that lets a team under it have its own. Plus the hours report as a PDF in that branding.
/// </summary>
public static partial class BrandingEndpoints
{
    public const string Page = "/admin/branding";

    public static void MapBrandingEndpoints(this WebApplication app)
    {
        // Logos are only shown on signed-in pages and in PDFs, so they stay behind sign-in too.
        app.MapGet("/branding/{group}/logo", (string group, HttpContext http, BrandingStore store) =>
        {
            if (store.Logo(group) is not { } logo) return Results.NotFound();
            http.Response.Headers.CacheControl = "private, max-age=31536000, immutable"; // the URL changes with every upload
            return Results.File(logo.Bytes, logo.Type);
        }).RequireAuthorization(AdminEndpoints.QrPolicy);

        var admin = app.MapGroup(Page).RequireAuthorization(AdminEndpoints.QrPolicy);

        admin.MapPost("/{group}", async (string group, [FromForm] string? displayName, [FromForm] string? tagline, [FromForm] string? primary,
            [FromForm] string? accent, [FromForm] string? footer, HttpContext http, IAccountStore accounts, BrandingStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (me.Tree.Find(group) is null) return Back(error: "gone");
            if (!BrandingRules.CanEdit(me, group) || me.Account is null) return Back(error: "denied", group: group);
            var p = BrandingRules.NormalizeColor(primary);
            var a = BrandingRules.NormalizeColor(accent);
            if ((!string.IsNullOrWhiteSpace(primary) && p is null) || (!string.IsNullOrWhiteSpace(accent) && a is null)) return Back(error: "color", group: group);
            if (p is not null && BrandingRules.ContrastOnWhite(p) < 4.5) return Back(error: "primary-contrast", group: group);
            if (a is not null && BrandingRules.ContrastOnWhite(a) < 3) return Back(error: "accent-contrast", group: group);
            store.Save(new Branding
            {
                GroupId = group, DisplayName = Clean(displayName, 80), Tagline = Clean(tagline, 140), Primary = p, Accent = a, Footer = Clean(footer, 400),
            }, me.Account.Email);
            return Back(done: "saved", group: group);
        });

        admin.MapPost("/{group}/logo", async (string group, IFormFile? logo, [FromForm] bool? remove, HttpContext http, IAccountStore accounts, BrandingStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (me.Tree.Find(group) is null) return Back(error: "gone");
            if (!BrandingRules.CanEdit(me, group) || me.Account is null) return Back(error: "denied", group: group);
            if (remove == true) { store.SetLogo(group, null, null, me.Account.Email); return Back(done: "logo-removed", group: group); }
            if (logo is null || logo.Length == 0) return Back(error: "logo-missing", group: group);
            if (logo.Length > BrandingStore.MaxLogoBytes) return Back(error: "logo-size", group: group);
            using var ms = new MemoryStream();
            await logo.CopyToAsync(ms, http.RequestAborted);
            var bytes = ms.ToArray();
            // Check by content, not by the file name or browser's claim, and make sure the PDF writer can use it.
            if (PdfImage.From(bytes, out _) is null) return Back(error: "logo-format", group: group);
            if (PdfImage.Size(bytes) is not { } size || size.W > 4000 || size.H > 4000) return Back(error: "logo-size", group: group);
            store.SetLogo(group, bytes, PdfImage.LooksLikePng(bytes) ? "image/png" : "image/jpeg", me.Account.Email);
            return Back(done: "logo", group: group);
        });

        admin.MapPost("/{group}/own", async (string group, [FromForm] bool? enabled, HttpContext http, IAccountStore accounts, BrandingStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (me.Tree.Find(group) is not { } g) return Back(error: "gone");
            if (!BrandingRules.CanToggle(me, g) || me.Account is null) return Back(error: "denied", group: g.Parent);
            store.SetEnabled(group, enabled == true, me.Account.Email);
            return Back(done: enabled == true ? "own-on" : "own-off", group: enabled == true ? group : g.Parent);
        });

        // The hours report as a PDF (same data and audience as /admin/hours/report).
        app.MapGet("/admin/hours/report.pdf", async (string? team, string? range, string? view, HttpContext http, IAccountStore accounts,
            HoursStore hours, BrandingStore branding) =>
        {
            var data = await HoursReportData.BuildAsync(http, accounts, hours, branding, team, range, view);
            if (data is null) return Results.NotFound();
            var logo = branding.Logo(data.Brand.SourceGroup) is { } l ? PdfImage.From(l.Bytes, out _) : null;
            var pdf = HoursReportPdf.Render(data, logo);
            var name = Slug($"{data.Brand.Name} hours {(data.Audience == ReportAudience.Sponsor ? "summary" : "report")} {data.Team ?? "all"} {data.RangeLabel}");
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(pdf, "application/pdf", name + ".pdf");
        }).RequireAuthorization(AdminEndpoints.QrPolicy);
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().Replace("\r\n", "\n");
        return s.Length > max ? s[..max] : s;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
    private static string Slug(string s) => NonSlug().Replace(s.ToLowerInvariant(), "-").Trim('-');

    private static IResult Back(string? done = null, string? error = null, string? group = null) =>
        Results.LocalRedirect(Page + (done is not null ? "?done=" + done : error is not null ? "?error=" + error : "") + (group is null ? "" : "#brand-" + group));
}
