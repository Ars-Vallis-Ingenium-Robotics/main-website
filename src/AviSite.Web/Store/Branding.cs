using System.Globalization;
using System.Text.RegularExpressions;
using AviSite.Web.Endpoints;
using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

/// <summary>
/// One group's branding for reports. Organizations (top-level groups) always use their own; a team under one uses its
/// own only when the organization's leads switch that on (e.g. Crush Depth under CCC Engineering Renaissance, SMS under AVI).
/// Everything else uses the nearest branded group above it.
/// </summary>
public sealed class Branding
{
    public string GroupId { get; set; } = "";
    /// <summary>For teams: their own branding is on. Organizations ignore this (always on).</summary>
    public bool Enabled { get; set; }
    public string? DisplayName { get; set; }
    public string? Tagline { get; set; }
    /// <summary>Headings, the header rule and stat numbers. Must read on white (4.5:1).</summary>
    public string? Primary { get; set; }
    /// <summary>Chart bars. Must be visible on white (3:1).</summary>
    public string? Accent { get; set; }
    public string? Footer { get; set; }
    public string? LogoType { get; set; }
    /// <summary>Changes with every logo upload; part of the logo URL so browsers fetch the new one.</summary>
    public string? LogoStamp { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
}

/// <summary>What a report actually uses: the resolved branding for a team, with defaults filled in.</summary>
public sealed record Brand(string SourceGroup, string Name, string? Tagline, string Primary, string Accent, string? Footer, string? LogoUrl)
{
    /// <summary>AVI's own colors (ink, blurple): the default for anyone who hasn't picked theirs.</summary>
    public const string DefaultPrimary = "#101011";
    public const string DefaultAccent = "#0c63e6";
}

public sealed class BrandingStore(SiteDb db, TimeProvider clock)
{
    public const int MaxLogoBytes = 1_000_000;
    private const string Cols = "group_id, enabled, display_name, tagline, primary_color, accent_color, footer, logo_type, logo_stamp, updated_by, updated_utc";

    private static Branding Read(SqliteDataReader r) => new()
    {
        GroupId = r.GetString(0), Enabled = r.GetInt64(1) != 0, DisplayName = SiteDb.StrOrNull(r, 2), Tagline = SiteDb.StrOrNull(r, 3),
        Primary = SiteDb.StrOrNull(r, 4), Accent = SiteDb.StrOrNull(r, 5), Footer = SiteDb.StrOrNull(r, 6), LogoType = SiteDb.StrOrNull(r, 7),
        LogoStamp = SiteDb.StrOrNull(r, 8), UpdatedBy = SiteDb.StrOrNull(r, 9), UpdatedUtc = SiteDb.TimeOrNull(r, 10),
    };

