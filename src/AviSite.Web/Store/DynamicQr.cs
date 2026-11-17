using System.Text.Json;
using System.Text.RegularExpressions;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>
/// A printed QR code that points at avirobotics.org/dynamicqr/{Slug}; the site redirects it to <see cref="Target"/>,
/// which officers can change in /admin/qr after the code is printed.
/// </summary>
public sealed class DynamicQr
{
    public string Slug { get; set; } = "";
    public string Target { get; set; } = "";
    /// <summary>Name sent to the Google Sheet with each scan (the old "campaign").</summary>
    public string Campaign { get; set; } = "";
    public string? Note { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public int Scans { get; set; }
    public DateTimeOffset? LastScanUtc { get; set; }
    /// <summary>Scans per day (yyyy-MM-dd, Pacific), last 90 days.</summary>
    public SortedDictionary<string, int> Daily { get; set; } = [];
    /// <summary>All-time scans by weekday (0 = Sunday) and hour (0–23), Pacific.</summary>
    public int[] Weekday { get; set; } = new int[7];
    public int[] Hour { get; set; } = new int[24];
    /// <summary>All-time scans by device: iPhone/iPad, Android, Computer, Other. From the User-Agent only; nothing personal is kept.</summary>
    public Dictionary<string, int> Devices { get; set; } = [];
    /// <summary>Team that owns the code (null = club-wide, officers only). Team codes are named "&lt;team&gt;-…".</summary>
    public string? Team { get; set; }
    /// <summary>Email of the account that made it. Members only see codes they made.</summary>
    public string? CreatedBy { get; set; }
    /// <summary>Set when deleted. The name stays reserved forever so nobody can re-point old printed copies,
    /// and scans of it go to the home page.</summary>
    /// <summary>Saved look from the QR maker (its settings as JSON), so every reprint matches. Null = the maker's default.</summary>
    public string? Look { get; set; }
    /// <summary>Uploaded logo for the saved look (PNG data URL, already shrunk by the maker).</summary>
    public string? LookLogo { get; set; }
    public DateTimeOffset? LookUpdatedUtc { get; set; }
    public string? LookBy { get; set; }
    public DateTimeOffset? DeletedUtc { get; set; }
    public string? DeletedBy { get; set; }
}

public sealed class DynamicQrOptions
{
    public const string Section = "DynamicQr";
    /// <summary>Google Apps Script web app that appends a row per scan (the old sheet). Empty = don't send.</summary>
    public string? GoogleScriptUrl { get; set; }
    /// <summary>Only production should write to the sheet; testing scans would muddy it.</summary>
    public bool SendToGoogleSheet { get; set; }
}

public static partial class DynamicQrRules
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,78}[a-z0-9])?$")]
    public static partial Regex SlugPattern();

    public static string? CheckSlug(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? "Give it a short name." :
        !SlugPattern().IsMatch(slug) ? "Use lowercase letters, numbers and dashes (up to 80)." : null;

    /// <summary>Who may see and change a code: the owner any; officers club-wide codes and AVI's; team leads their teams'
    /// (and sub-teams'); members the ones they made on their teams.</summary>
    public static bool CanAccess(Access a, DynamicQr code) =>
        a.IsOwner
        || (code.Team is null && a.IsOfficer)
        || (code.Team is not null && a.Manages.Contains(code.Team))
        || (code.Team is not null && a.BelongsTo.Contains(code.Team) && code.CreatedBy is not null && code.CreatedBy == a.Account?.Email);

    /// <summary>Teams someone can make codes for (null = club-wide: owner and officers).</summary>
    public static IEnumerable<string?> CreatableTeams(Access a) =>
        (a.CanSeeClubWide ? new string?[] { null } : []).Concat(a.Tree.Ordered(g => a.CanSee(g.Id)).Select(g => (string?)g.Id));

    /// <summary>
    /// Every code under a team is named after the whole chain from its organization down: "avi-sms-electrical-flyer" for
    /// Electrical in SMS in AVI, "avi-flyer" for AVI itself. So a name says whose it is, and teams can't collide.
    /// </summary>
    public static string PrefixFor(string team, GroupTree tree)
    {
        var ids = new List<string>();
        for (var g = tree.Find(team); g is not null && ids.Count < 12; g = tree.Find(g.Parent)) ids.Insert(0, g.Id);
        if (ids.Count == 0) ids.Add(team);
        return string.Join("-", ids) + "-";
    }

    /// <summary>"flyer" → "avi-sms-flyer": a name typed without its team prefix gets it added.</summary>
    public static string WithPrefix(string slug, string? team, GroupTree tree) =>
        team is null || slug.StartsWith(PrefixFor(team, tree), StringComparison.Ordinal) ? slug : PrefixFor(team, tree) + slug;

    public static string? CheckTeamSlug(string slug, string? team, GroupTree tree)
    {
        if (team is null) return null;
        var prefix = PrefixFor(team, tree);
        return slug.StartsWith(prefix, StringComparison.Ordinal) && slug.Length > prefix.Length ? null
            : $"{tree.Name(team)} codes start with \"{prefix}\", then a name: {prefix}flyer.";
    }

    /// <summary>Absolute http(s) links, or a path on this site like /join.</summary>
    public static string? CheckTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return "Where should it go?";
        target = target.Trim();
        if (target.StartsWith('/') && !target.StartsWith("//")) return null;
        return Uri.TryCreate(target, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http")
            ? null : "Use a full link starting with https:// (or a page on this site like /join).";
    }
}

