using System.Net;
using AviSite.Web.Content.Md;
using AviSite.Web.Options;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Content;

/// <summary>
/// Renders copy from content/ for the current request (links to the SMS microsite depend on the host).
/// In Razor: <c>@inject Copy Copy</c>, then <c>var c = Copy.Page("pages/home")</c> and
/// <c>@c.Text("intro.title")</c>, <c>@c.Inline("intro.lede")</c>, <c>@c.Html("purpose")</c>.
/// </summary>
public sealed class Copy(SiteContent content, SmsUrls sms, IOptions<NonprofitOptions> nonprofit, IOptions<SiteOptions> site)
{
    public PageCopy Page(string name) => new(this, content.Doc(name));
    public PageCopy Site => Page("site");

    /// <summary>{token} as plain text.</summary>
    public string? TokenText(string name)
    {
        var np = nonprofit.Value;
        return name switch
        {
            "status" => np.Status,
            "legal-name" => np.LegalName,
            "ein" => $"EIN {np.Ein}",
            "ein-number" => np.Ein,
            "effective-date" => np.EffectiveDate?.ToString("MMMM d, yyyy") ?? "2025",
            "contact-email" => site.Value.ContactEmail,
            "site-name" => site.Value.Name,
            "long-name" => site.Value.LongName,
            _ => content.Doc("site").Section("tokens")?.Field(name),
        };
    }

    /// <summary>{token} as HTML. {ein} keeps "EIN" and the number together on one line.</summary>
    public string? TokenHtml(string name) => name switch
    {
        "ein" => $"<span class=\"ein\">EIN <span class=\"mono\">{WebUtility.HtmlEncode(nonprofit.Value.Ein)}</span></span>",
        _ => TokenText(name) is { } t ? MdRenderer.Inline(t, _ => null, ResolveLink) : null,
    };

    /// <summary>Where a Markdown link goes. Null drops the link and keeps its text.</summary>
    public (string Href, bool External)? ResolveLink(string href)
    {
        if (href.StartsWith("sms:")) return (sms.Sms(Path(href[4..])), false);
        if (href.StartsWith("avi:")) return (sms.Avi(Path(href[4..])), false);
        if (href.StartsWith("http://") || href.StartsWith("https://")) return (href, true);
        if (href.StartsWith('/') || href.StartsWith('#') || href.StartsWith("mailto:") || href.StartsWith("tel:"))
            return (href, href.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));
        // Links set in appsettings (left out = not configured yet = no link).
        string? configured = href switch
        {
            "sponsor-packet" => site.Value.SponsorPacketPath,
            "discord" => site.Value.DiscordInviteUrl,
            "youtube" => site.Value.YouTubeUrl,
            "linktree" => site.Value.LinktreeUrl,
            "instagram" => site.Value.InstagramUrl,
            "portal" => site.Value.PortalUrl,
            "contact" => site.Value.ContactEmail is { } e ? "mailto:" + e : null,
            _ => "",
        };
        if (configured is null) return null;
        var named = configured.Length > 0 ? configured : content.Doc("site").Section("links")?.Field(href);
        if (named is null) { content.Missing($"link name '{href}' (content/site.md → ## links)"); return null; }
        return ResolveLink(named);

        static string Path(string p) => p.Length == 0 ? "/" : p;
    }

    public MarkupString Inline(string? md) => new(md is null ? "" : MdRenderer.Inline(md, TokenHtml, ResolveLink));
    public MarkupString Blocks(string? md) => new(md is null ? "" : MdRenderer.Blocks(md, TokenHtml, ResolveLink));
    public string Plain(string? md) => md is null ? "" : MdRenderer.Plain(md, TokenText);

    /// <summary>A named link (sms:/2026, sponsor-packet, /join…) resolved to an href.</summary>
    public string Href(string link) => ResolveLink(link)?.Href ?? "#";

    internal string MissingMarker(string what)
    {
        content.Missing(what);
        return content.IsDevelopment ? $"⟦missing {what}⟧" : "";
    }
}

/// <summary>One content file, ready to render. <c>c["intro"]</c> gets a section.</summary>
public sealed class PageCopy(Copy copy, MdDocument doc)
{
    public MdDocument Doc => doc;
    public Copy Copy => copy;

    public SectionCopy this[string key] =>
        doc.Section(key) is { } s ? new SectionCopy(copy, s, $"{doc.Path}.md › ## {key}") : SectionCopy.Missing(copy, $"{doc.Path}.md › ## {key}");

    public bool Has(string key) => doc.Section(key) is not null;

    /// <summary>Front matter value (title, description…) as plain text.</summary>
    public string Meta(string key) => doc.Meta.TryGetValue(key, out var v) ? copy.Plain(v) : copy.MissingMarker($"{doc.Path}.md › {key}:");
}

