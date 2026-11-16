using System.Text.Json;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>An event on /countdown, managed by officers in /admin/countdowns.</summary>
public sealed class CountdownEvent
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>competition | deadline | testing | fundraiser | worlds | outreach | meeting | event</summary>
    public string Type { get; set; } = "event";
    /// <summary>avi | sms | null</summary>
    public string? Team { get; set; }
    /// <summary>Wall-clock times in <see cref="TimeZone"/>.</summary>
    public DateTime StartLocal { get; set; }
    public DateTime? EndLocal { get; set; }
    public string TimeZone { get; set; } = CountdownZones.Default;
    public string? Where { get; set; }
    public string? LinkUrl { get; set; }
    public string? LinkLabel { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public DateTimeOffset Start => CountdownZones.ToInstant(StartLocal, TimeZone);
    [System.Text.Json.Serialization.JsonIgnore] public DateTimeOffset? End => EndLocal is { } e ? CountdownZones.ToInstant(e, TimeZone) : null;
}

public static class CountdownZones
{
    public const string Default = "America/Los_Angeles";

    public static readonly (string Id, string Label)[] All =
    [
        ("America/Los_Angeles", "Pacific"), ("America/Denver", "Mountain"), ("America/Phoenix", "Arizona"),
        ("America/Chicago", "Central"), ("America/New_York", "Eastern"), ("America/Halifax", "Atlantic"),
        ("America/St_Johns", "Newfoundland"), ("Pacific/Honolulu", "Hawaii"), ("Etc/UTC", "UTC"),
    ];

    public static readonly (string Id, string Label)[] Types =
    [
        ("competition", "Competition"), ("deadline", "Deadline"), ("testing", "Testing"), ("fundraiser", "Fundraiser"),
        ("worlds", "Worlds"), ("outreach", "Outreach"), ("meeting", "Meeting"), ("event", "Event"),
    ];

    public static string Label(string zone) => All.FirstOrDefault(z => z.Id == zone).Label ?? zone;

    public static DateTimeOffset ToInstant(DateTime local, string zone)
    {
        var tz = Find(zone);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, tz.GetUtcOffset(unspecified));
    }

    private static TimeZoneInfo Find(string zone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById(Default); }
    }
}

public interface ICountdownStore
{
    Task<IReadOnlyList<CountdownEvent>> ListAsync(CancellationToken ct = default);
    Task<CountdownEvent?> GetAsync(string id, CancellationToken ct = default);
    Task<CountdownEvent> SaveAsync(CountdownEvent e, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}

/// <summary>Same draft storage as orders: App_Data/countdowns.json, atomic rewrite.</summary>
public sealed class JsonCountdownStore : ICountdownStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<CountdownEvent> _events;
    private readonly TimeProvider _clock;

    public JsonCountdownStore(IOptions<StoreOptions> options, IWebHostEnvironment env, TimeProvider clock)
    {
        _clock = clock;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "countdowns.json");
        var exists = File.Exists(_path);
        _events = exists
            ? JsonSerializer.Deserialize<List<CountdownEvent>>(File.ReadAllText(_path), JsonDefaults.Options) ?? []
            : Seed();
        if (!exists) Save();
    }

    /// <summary>Last season's events from the old /countdown page, so "Done" isn't empty on day one.</summary>
    private static List<CountdownEvent> Seed()
    {
        CountdownEvent E(string title, string type, string? team, string start, string? end, string? where, string? url = null, string? label = null, string zone = CountdownZones.Default) => new()
        {
            Id = NewId(), Title = title, Type = type, Team = team, StartLocal = DateTime.Parse(start, System.Globalization.CultureInfo.InvariantCulture), EndLocal = end is null ? null : DateTime.Parse(end, System.Globalization.CultureInfo.InvariantCulture),
            TimeZone = zone, Where = where, LinkUrl = url, LinkLabel = label,
        };
        return
        [
            E("Water Testing", "testing", "sms", "2026-04-04 11:00", "2026-04-04 23:00", "Fresno, CA"),
            E("Panda Express Fundraiser", "fundraiser", null, "2026-04-09 08:00", "2026-04-09 17:00", "Panda Express, University Student Union, Fresno State", "/Files/2026-04-09-panda-fundraiser.pdf", "Flyer (PDF)"),
            E("Water Testing", "testing", "sms", "2026-04-16 17:30", "2026-04-16 22:00", "Fresno, CA"),
            E("Vintage Days", "fundraiser", null, "2026-04-17 10:00", "2026-04-19 17:00", "Fresno State", "https://studentaffairs.fresnostate.edu/studentinvolvement/traditions/vintagedays/", "Event info"),
            E("Monterey Regional", "competition", "sms", "2026-04-25 09:00", "2026-04-25 17:00", "Watsonville, CA"),
            E("Tech Doc Submissions", "deadline", "sms", "2026-05-21 03:00", null, "Online", zone: "Pacific/Honolulu"),
            E("MATE ROV World Championship", "worlds", "sms", "2026-06-23 08:00", "2026-06-28 18:00", "St. John's, Newfoundland, Canada", zone: "America/St_Johns"),
        ];
    }

    public static string NewId() => Guid.NewGuid().ToString("N")[..10];

    public async Task<IReadOnlyList<CountdownEvent>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _events.Select(Clone).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<CountdownEvent?> GetAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _events.FirstOrDefault(e => e.Id == id) is { } e ? Clone(e) : null; }
        finally { _lock.Release(); }
    }

    public async Task<CountdownEvent> SaveAsync(CountdownEvent e, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (string.IsNullOrEmpty(e.Id)) e.Id = NewId();
            e.UpdatedUtc = _clock.GetUtcNow();
            var i = _events.FindIndex(x => x.Id == e.Id);
            if (i >= 0) _events[i] = Clone(e); else _events.Add(Clone(e));
            Save();
            return Clone(e);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var removed = _events.RemoveAll(e => e.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
        finally { _lock.Release(); }
    }

    private void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_events, JsonDefaults.Options));
        File.Move(tmp, _path, overwrite: true);
    }

    private static CountdownEvent Clone(CountdownEvent e) =>
        JsonSerializer.Deserialize<CountdownEvent>(JsonSerializer.Serialize(e, JsonDefaults.Options), JsonDefaults.Options)!;
}
