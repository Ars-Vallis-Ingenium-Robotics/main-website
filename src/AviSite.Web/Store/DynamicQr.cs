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
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")]
    public static partial Regex SlugPattern();

    public static string? CheckSlug(string? slug) =>
        string.IsNullOrWhiteSpace(slug) ? "Give it a short name." :
        !SlugPattern().IsMatch(slug) ? "Use lowercase letters, numbers and dashes (up to 40)." : null;

    /// <summary>Team codes live under "&lt;team&gt;-" so teams can't take each other's (or the club's) names.</summary>
    public static string? CheckTeamSlug(string slug, string? team) =>
        team is null || slug.StartsWith(team + "-", StringComparison.Ordinal) ? null : $"Your team's codes start with \"{team}-\".";

    /// <summary>Who may see and change a code: officers any; leads their group's; members the ones they made.</summary>
    public static bool CanAccess(System.Security.Claims.ClaimsPrincipal user, DynamicQr code)
    {
        if (user.IsInRole("Admin")) return true;
        var team = user.FindFirst(AviSite.Web.Endpoints.AdminEndpoints.TeamClaim)?.Value;
        if (team is null || code.Team != team) return false;
        return !user.IsInRole("Member") || code.CreatedBy == user.FindFirst(AviSite.Web.Endpoints.AccountAuth.EmailClaim)?.Value;
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

/// <summary>Same draft storage as orders: one JSON file in the data directory, atomic rewrite.</summary>
public sealed class JsonDynamicQrStore : IDynamicQrStore
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<DynamicQr> _codes;
    private readonly TimeProvider _clock;

    public JsonDynamicQrStore(IOptions<StoreOptions> options, IWebHostEnvironment env, TimeProvider clock)
    {
        _clock = clock;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "dynamic-qr.json");
        _codes = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<DynamicQr>>(File.ReadAllText(_path), JsonDefaults.Options) ?? []
            : Seed(clock.GetUtcNow());
        if (!File.Exists(_path)) Save();
    }

    /// <summary>The four codes already printed from the old site (legacy-static/dynamicqr/*).</summary>
    private static List<DynamicQr> Seed(DateTimeOffset now) =>
    [
        new() { Slug = "one", Target = "https://avirobotics.org/", Campaign = "QR_Main_2026", Note = "Main site", CreatedUtc = now, UpdatedUtc = now },
        new() { Slug = "two", Target = "https://forms.gle/3D1GYYsCyemHoMx96", Campaign = "9-11-26 Sign in form", Note = "Meeting sign-in form", CreatedUtc = now, UpdatedUtc = now },
        new() { Slug = "three", Target = "https://linktr.ee/avi_robotics", Campaign = "Linktree", CreatedUtc = now, UpdatedUtc = now },
        new() { Slug = "four", Target = "https://discord.gg/NHMHVrp473", Campaign = "Discord Join", CreatedUtc = now, UpdatedUtc = now },
    ];

    public async Task<IReadOnlyList<DynamicQr>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _codes.Where(c => c.DeletedUtc is null).Select(Clone).ToList()!; }
        finally { _lock.Release(); }
    }

    public async Task<DynamicQr?> GetAsync(string slug, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return Find(slug) is { DeletedUtc: null } c ? Clone(c) : null; }
        finally { _lock.Release(); }
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
        await _lock.WaitAsync(ct);
        try
        {
            var c = Find(slug);
            if (c is null) return null;
            if (c.DeletedUtc is not null) return new DynamicQr { Slug = c.Slug, Active = false }; // old prints → home page
            if (c.Active)
            {
                var now = _clock.GetUtcNow();
                var local = TimeZoneInfo.ConvertTime(now, Pacific);
                var day = local.ToString("yyyy-MM-dd");
                if (c.Weekday is not { Length: 7 }) c.Weekday = new int[7];
                if (c.Hour is not { Length: 24 }) c.Hour = new int[24];
                c.Weekday[(int)local.DayOfWeek]++;
                c.Hour[local.Hour]++;
                var device = DeviceOf(userAgent);
                c.Devices[device] = c.Devices.GetValueOrDefault(device) + 1;
                c.Scans++;
                c.LastScanUtc = now;
                c.Daily[day] = c.Daily.GetValueOrDefault(day) + 1;
                while (c.Daily.Count > 90) c.Daily.Remove(c.Daily.Keys.First());
                Save();
            }
            return Clone(c);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> CreateAsync(DynamicQr code, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (Find(code.Slug) is not null) return false;
            code.CreatedUtc = code.UpdatedUtc = _clock.GetUtcNow();
            _codes.Add(code);
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> UpdateAsync(string slug, Action<DynamicQr> change, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var c = Find(slug);
            if (c is null || c.DeletedUtc is not null) return false;
            change(c);
            c.Slug = slug; // the printed address never changes
            c.UpdatedUtc = _clock.GetUtcNow();
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string slug, string? by, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var c = Find(slug);
            if (c is null || c.DeletedUtc is not null) return false;
            c.DeletedUtc = c.UpdatedUtc = _clock.GetUtcNow();
            c.DeletedBy = by;
            c.Active = false;
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> IsTakenAsync(string slug, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return Find(slug) is not null; }
        finally { _lock.Release(); }
    }

    private DynamicQr? Find(string slug) => _codes.FirstOrDefault(c => c.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));

    private void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_codes, JsonDefaults.Options));
        File.Move(tmp, _path, overwrite: true);
    }

    private static DynamicQr? Clone(DynamicQr? c) =>
        c is null ? null : JsonSerializer.Deserialize<DynamicQr>(JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options);
}