    public Dictionary<string, Branding> All()
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, $"SELECT {Cols} FROM group_branding");
        using var r = cmd.ExecuteReader();
        var all = new Dictionary<string, Branding>();
        while (r.Read()) { var b = Read(r); all[b.GroupId] = b; }
        return all;
    }

    public Branding? Get(string groupId) => All().GetValueOrDefault(groupId);

    /// <summary>The text and colors (not the logo, not the on/off switch).</summary>
    public void Save(Branding b, string by)
    {
        using var c = db.Open();
        SiteDb.Exec(c, """
            INSERT INTO group_branding (group_id, enabled, display_name, tagline, primary_color, accent_color, footer, updated_by, updated_utc)
            VALUES ($g, 0, $name, $tag, $p, $a, $f, $by, $now)
            ON CONFLICT(group_id) DO UPDATE SET display_name = excluded.display_name, tagline = excluded.tagline, primary_color = excluded.primary_color,
                accent_color = excluded.accent_color, footer = excluded.footer, updated_by = excluded.updated_by, updated_utc = excluded.updated_utc
            """, null, ("$g", b.GroupId), ("$name", b.DisplayName), ("$tag", b.Tagline), ("$p", b.Primary), ("$a", b.Accent), ("$f", b.Footer),
            ("$by", by), ("$now", SiteDb.Iso(clock.GetUtcNow())));
    }

    /// <summary>A team's "own branding" switch (organizations are always on).</summary>
    public void SetEnabled(string groupId, bool enabled, string by)
    {
        using var c = db.Open();
        SiteDb.Exec(c, """
            INSERT INTO group_branding (group_id, enabled, updated_by, updated_utc) VALUES ($g, $e, $by, $now)
            ON CONFLICT(group_id) DO UPDATE SET enabled = excluded.enabled, updated_by = excluded.updated_by, updated_utc = excluded.updated_utc
            """, null, ("$g", groupId), ("$e", enabled ? 1 : 0), ("$by", by), ("$now", SiteDb.Iso(clock.GetUtcNow())));
    }

    public void SetLogo(string groupId, byte[]? logo, string? type, string by)
    {
        using var c = db.Open();
        SiteDb.Exec(c, """
            INSERT INTO group_branding (group_id, enabled, logo, logo_type, logo_stamp, updated_by, updated_utc) VALUES ($g, 0, $l, $t, $s, $by, $now)
            ON CONFLICT(group_id) DO UPDATE SET logo = excluded.logo, logo_type = excluded.logo_type, logo_stamp = excluded.logo_stamp,
                updated_by = excluded.updated_by, updated_utc = excluded.updated_utc
            """, null, ("$g", groupId), ("$l", logo), ("$t", logo is null ? null : type), ("$s", logo is null ? null : Guid.NewGuid().ToString("N")[..10]),
            ("$by", by), ("$now", SiteDb.Iso(clock.GetUtcNow())));
    }

    public (byte[] Bytes, string Type)? Logo(string groupId)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "SELECT logo, logo_type FROM group_branding WHERE group_id = $g AND logo IS NOT NULL", null, ("$g", groupId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? ((byte[])r[0], r.GetString(1)) : null;
    }

    /// <summary>The group whose branding <paramref name="team"/> uses: itself if it's an organization or switched on, else the nearest such group above.</summary>
    public static string SourceFor(string team, GroupTree tree, IReadOnlyDictionary<string, Branding> all)
    {
        for (var g = tree.Find(team); g is not null; g = tree.Find(g.Parent))
            if (g.Parent is null || (all.TryGetValue(g.Id, out var b) && b.Enabled)) return g.Id;
        return team;
    }

    public Brand Resolve(string team, GroupTree tree)
    {
        var all = All();
        var source = SourceFor(team, tree, all);
        all.TryGetValue(source, out var b);
        return new Brand(source,
            string.IsNullOrWhiteSpace(b?.DisplayName) ? tree.Name(source) : b.DisplayName!,
            string.IsNullOrWhiteSpace(b?.Tagline) ? null : b.Tagline,
            b?.Primary ?? Brand.DefaultPrimary,
            b?.Accent ?? Brand.DefaultAccent,
            string.IsNullOrWhiteSpace(b?.Footer) ? null : b.Footer,
            b?.LogoStamp is { } stamp ? $"/branding/{source}/logo?v={stamp}" : null);
    }
}

/// <summary>Who may change branding, and color checks.</summary>
public static partial class BrandingRules
{
    /// <summary>A group's logo, colors and text: the owner and its leads (an organization's leads cover every team in it).</summary>
    public static bool CanEdit(Access a, string groupId) => a.IsOwner || a.Leads.Contains(groupId);

    /// <summary>A team's "own branding" switch: the owner and the leads of the team above it. Organizations are always on.</summary>
    public static bool CanToggle(Access a, AccountGroup g) => g.Parent is not null && a.CanEditTeamsUnder(g.Parent);

    [GeneratedRegex("^#[0-9a-f]{6}$")]
    private static partial Regex Hex();

    /// <summary>"#0C63E6" / "0c63e6" → "#0c63e6", or null.</summary>
    public static string? NormalizeColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().ToLowerInvariant();
        if (!s.StartsWith('#')) s = "#" + s;
        return Hex().IsMatch(s) ? s : null;
    }

    /// <summary>WCAG contrast ratio of a color against white.</summary>
    public static double ContrastOnWhite(string hex)
    {
        var v = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        static double Lin(int c) { var s = c / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        var l = 0.2126 * Lin((v >> 16) & 255) + 0.7152 * Lin((v >> 8) & 255) + 0.0722 * Lin(v & 255);
        return 1.05 / (l + 0.05);
    }
}
