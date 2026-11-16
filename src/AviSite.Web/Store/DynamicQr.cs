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
    Task<DynamicQr?> ScanAsync(string slug, CancellationToken ct = default);
    Task<bool> CreateAsync(DynamicQr code, CancellationToken ct = default);
    Task<bool> UpdateAsync(string slug, Action<DynamicQr> change, CancellationToken ct = default);
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
        try { return _codes.Select(Clone).ToList()!; }
        finally { _lock.Release(); }
    }

    public async Task<DynamicQr?> GetAsync(string slug, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return Clone(Find(slug)); }
        finally { _lock.Release(); }
    }

    public async Task<DynamicQr?> ScanAsync(string slug, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var c = Find(slug);
            if (c is null) return null;
            if (c.Active)
            {
                var now = _clock.GetUtcNow();
                var day = TimeZoneInfo.ConvertTime(now, Pacific).ToString("yyyy-MM-dd");
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
            if (c is null) return false;
            change(c);
            c.Slug = slug; // the printed address never changes
            c.UpdatedUtc = _clock.GetUtcNow();
            Save();
            return true;
        }
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
