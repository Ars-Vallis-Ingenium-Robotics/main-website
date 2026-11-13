using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AviSite.Web.Content.Md;

/// <summary>
/// A small Markdown renderer for site copy. Supported:
///   paragraphs (blank line between), "- " bullet lists, "1. " numbered lists,
///   **bold**, *italic* or _italic_, `code`, [text](link), and {tokens}.
/// Everything is HTML-escaped first, so copy can't inject markup.
/// Links: https://… (opens in a new tab), /path, #anchor, mailto:…, person:first-last (name pop-up), sms:/path and avi:/path
/// (the right host for the SMS microsite), or a name from content/site.md → "## links".
/// Tokens ({ein}, {status}, {meeting-time}…) come from the site settings and content/site.md → "## tokens".
/// </summary>
public static partial class MdRenderer
{
    public delegate string? TokenHtml(string name);
    public delegate (string Href, bool External)? LinkResolver(string href);

    [GeneratedRegex(@"^\s*(?:[-*]|\d+\.)\s+")] private static partial Regex ListMarker();
    [GeneratedRegex(@"^\s*\d+\.\s+")] private static partial Regex OrderedMarker();
    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")] private static partial Regex Link();
    [GeneratedRegex(@"`([^`]+)`")] private static partial Regex Code();
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])")] private static partial Regex StarEm();
    [GeneratedRegex(@"(?<![\w])_(?!\s)(.+?)(?<!\s)_(?![\w])")] private static partial Regex UnderEm();
    [GeneratedRegex(@"\{([a-z][a-z0-9-]*)\}")] private static partial Regex Token();
    [GeneratedRegex("\u0001(\\d+)\u0002")] private static partial Regex Slot();

    /// <summary>Block Markdown → HTML (paragraphs and lists).</summary>
    public static string Blocks(string md, TokenHtml tokens, LinkResolver links)
    {
        var sb = new StringBuilder();
        foreach (var block in SplitBlocks(md))
        {
            if (block.All(l => ListMarker().IsMatch(l)))
            {
                var tag = OrderedMarker().IsMatch(block[0]) ? "ol" : "ul";
                sb.Append('<').Append(tag).Append('>');
                foreach (var l in block) sb.Append("<li>").Append(Inline(ListMarker().Replace(l, ""), tokens, links)).Append("</li>");
                sb.Append("</").Append(tag).Append('>');
            }
            else
            {
                sb.Append("<p>").Append(Inline(string.Join(' ', block.Select(l => l.Trim())), tokens, links)).Append("</p>");
            }
        }
        return sb.ToString();
    }

    /// <summary>Inline Markdown → HTML (no &lt;p&gt;). Multiple lines are joined with spaces.</summary>
    public static string Inline(string md, TokenHtml tokens, LinkResolver links)
    {
        var slots = new List<string>();
        string Keep(string html) { slots.Add(html); return $"\u0001{slots.Count - 1}\u0002"; }

        var s = string.Join(' ', md.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
        s = Code().Replace(s, m => Keep($"<code>{WebUtility.HtmlEncode(m.Groups[1].Value)}</code>"));
        s = Token().Replace(s, m => tokens(m.Groups[1].Value) is { } html ? Keep(html) : m.Value);
        s = Link().Replace(s, m =>
        {
            var text = Inline(m.Groups[1].Value, tokens, links);
            var target = m.Groups[2].Value;
            if (target.StartsWith("person:"))
            {
                // [Robert Voss](person:robert-voss) → the name opens that person's pop-up
                var slug = WebUtility.HtmlEncode(target[7..]);
                return Keep($"<a class=\"person-link\" href=\"/people/{slug}\" data-person=\"{slug}\">{text}</a>");
            }
            var r = links(target);
            if (r is not { } link) return Keep(text);
            var ext = link.External ? " target=\"_blank\" rel=\"noopener\"" : "";
            return Keep($"<a href=\"{WebUtility.HtmlEncode(link.Href)}\"{ext}>{text}</a>");
        });
        s = WebUtility.HtmlEncode(s);
        s = Bold().Replace(s, "<strong>$1</strong>");
        s = StarEm().Replace(s, "<em>$1</em>");
        s = UnderEm().Replace(s, "<em>$1</em>");
        // slots may contain slots (link text with tokens), so restore until stable
        for (var guard = 0; guard < 4 && s.Contains('\u0001'); guard++)
            s = Slot().Replace(s, m => slots[int.Parse(m.Groups[1].Value)]);
        return s;
    }

    /// <summary>Plain text: Markdown markers removed, links reduced to their text, tokens as text.</summary>
    public static string Plain(string md, Func<string, string?> tokenText)
    {
        var s = string.Join(' ', md.Split('\n').Select(l => ListMarker().Replace(l, "").Trim()).Where(l => l.Length > 0));
        s = Link().Replace(s, "$1");
        s = Code().Replace(s, "$1");
        s = Bold().Replace(s, "$1");
        s = StarEm().Replace(s, "$1");
        s = UnderEm().Replace(s, "$1");
        s = Token().Replace(s, m => tokenText(m.Groups[1].Value) ?? m.Value);
        return s;
    }

    public static IReadOnlyList<string> ListItems(string md)
    {
        var items = new List<string>();
        foreach (var block in SplitBlocks(md))
            foreach (var l in block)
                if (ListMarker().IsMatch(l)) items.Add(ListMarker().Replace(l, "").Trim());
        return items;
    }

    public static string WithoutLists(string md) =>
        string.Join("\n\n", SplitBlocks(md).Where(b => !b.All(l => ListMarker().IsMatch(l))).Select(b => string.Join('\n', b)));

    /// <summary>Blocks separated by blank lines; list items absorb their indented continuation lines.</summary>
    private static List<List<string>> SplitBlocks(string md)
    {
        var blocks = new List<List<string>>();
        List<string>? cur = null;
        foreach (var raw in md.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0) { cur = null; continue; }
            if (cur is null) { cur = []; blocks.Add(cur); }
            var isItem = ListMarker().IsMatch(line);
            if (!isItem && cur.Count > 0 && ListMarker().IsMatch(cur[^1]) && (raw.StartsWith(' ') || raw.StartsWith('\t')))
                cur[^1] += " " + line.Trim();
            else if (isItem && cur.Count > 0 && !ListMarker().IsMatch(cur[^1]))
            {
                // a list right under a paragraph line: start a new block
                cur = [line]; blocks.Add(cur);
            }
            else cur.Add(line);
        }
        return blocks;
    }
}