/// <summary>A link written as [text](where) in a field or list item.</summary>
public sealed record LinkCopy(MarkupString Text, string PlainText, string Href, bool External)
{
    public string? Target => External ? "_blank" : null;
    public string? Rel => External ? "noopener" : null;
}

/// <summary>One "## section" (or "### record") of a content file.</summary>
public sealed partial class SectionCopy(Copy copy, MdSection section, string where)
{
    private readonly bool _missing;
    private SectionCopy(Copy copy, string where, bool missing) : this(copy, new MdSection { Key = "" }, where) => _missing = missing;
    internal static SectionCopy Missing(Copy copy, string where) => new(copy, where, true) { };

    public MdSection Raw => section;
    public string Key => section.Key;
    /// <summary>The heading text rendered inline (for records whose title is the heading).</summary>
    public MarkupString Title => copy.Inline(section.Key);
    public string TitleText => copy.Plain(section.Key);

    private string BodyMd => _missing ? copy.MissingMarker(where) : section.Body;
    private string FieldMd(string field) =>
        section.Fields.TryGetValue(field, out var v) ? v : copy.MissingMarker($"{where} › {field}:");

    public bool Has(string field) => !_missing && section.Field(field) is not null;
    /// <summary>A field, unrendered (photo keys, ids…). Null when absent.</summary>
    public string? Value(string field) => section.Field(field);
    /// <summary>The "photo:" field (a key from the photo manifest).</summary>
    public string Photo => section.Field("photo") ?? (_missing ? "" : copy.MissingMarker($"{where} › photo:"));

    /// <summary>Body: paragraphs and lists → HTML. Use for prose blocks.</summary>
    public MarkupString Html() => copy.Blocks(BodyMd);
    /// <summary>Body as one inline run (no &lt;p&gt;).</summary>
    public MarkupString Inline() => copy.Inline(BodyMd);
    /// <summary>Body as plain text (attributes, titles).</summary>
    public string Text() => copy.Plain(BodyMd);
    /// <summary>Body paragraphs only (lists dropped), as HTML.</summary>
    public MarkupString ProseHtml() => copy.Blocks(_missing ? BodyMd : section.Prose);
    /// <summary>Body paragraphs only (lists dropped), inline.</summary>
    public MarkupString ProseInline() => copy.Inline(_missing ? BodyMd : section.Prose);

    public MarkupString Inline(string field) => copy.Inline(FieldMd(field));
    public string Text(string field) => copy.Plain(FieldMd(field));

    /// <summary>A field holding [text](where).</summary>
    public LinkCopy Link(string field) => ParseLink(FieldMd(field));
    /// <summary>The body is a single [text](where).</summary>
    public LinkCopy Link() => ParseLink(BodyMd);

    /// <summary>The body's "- item" lines, raw Markdown.</summary>
    public IReadOnlyList<string> List() => _missing ? [BodyMd] : section.ListItems;
    /// <summary>List items rendered inline.</summary>
    public IEnumerable<MarkupString> ListHtml() => List().Select(copy.Inline);
    /// <summary>"- [text](where)" items. Links to unconfigured places (e.g. no Discord yet) are left out.</summary>
    public IReadOnlyList<LinkCopy> Links() => List().Select(i => ParseLink(i, skipUnresolved: true)).OfType<LinkCopy>().ToList();
    /// <summary>"- Term: value" items (spec sheets).</summary>
    public IReadOnlyList<(string Term, MarkupString Value)> Pairs() => List().Select(i =>
    {
        var at = i.IndexOf(": ", StringComparison.Ordinal);
        return at < 0 ? (copy.Plain(i), new MarkupString("")) : (copy.Plain(i[..at]), copy.Inline(i[(at + 2)..]));
    }).ToList();
    /// <summary>"- a | b | c" items split into columns (raw Markdown).</summary>
    public IReadOnlyList<string[]> Rows() => List().Select(ClubData.Pipes).ToList();

    /// <summary>The "### records" under this section.</summary>
    public IReadOnlyList<SectionCopy> Items => section.Items.Select(i => new SectionCopy(copy, i, $"{where} › ### {i.Key}")).ToList();

    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*\[(.+)\]\(([^)\s]+)\)\s*$")]
    private static partial System.Text.RegularExpressions.Regex LinkMd();

    private LinkCopy ParseLink(string md) => ParseLink(md, false)!;
    private LinkCopy? ParseLink(string md, bool skipUnresolved)
    {
        var m = LinkMd().Match(md);
        if (!m.Success) return new LinkCopy(copy.Inline(md), copy.Plain(md), "#", false);
        var r = copy.ResolveLink(m.Groups[2].Value);
        if (r is null) return skipUnresolved ? null : new LinkCopy(copy.Inline(m.Groups[1].Value), copy.Plain(m.Groups[1].Value), "#", false);
        return new LinkCopy(copy.Inline(m.Groups[1].Value), copy.Plain(m.Groups[1].Value), r.Value.Href, r.Value.External);
    }
}
