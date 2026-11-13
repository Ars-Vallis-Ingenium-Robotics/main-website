using System.Text.RegularExpressions;

namespace AviSite.Web.Content;

/// <summary>A link a member chose to share (LinkedIn, email, portfolio…).</summary>
public sealed record ProfileLink(string Kind, string Label, string Href);

/// <summary>
/// A member's profile from content/people/&lt;slug&gt;.md. Everything is opt-in: a field that's
/// blank or missing is simply not shown. The body under the front matter is a short bio (Markdown).
/// </summary>
public sealed record Profile(string Slug, string Name, string? Pronouns, string? Major, string? Headline, string? Photo,
    IReadOnlyList<ProfileLink> Links, string Bio);

/// <summary>Everyone the site mentions, their profile (if they made one) and every role they hold.</summary>
public sealed partial class People(SiteContent content, ClubData data, IWebHostEnvironment env)
{
    // Order matters: shown top to bottom in the pop-up.
    private static readonly (string Field, string Label)[] LinkFields =
    [
        ("email", "Email"), ("linkedin", "LinkedIn"), ("github", "GitHub"), ("portfolio", "Portfolio"),
        ("website", "Website"), ("resume", "Résumé"), ("instagram", "Instagram"),
    ];

    [GeneratedRegex(@"[^a-z0-9\- ]")] private static partial Regex NotSlug();
    [GeneratedRegex(@"\s*\(([^)]*)\)\s*$")] private static partial Regex Paren();

    public static string SlugOf(string name)
    {
        var n = Paren().Replace(name, "").Replace("Dr. ", "").ToLowerInvariant();
        n = n.Normalize(System.Text.NormalizationForm.FormD);
        n = NotSlug().Replace(n, "");
        return string.Join('-', n.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>"Guillermo Prado (pilot)" → ("Guillermo Prado", "pilot").</summary>
    public static (string Name, string? Note) SplitNote(string raw)
    {
        var m = Paren().Match(raw);
        return m.Success ? (raw[..m.Index].Trim(), m.Groups[1].Value) : (raw.Trim(), null);
    }

    public Profile? Profile(string slug)
    {
        if (!content.Files("people").Contains($"people/{slug}", StringComparer.OrdinalIgnoreCase)) return null;
        var d = content.Doc($"people/{slug}");
        string? M(string k) => d.Meta.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
        var links = new List<ProfileLink>();
        foreach (var (field, label) in LinkFields)
        {
            if (M(field) is not { } v) continue;
            var href = field == "email" ? (v.StartsWith("mailto:") ? v : "mailto:" + v)
                : v.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v : "https://" + v;
            links.Add(new ProfileLink(field, field == "email" ? v.Replace("mailto:", "") : label, href));
        }
        return new Profile(slug, M("name") ?? slug, M("pronouns"), M("major"), M("headline"), M("photo"), links, d.Intro);
    }

    public string? PhotoSlug(string slug)
    {
        var s = Profile(slug)?.Photo ?? slug;
        return env.WebRootFileProvider.GetFileInfo($"img/people/{s}-160.webp").Exists ? s : null;
    }

    /// <summary>Every person named anywhere on the site → display name and their roles, in site order.</summary>
    public IReadOnlyDictionary<string, (string Name, List<string> Roles)> Directory()
    {
        var map = new Dictionary<string, (string Name, List<string> Roles)>(StringComparer.OrdinalIgnoreCase);
        void Add(string rawName, string role)
        {
            var (name, _) = SplitNote(rawName);
            var slug = SlugOf(name);
            if (slug.Length == 0) return;
            if (!map.TryGetValue(slug, out var e)) map[slug] = e = (name, []);
            if (!e.Roles.Contains(role)) e.Roles.Add(role);
        }
        const string avi = "AVI Robotics", sms = "Sequoia Marine Solutions 2026";
        foreach (var g in new[] { "founders", "advisors", "mentors", "officers", "leads" })
            foreach (var p in data.Roster(g)) Add(p.Name, $"{p.Role} · {avi}");
        foreach (var l in data.SmsLeaders) Add(l.Name, $"{l.Role} · {sms}");
        foreach (var c in data.SmsCrews)
        {
            Add(c.Lead, $"Lead, {c.Name} · {sms}");
            foreach (var m in c.Members)
            {
                var (_, note) = SplitNote(m);
                Add(m, $"{c.Name}{(note is null ? "" : $" ({note})")} · {sms}");
            }
        }
        foreach (var t in data.SmsSupport) foreach (var m in t.Members) Add(m, $"{t.Team} · {sms}");
        foreach (var f in content.Files("people"))
        {
            var slug = f["people/".Length..];
            if (!map.ContainsKey(slug) && Profile(slug) is { } p) map[slug] = (p.Name, []);
        }
        return map;
    }
}