public interface IDynamicQrStore
{
    Task<IReadOnlyList<DynamicQr>> ListAsync(CancellationToken ct = default);
    Task<DynamicQr?> GetAsync(string slug, CancellationToken ct = default);
    /// <summary>Counts a scan and returns the code (null if unknown).</summary>
    Task<DynamicQr?> ScanAsync(string slug, string? userAgent = null, CancellationToken ct = default);
    Task<bool> CreateAsync(DynamicQr code, CancellationToken ct = default);
    Task<bool> UpdateAsync(string slug, Action<DynamicQr> change, CancellationToken ct = default);
    /// <summary>Hides the code everywhere and sends its scans to the home page. Its name can't be used again.</summary>
    Task<bool> DeleteAsync(string slug, string? by, CancellationToken ct = default);
    /// <summary>True if the name was ever used (including deleted codes).</summary>
    Task<bool> IsTakenAsync(string slug, CancellationToken ct = default);
}

/// <summary>
/// Dynamic QR codes in SQLite (<see cref="SiteDb"/>). A scan updates a few counters on one row and one day row,
/// never the logos. On first start it imports the old dynamic-qr.json and renames it to dynamic-qr.json.imported.
/// </summary>
public sealed class SqliteDynamicQrStore : IDynamicQrStore
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private readonly SiteDb _db;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _write = new(1, 1);
    private const string Columns = "slug, target, campaign, note, active, team, created_by, created_utc, updated_utc, scans, last_scan_utc, weekday, hour, devices, look, look_updated_utc, look_by, deleted_utc, deleted_by";

    public SqliteDynamicQrStore(SiteDb db, IOptions<StoreOptions> options, IWebHostEnvironment env, TimeProvider clock, ILogger<SqliteDynamicQrStore> log)
    {
        _db = db;
        _clock = clock;
        using var c = db.Open();
        using (var cmd = SiteDb.Cmd(c, "SELECT COUNT(*) FROM qr_codes")) if ((long)cmd.ExecuteScalar()! > 0) return;
        var json = Path.Combine(env.ContentRootPath, options.Value.DataDirectory, "dynamic-qr.json");
        var codes = File.Exists(json)
            ? JsonSerializer.Deserialize<List<DynamicQr>>(File.ReadAllText(json), JsonDefaults.Options) ?? []
            : Seed(clock.GetUtcNow());
        using var tx = c.BeginTransaction();
        foreach (var q in codes) Insert(c, tx, q);
        tx.Commit();
        if (File.Exists(json))
        {
            File.Move(json, json + ".imported", overwrite: true);
            log.LogInformation("Imported {Count} dynamic QR codes from dynamic-qr.json into site.db (old file kept as dynamic-qr.json.imported)", codes.Count);
        }
    }

    /// <summary>The four codes already printed from the old site (legacy-static/dynamicqr/*).</summary>
    private static List<DynamicQr> Seed(DateTimeOffset now) =>
    [
        new() { Slug = "one", Target = "https://avirobotics.org/", Campaign = "QR_Main_2026", Note = "Main site", CreatedUtc = now, UpdatedUtc = now },
        new() { Slug = "two", Target = "https://forms.gle/3D1GYYsCyemHoMx96", Campaign = "9-11-26 Sign in form", Note = "Meeting sign-in form", CreatedUtc = now, UpdatedUtc = now },
        new() { Slug = "three", Target = "https://linktr.ee/avi_robotics", Campaign = "Linktree", CreatedUtc = now, UpdatedUtc = now },
        new() { Slug = "four", Target = "https://discord.gg/NHMHVrp473", Campaign = "Discord Join", CreatedUtc = now, UpdatedUtc = now },
    ];

    private static void Insert(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, DynamicQr q)
    {
        SiteDb.Exec(c, $"INSERT INTO qr_codes ({Columns}) VALUES ($slug,$target,$campaign,$note,$active,$team,$by,$created,$updated,$scans,$last,$wk,$hr,$dev,$look,$lookAt,$lookBy,$del,$delBy)", tx, Params(q));
        foreach (var (day, n) in q.Daily) SiteDb.Exec(c, "INSERT INTO qr_daily (slug, day, scans) VALUES ($s,$d,$n)", tx, ("$s", q.Slug), ("$d", day), ("$n", n));
        if (q.LookLogo is not null) SiteDb.Exec(c, "INSERT INTO qr_logos (slug, data) VALUES ($s,$d)", tx, ("$s", q.Slug), ("$d", q.LookLogo));
    }

    private static (string, object?)[] Params(DynamicQr q) =>
    [
        ("$slug", q.Slug), ("$target", q.Target), ("$campaign", q.Campaign), ("$note", q.Note), ("$active", q.Active ? 1 : 0),
        ("$team", q.Team), ("$by", q.CreatedBy), ("$created", SiteDb.Iso(q.CreatedUtc)), ("$updated", SiteDb.Iso(q.UpdatedUtc)),
        ("$scans", q.Scans), ("$last", SiteDb.Iso(q.LastScanUtc)),
        ("$wk", JsonSerializer.Serialize(q.Weekday is { Length: 7 } ? q.Weekday : new int[7])),
        ("$hr", JsonSerializer.Serialize(q.Hour is { Length: 24 } ? q.Hour : new int[24])),
        ("$dev", JsonSerializer.Serialize(q.Devices)), ("$look", q.Look), ("$lookAt", SiteDb.Iso(q.LookUpdatedUtc)), ("$lookBy", q.LookBy),
        ("$del", SiteDb.Iso(q.DeletedUtc)), ("$delBy", q.DeletedBy),
    ];

    private static DynamicQr Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Slug = r.GetString(0), Target = r.GetString(1), Campaign = r.GetString(2), Note = SiteDb.StrOrNull(r, 3), Active = r.GetInt64(4) != 0,
        Team = SiteDb.StrOrNull(r, 5), CreatedBy = SiteDb.StrOrNull(r, 6), CreatedUtc = SiteDb.Time(r.GetString(7)), UpdatedUtc = SiteDb.Time(r.GetString(8)),
        Scans = (int)r.GetInt64(9), LastScanUtc = SiteDb.TimeOrNull(r, 10),
        Weekday = JsonSerializer.Deserialize<int[]>(r.GetString(11)) ?? new int[7],
        Hour = JsonSerializer.Deserialize<int[]>(r.GetString(12)) ?? new int[24],
        Devices = JsonSerializer.Deserialize<Dictionary<string, int>>(r.GetString(13)) ?? [],
        Look = SiteDb.StrOrNull(r, 14), LookUpdatedUtc = SiteDb.TimeOrNull(r, 15), LookBy = SiteDb.StrOrNull(r, 16),
        DeletedUtc = SiteDb.TimeOrNull(r, 17), DeletedBy = SiteDb.StrOrNull(r, 18),
    };

    private string DailyFrom() => TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Pacific).Date.AddDays(-89).ToString("yyyy-MM-dd");

    /// <summary>Loads codes (no logos) with their last 90 days of scans.</summary>
    private List<DynamicQr> Load(Microsoft.Data.Sqlite.SqliteConnection c, string where, params (string, object?)[] args)
    {
        var list = new List<DynamicQr>();
        using (var cmd = SiteDb.Cmd(c, $"SELECT {Columns} FROM qr_codes WHERE {where}", null, args))
        using (var r = cmd.ExecuteReader()) while (r.Read()) list.Add(Read(r));
        if (list.Count == 0) return list;
        var bySlug = list.ToDictionary(q => q.Slug);
        using (var cmd = SiteDb.Cmd(c, "SELECT slug, day, scans FROM qr_daily WHERE day >= $from ORDER BY day", null, ("$from", DailyFrom())))
        using (var r = cmd.ExecuteReader())
            while (r.Read()) if (bySlug.TryGetValue(r.GetString(0), out var q)) q.Daily[r.GetString(1)] = (int)r.GetInt64(2);
        return list;
    }

    public Task<IReadOnlyList<DynamicQr>> ListAsync(CancellationToken ct = default)
    {
        using var c = _db.Open();
        return Task.FromResult<IReadOnlyList<DynamicQr>>(Load(c, "deleted_utc IS NULL ORDER BY created_utc"));
    }

    public Task<DynamicQr?> GetAsync(string slug, CancellationToken ct = default)
    {
        using var c = _db.Open();
        return Task.FromResult(Get(c, slug, includeDeleted: false));
    }

    private DynamicQr? Get(Microsoft.Data.Sqlite.SqliteConnection c, string slug, bool includeDeleted)
    {
        var q = Load(c, "slug = $s COLLATE NOCASE" + (includeDeleted ? "" : " AND deleted_utc IS NULL"), ("$s", slug)).FirstOrDefault();
        if (q is null) return null;
        using var cmd = SiteDb.Cmd(c, "SELECT data FROM qr_logos WHERE slug = $s", null, ("$s", q.Slug));
        q.LookLogo = cmd.ExecuteScalar() as string;
        return q;
    }

    public static string DeviceOf(string? ua)
    {
        ua ??= "";
        if (ua.Contains("iPhone") || ua.Contains("iPad") || ua.Contains("iPod")) return "iPhone/iPad";
        if (ua.Contains("Android")) return "Android";
        if (ua.Contains("Windows") || ua.Contains("Macintosh") || ua.Contains("X11") || ua.Contains("CrOS")) return "Computer";
        return "Other";
    }

    public async Task<DynamicQr?> ScanAsync(string slug, string? userAgent = null, CancellationToken ct = default)
    {
        await _write.WaitAsync(ct);
        try
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            DynamicQr? q;
            using (var cmd = SiteDb.Cmd(c, $"SELECT {Columns} FROM qr_codes WHERE slug = $s COLLATE NOCASE", tx, ("$s", slug)))
            using (var r = cmd.ExecuteReader()) q = r.Read() ? Read(r) : null;
            if (q is null) return null;
            if (q.DeletedUtc is not null) return new DynamicQr { Slug = q.Slug, Active = false }; // old prints → home page
            if (q.Active)
            {
                var now = _clock.GetUtcNow();
                var local = TimeZoneInfo.ConvertTime(now, Pacific);
                q.Weekday[(int)local.DayOfWeek]++;
                q.Hour[local.Hour]++;
                var device = DeviceOf(userAgent);
                q.Devices[device] = q.Devices.GetValueOrDefault(device) + 1;
                q.Scans++;
                q.LastScanUtc = now;
                SiteDb.Exec(c, "UPDATE qr_codes SET scans = scans + 1, last_scan_utc = $t, weekday = $wk, hour = $hr, devices = $dev WHERE slug = $s", tx,
                    ("$t", SiteDb.Iso(now)), ("$wk", JsonSerializer.Serialize(q.Weekday)), ("$hr", JsonSerializer.Serialize(q.Hour)),
                    ("$dev", JsonSerializer.Serialize(q.Devices)), ("$s", q.Slug));
                SiteDb.Exec(c, "INSERT INTO qr_daily (slug, day, scans) VALUES ($s, $d, 1) ON CONFLICT(slug, day) DO UPDATE SET scans = scans + 1", tx,
                    ("$s", q.Slug), ("$d", local.ToString("yyyy-MM-dd")));
            }
            tx.Commit();
            return q;
        }
        finally { _write.Release(); }
    }

    public async Task<bool> CreateAsync(DynamicQr code, CancellationToken ct = default)
    {
        await _write.WaitAsync(ct);
        try
        {
            using var c = _db.Open();
            if (Get(c, code.Slug, includeDeleted: true) is not null) return false;
            code.CreatedUtc = code.UpdatedUtc = _clock.GetUtcNow();
            using var tx = c.BeginTransaction();
            Insert(c, tx, code);
            tx.Commit();
            return true;
        }
        finally { _write.Release(); }
    }

    public async Task<bool> UpdateAsync(string slug, Action<DynamicQr> change, CancellationToken ct = default)
    {
        await _write.WaitAsync(ct);
        try
        {
            using var c = _db.Open();
            var q = Get(c, slug, includeDeleted: false);
            if (q is null) return false;
            var printed = q.Slug;
            change(q);
            q.Slug = printed; // the printed address never changes
            q.UpdatedUtc = _clock.GetUtcNow();
            using var tx = c.BeginTransaction();
            SiteDb.Exec(c, """
                UPDATE qr_codes SET target=$target, campaign=$campaign, note=$note, active=$active, team=$team, created_by=$by,
                    updated_utc=$updated, look=$look, look_updated_utc=$lookAt, look_by=$lookBy WHERE slug=$slug
                """, tx, Params(q));
            if (q.LookLogo is null) SiteDb.Exec(c, "DELETE FROM qr_logos WHERE slug = $s", tx, ("$s", q.Slug));
            else SiteDb.Exec(c, "INSERT INTO qr_logos (slug, data) VALUES ($s,$d) ON CONFLICT(slug) DO UPDATE SET data = excluded.data", tx, ("$s", q.Slug), ("$d", q.LookLogo));
            tx.Commit();
            return true;
        }
        finally { _write.Release(); }
    }

    public async Task<bool> DeleteAsync(string slug, string? by, CancellationToken ct = default)
    {
        await _write.WaitAsync(ct);
        try
        {
            using var c = _db.Open();
            var now = SiteDb.Iso(_clock.GetUtcNow());
            using var cmd = SiteDb.Cmd(c, "UPDATE qr_codes SET deleted_utc = $t, deleted_by = $by, active = 0, updated_utc = $t WHERE slug = $s COLLATE NOCASE AND deleted_utc IS NULL",
                null, ("$t", now), ("$by", by), ("$s", slug));
            return cmd.ExecuteNonQuery() > 0;
        }
        finally { _write.Release(); }
    }

    public Task<bool> IsTakenAsync(string slug, CancellationToken ct = default)
    {
        using var c = _db.Open();
        return Task.FromResult(Get(c, slug, includeDeleted: true) is not null);
    }
}
