using AviSite.Web.Content.Md;

namespace AviSite.Web.Content;

/// <summary>Records shared by pages. Text fields hold Markdown; render them with <see cref="Copy"/>.</summary>
public sealed record Person(string Name, string Role, string? Group = null, string? Email = null)
{
    /// <summary>File stem for photos (wwwroot/img/people) and profiles (content/people).</summary>
    public string Slug => People.SlugOf(Name);
}

/// <summary>One thing that happened inside a timeline chapter (shown when the chapter is opened).</summary>
public sealed record Moment(string Date, string Title, string? Body = null, string? Photo = null, string? Caption = null);
public sealed record Milestone(string When, string Title, string Body, string? Photo = null, IReadOnlyList<Moment>? Moments = null);
public sealed record Stat(string Value, string Label);
public sealed record Sponsor(string Name, string Logo, int Tier, int W, int H);
public sealed record Leader(string Name, string Role, string Major, IReadOnlyList<string> Did);
public sealed record Crew(string Id, string Name, string Lead, string Summary, IReadOnlyList<string> Built, IReadOnlyList<string> Members, string? Photo = null);
public sealed record SupportTeam(string Team, IReadOnlyList<string> Members);

/// <summary>Typed views over the content files. Each is cached per file version.</summary>
public sealed class ClubData(SiteContent content)
{
    private T From<T>(string file, string key, Func<MdDocument, T> build) where T : class
    {
        var doc = content.Doc(file);
        return (T)doc.Derived.GetOrAdd(typeof(T).FullName + "|" + key, _ => build(doc));
    }

    /// <summary>content/roster.md: "## advisors", "## officers"… each a list of "Name | Role | email".</summary>
    public IReadOnlyList<Person> Roster(string group) => From("roster", "roster", d =>
        new Dictionary<string, IReadOnlyList<Person>>(d.Sections.Select(s => KeyValuePair.Create(s.Key.ToLowerInvariant(),
            (IReadOnlyList<Person>)s.ListItems.Select(ParsePerson).ToList())), StringComparer.OrdinalIgnoreCase))
        .GetValueOrDefault(group, []);

    private static Person ParsePerson(string item)
    {
        var p = Pipes(item);
        return new Person(p[0], p.ElementAtOrDefault(1) ?? "", null, p.ElementAtOrDefault(2) is { Length: > 0 } e ? e : null);
    }

    /// <summary>content/timelines/&lt;name&gt;.md: "## Chapter title" + when/photo + body, "### Moment" + date/photo/caption + body.</summary>
    public IReadOnlyList<Milestone> Timeline(string name) => From($"timelines/{name}", "timeline", d =>
        (IReadOnlyList<Milestone>)d.Sections.Select(s => new Milestone(
            s.Field("when") ?? "", s.Key, s.Body, s.Field("photo"),
            s.Items.Select(m => new Moment(m.Field("date") ?? "", m.Key, NullIfEmpty(m.Body), m.Field("photo"), m.Field("caption"))).ToList())).ToList());

    /// <summary>content/sponsors.md: "## Name" + logo, tier, size (WxH of the logo file).</summary>
    public IReadOnlyList<Sponsor> Sponsors => From("sponsors", "sponsors", d =>
        (IReadOnlyList<Sponsor>)d.Sections.Where(s => s.Field("logo") is not null).Select(s =>
        {
            var size = (s.Field("size") ?? "300x150").Split('x');
            return new Sponsor(s.Key, s.Field("logo")!, int.Parse(s.Field("tier") ?? "4"), int.Parse(size[0]), int.Parse(size[1]));
        }).ToList());

    /// <summary>Tier names from content/sponsors.md front matter (tier-1: Anchor …).</summary>
    public string TierLabel(int tier) => content.Doc("sponsors").Meta.GetValueOrDefault($"tier-{tier}") ?? $"Tier {tier}";

    /// <summary>"- value | label" lines in a section.</summary>
    public IReadOnlyList<Stat> Stats(string file, string section) => From(file, "stats|" + section, d =>
        (IReadOnlyList<Stat>)(d.Section(section)?.ListItems ?? []).Select(i => { var p = Pipes(i); return new Stat(p[0], p.ElementAtOrDefault(1) ?? ""); }).ToList());

    /// <summary>content/sms/company.md "## leadership": "### Name" + role, major + a bullet list of what they did.</summary>
    public IReadOnlyList<Leader> SmsLeaders => From("sms/company", "leaders", d =>
        (IReadOnlyList<Leader>)(d.Section("leadership")?.Items ?? []).Select(i =>
            new Leader(i.Key, i.Field("role") ?? "", i.Field("major") ?? "", i.ListItems)).ToList());

    /// <summary>content/sms/company.md "## crews": "### Crew name" + id, lead, photo, members (comma list) + summary + bullets.</summary>
    public IReadOnlyList<Crew> SmsCrews => From("sms/company", "crews", d =>
        (IReadOnlyList<Crew>)(d.Section("crews")?.Items ?? []).Select(i =>
            new Crew(i.Field("id") ?? People.SlugOf(i.Key), i.Key, i.Field("lead") ?? "", i.Prose, i.ListItems,
                Commas(i.Field("members")), i.Field("photo"))).ToList());

    /// <summary>content/sms/company.md "## support": "- Team: Name, Name".</summary>
    public IReadOnlyList<SupportTeam> SmsSupport => From("sms/company", "support", d =>
        (IReadOnlyList<SupportTeam>)(d.Section("support")?.ListItems ?? []).Select(i =>
        {
            var at = i.IndexOf(':');
            return new SupportTeam(i[..at].Trim(), Commas(i[(at + 1)..]));
        }).ToList());

    public static string[] Pipes(string s) => s.Split('|').Select(p => p.Trim()).ToArray();
    public static IReadOnlyList<string> Commas(string? s) =>
        string.IsNullOrWhiteSpace(s) ? [] : s.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
