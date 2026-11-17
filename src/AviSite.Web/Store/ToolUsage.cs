using System.Text.Json;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>
/// Anonymous tool counters ("qr.png" downloaded 3 times on 2026-11-16). No cookies, no IP, no user agent:
/// only the counter name and the Pacific day. Shown to officers at /admin/usage. Disclosed on /privacy.
/// </summary>
public static class ToolUsageEvents
{
    /// <summary>Every counter the site accepts, with its label on /admin/usage. Anything else is ignored.</summary>
    public static readonly (string Key, string Label)[] All =
    [
        ("tools.open", "Tools page opened"),
        ("qr.open", "QR maker opened"),
        ("qr.png", "QR: PNG downloads"),
        ("qr.svg", "QR: SVG downloads"),
        ("qr.jpg", "QR: JPG downloads"),
        ("qr.pdf", "QR: PDF downloads"),
        ("qr.copy", "QR: copied image"),
        ("qr.zip", "QR: batch zips"),
        ("qr.pdfsheet", "QR: batch PDF sheets"),
        ("qr.print", "QR: batch prints"),
        ("qr.share", "QR: look links shared"),
        ("countdown.open", "Countdowns opened"),
    ];

    public static bool IsKnown(string key) => All.Any(e => e.Key == key);

    /// <summary>Link-preview bots and crawlers shouldn't count as people opening a tool.</summary>
    public static bool LooksLikeBot(string? ua) =>
        string.IsNullOrEmpty(ua) || System.Text.RegularExpressions.Regex.IsMatch(ua,
            "bot|crawl|spider|slurp|preview|facebookexternalhit|Discord|Slack|WhatsApp|Telegram|curl|wget|python|HeadlessChrome",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

public interface IToolUsageStore
{
    Task CountAsync(string key, CancellationToken ct = default);
    /// <summary>key → (Pacific day yyyy-MM-dd → count), last 400 days.</summary>
    Task<IReadOnlyDictionary<string, SortedDictionary<string, int>>> ReadAsync(CancellationToken ct = default);
}

/// <summary>Same draft storage as orders: App_Data/tool-usage.json, atomic rewrite.</summary>
public sealed class JsonToolUsageStore : IToolUsageStore
{
    private static readonly TimeZoneInfo Pacific = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, SortedDictionary<string, int>> _counts;
    private readonly TimeProvider _clock;

    public JsonToolUsageStore(IOptions<StoreOptions> options, IWebHostEnvironment env, TimeProvider clock)
    {
        _clock = clock;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "tool-usage.json");
        _counts = File.Exists(_path)
            ? JsonSerializer.Deserialize<Dictionary<string, SortedDictionary<string, int>>>(File.ReadAllText(_path), JsonDefaults.Options) ?? []
            : [];
    }

    public async Task CountAsync(string key, CancellationToken ct = default)
    {
        if (!ToolUsageEvents.IsKnown(key)) return;
        await _lock.WaitAsync(ct);
        try
        {
            var day = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), Pacific).ToString("yyyy-MM-dd");
            if (!_counts.TryGetValue(key, out var days)) _counts[key] = days = [];
            days[day] = days.GetValueOrDefault(day) + 1;
            while (days.Count > 400) days.Remove(days.Keys.First());
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_counts, JsonDefaults.Options));
            File.Move(tmp, _path, overwrite: true);
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyDictionary<string, SortedDictionary<string, int>>> ReadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _counts.ToDictionary(kv => kv.Key, kv => new SortedDictionary<string, int>(kv.Value)); }
        finally { _lock.Release(); }
    }
}
