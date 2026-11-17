using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

/// <summary>
/// One stretch of time someone worked. A clocked-in session has no end yet (<see cref="EndUtc"/> null);
/// everyone has at most one of those. Manual entries and lead fixes are the same rows with a different <see cref="Source"/>.
/// </summary>
public sealed class HoursEntry
{
    public long Id { get; set; }
    public string Email { get; set; } = "";
    /// <summary>Team id (any level of the tree). The organization is the team's root.</summary>
    public string Team { get; set; } = "";
    public string Category { get; set; } = "";
    public DateTimeOffset StartUtc { get; set; }
    public DateTimeOffset? EndUtc { get; set; }
    public string? Note { get; set; }
    /// <summary>"clock" (clocked in/out), "manual" (added by hand), "lead" (added or fixed by a lead).</summary>
    public string Source { get; set; } = "clock";
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public string? UpdatedBy { get; set; }

    public bool Running => EndUtc is null;
    public TimeSpan Duration(DateTimeOffset now) => (EndUtc ?? now) - StartUtc;
    public double Hours(DateTimeOffset now) => Math.Max(0, Duration(now).TotalHours);
}

public sealed record HoursSettings(string Org, bool Leaderboard, int SeasonMonth, int SeasonDay)
{
    /// <summary>The season containing <paramref name="day"/>: e.g. Aug 1 2026 – Jul 31 2027, "2026–27".</summary>
    public (DateOnly Start, DateOnly EndExclusive, string Label) SeasonOf(DateOnly day)
    {
        var start = new DateOnly(day.Year, SeasonMonth, Math.Min(SeasonDay, DateTime.DaysInMonth(day.Year, SeasonMonth)));
        if (day < start) start = start.AddYears(-1);
        var end = start.AddYears(1);
        var label = start.Month == 1 && start.Day == 1 ? $"{start.Year}" : $"{start.Year}–{end.Year % 100:00}";
        return (start, end, label);
    }
}

/// <summary>
/// Hours in SQLite. Clock in/out, manual entries, lead fixes, categories and per-organization settings.
/// Who may see or change what is decided by the pages with <see cref="Access"/> (members: their own; leads: their teams).
/// </summary>
public sealed class HoursStore(SiteDb db, TimeProvider clock)
{
    public static readonly string[] DefaultCategories =
        ["Build", "Programming", "CAD / Design", "Electrical", "Outreach", "Meetings", "Fundraising", "Competition", "Training", "Other"];

    /// <summary>Longest a single entry may be. A clocked-in session past this shows up as a warning for leads.</summary>
    public static readonly TimeSpan LongSession = TimeSpan.FromHours(24);

    private const string Cols = "id, email, team, category, start_utc, end_utc, note, source, created_utc, updated_utc, updated_by";

    private static HoursEntry Read(SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0), Email = r.GetString(1), Team = r.GetString(2), Category = r.GetString(3),
        StartUtc = SiteDb.Time(r.GetString(4)), EndUtc = SiteDb.TimeOrNull(r, 5), Note = SiteDb.StrOrNull(r, 6),
        Source = r.GetString(7), CreatedUtc = SiteDb.Time(r.GetString(8)), UpdatedUtc = SiteDb.Time(r.GetString(9)), UpdatedBy = SiteDb.StrOrNull(r, 10),
    };

    private static List<HoursEntry> Query(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        var list = new List<HoursEntry>();
        using var cmd = SiteDb.Cmd(c, sql, null, args);
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public DateTimeOffset Now => clock.GetUtcNow();

    public HoursEntry? Running(string email)
    {
        using var c = db.Open();
        return Query(c, $"SELECT {Cols} FROM hours WHERE email = $e AND end_utc IS NULL", ("$e", email)).FirstOrDefault();
    }

    public HoursEntry? Get(long id)
    {
        using var c = db.Open();
        return Query(c, $"SELECT {Cols} FROM hours WHERE id = $id", ("$id", id)).FirstOrDefault();
    }

    /// <summary>Starts a session. Null if they're already clocked in (two taps on two phones can't make two sessions).</summary>
    public HoursEntry? ClockIn(string email, string team, string category)
    {
        using var c = db.Open();
        var now = SiteDb.Iso(Now);
        try
        {
            using var cmd = SiteDb.Cmd(c, $"INSERT INTO hours (email, team, category, start_utc, source, created_utc, updated_utc, updated_by) VALUES ($e,$t,$c,$now,'clock',$now,$now,$e) RETURNING {Cols}",
                null, ("$e", email), ("$t", team), ("$c", category), ("$now", now));
            using var r = cmd.ExecuteReader();
            return r.Read() ? Read(r) : null;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return null; } // UNIQUE: already running
    }

    /// <summary>Ends their running session now. Null if they weren't clocked in.</summary>
    public HoursEntry? ClockOut(string email, string? note)
    {
        using var c = db.Open();
        var now = SiteDb.Iso(Now);
        using var cmd = SiteDb.Cmd(c, $"UPDATE hours SET end_utc = $now, note = COALESCE($note, note), updated_utc = $now, updated_by = $e WHERE email = $e AND end_utc IS NULL RETURNING {Cols}",
            null, ("$e", email), ("$now", now), ("$note", string.IsNullOrWhiteSpace(note) ? null : note.Trim()));
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    public HoursEntry Add(HoursEntry e, string by)
    {
        using var c = db.Open();
        var now = SiteDb.Iso(Now);
        using var cmd = SiteDb.Cmd(c, $"INSERT INTO hours (email, team, category, start_utc, end_utc, note, source, created_utc, updated_utc, updated_by) VALUES ($e,$t,$c,$s,$end,$n,$src,$now,$now,$by) RETURNING {Cols}",
            null, ("$e", e.Email), ("$t", e.Team), ("$c", e.Category), ("$s", SiteDb.Iso(e.StartUtc)), ("$end", SiteDb.Iso(e.EndUtc)),
            ("$n", e.Note), ("$src", e.Source), ("$now", now), ("$by", by));
        using var r = cmd.ExecuteReader();
        r.Read();
        return Read(r);
    }

    /// <summary>Saves team, category, times and note. False if it's gone, or if it would make a second running session.</summary>
    public bool Update(HoursEntry e, string by)
    {
        using var c = db.Open();
        try
        {
            using var cmd = SiteDb.Cmd(c, "UPDATE hours SET team=$t, category=$c, start_utc=$s, end_utc=$end, note=$n, updated_utc=$now, updated_by=$by WHERE id=$id",
                null, ("$t", e.Team), ("$c", e.Category), ("$s", SiteDb.Iso(e.StartUtc)), ("$end", SiteDb.Iso(e.EndUtc)), ("$n", e.Note),
                ("$now", SiteDb.Iso(Now)), ("$by", by), ("$id", e.Id));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return false; }
    }

    public bool Delete(long id)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "DELETE FROM hours WHERE id = $id", null, ("$id", id));
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>
    /// Entries that overlap [from, to): by person (emails) and/or by team. Null filters mean "any".
    /// Newest first. Running sessions are included.
    /// </summary>
    public List<HoursEntry> List(IReadOnlyCollection<string>? emails = null, IReadOnlyCollection<string>? teams = null,
        DateTimeOffset? from = null, DateTimeOffset? to = null, int limit = 5000)
    {
        using var c = db.Open();
        var where = new List<string> { "1=1" };
        var args = new List<(string, object?)>();
        if (emails is not null)
        {
            if (emails.Count == 0) return [];
            where.Add($"email IN ({string.Join(",", emails.Select((_, i) => "$e" + i))})");
            args.AddRange(emails.Select((e, i) => ("$e" + i, (object?)e)));
        }
        if (teams is not null)
        {
            if (teams.Count == 0) return [];
            where.Add($"team IN ({string.Join(",", teams.Select((_, i) => "$t" + i))})");
            args.AddRange(teams.Select((t, i) => ("$t" + i, (object?)t)));
        }
        if (from is { } f) { where.Add("(end_utc IS NULL OR end_utc > $from)"); args.Add(("$from", SiteDb.Iso(f))); }
        if (to is { } t2) { where.Add("start_utc < $to"); args.Add(("$to", SiteDb.Iso(t2))); }
        args.Add(("$limit", limit));
        return Query(c, $"SELECT {Cols} FROM hours WHERE {string.Join(" AND ", where)} ORDER BY start_utc DESC LIMIT $limit", [.. args]);
    }

    /// <summary>Clocked-in sessions older than <see cref="LongSession"/>, oldest first (forgot to clock out?).</summary>
    public List<HoursEntry> LongRunning(IReadOnlyCollection<string>? teams)
    {
        using var c = db.Open();
        var cutoff = SiteDb.Iso(Now - LongSession);
        var all = Query(c, $"SELECT {Cols} FROM hours WHERE end_utc IS NULL AND start_utc < $cut ORDER BY start_utc", ("$cut", cutoff));
        return teams is null ? all : all.Where(e => teams.Contains(e.Team)).ToList();
    }

    /// <summary>What they used last, so the next clock-in is already set up.</summary>
    public (string Team, string Category)? LastUsed(string email)
    {
        using var c = db.Open();
        return Query(c, $"SELECT {Cols} FROM hours WHERE email = $e AND category <> '' ORDER BY created_utc DESC LIMIT 1", ("$e", email)).FirstOrDefault() is { } last
            ? (last.Team, last.Category) : null;
    }

    // ---------- Per-organization settings and categories ----------

    public HoursSettings Settings(string org)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "SELECT leaderboard, season_month, season_day FROM hours_settings WHERE org = $o", null, ("$o", org));
        using var r = cmd.ExecuteReader();
        return r.Read() ? new HoursSettings(org, r.GetInt64(0) != 0, (int)r.GetInt64(1), (int)r.GetInt64(2)) : new HoursSettings(org, false, 8, 1);
    }

    public void SaveSettings(HoursSettings s)
    {
        using var c = db.Open();
        SiteDb.Exec(c, """
            INSERT INTO hours_settings (org, leaderboard, season_month, season_day) VALUES ($o,$l,$m,$d)
            ON CONFLICT(org) DO UPDATE SET leaderboard = excluded.leaderboard, season_month = excluded.season_month, season_day = excluded.season_day
            """, null, ("$o", s.Org), ("$l", s.Leaderboard ? 1 : 0), ("$m", s.SeasonMonth), ("$d", s.SeasonDay));
    }

    public List<string> Categories(string org)
    {
        using var c = db.Open();
        var list = new List<string>();
        using var cmd = SiteDb.Cmd(c, "SELECT name FROM hours_categories WHERE org = $o ORDER BY sort", null, ("$o", org));
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(r.GetString(0));
        return list.Count > 0 ? list : [.. DefaultCategories];
    }

    public void SaveCategories(string org, IEnumerable<string> names)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        SiteDb.Exec(c, "DELETE FROM hours_categories WHERE org = $o", tx, ("$o", org));
        var i = 0;
        foreach (var n in names.Select(n => n.Trim()).Where(n => n.Length is > 0 and <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(40))
            SiteDb.Exec(c, "INSERT INTO hours_categories (org, name, sort) VALUES ($o,$n,$s)", tx, ("$o", org), ("$n", n), ("$s", i++));
        tx.Commit();
    }
}

/// <summary>Formatting and time helpers shared by the hours pages.</summary>
public static class HoursText
{
    /// <summary>Quick clock-ins (from the header) start without a reason; this is what shows until one is picked.</summary>
    public const string NoReason = "No reason yet";
    public static string Cat(string category) => string.IsNullOrEmpty(category) ? NoReason : category;

    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        var h = (int)t.TotalHours; var m = t.Minutes;
        return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
    }

    /// <summary>"1:02:37": hours, minutes and seconds (for the running timer and entry lengths).</summary>
    public static string Clock(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}";
    }

    public static string Hours(double h) => h.ToString(h < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Pacific wall clock → UTC (for times people type in).</summary>
    public static DateTimeOffset FromPacific(DateTime local) =>
        new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Pacific.Zone.GetUtcOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified))).ToUniversalTime();

    public static DateTimeOffset StartOfDayPacific(DateOnly d) => FromPacific(d.ToDateTime(TimeOnly.MinValue));

    /// <summary>Monday of the week containing <paramref name="d"/>.</summary>
    public static DateOnly WeekStart(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}

/// <summary>Named date ranges for hours pages ("week", "month", "season"…), in Pacific time.</summary>
public static class HoursRanges
{
    public static readonly (string Key, string Label)[] All =
        [("season", "This season"), ("week", "This week"), ("lastweek", "Last week"), ("month", "This month"), ("lastmonth", "Last month"), ("lastseason", "Last season"), ("all", "All time")];

    public static (DateTimeOffset? From, DateTimeOffset? To, string Label) Resolve(string? key, HoursSettings s, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.ToPacific().DateTime);
        DateTimeOffset At(DateOnly d) => HoursText.StartOfDayPacific(d);
        var week = HoursText.WeekStart(today);
        var month = new DateOnly(today.Year, today.Month, 1);
        var season = s.SeasonOf(today);
        var last = s.SeasonOf(season.Start.AddDays(-1));
        return key switch
        {
            "week" => (At(week), null, "This week"),
            "lastweek" => (At(week.AddDays(-7)), At(week), "Last week"),
            "month" => (At(month), null, today.ToString("MMMM yyyy")),
            "lastmonth" => (At(month.AddMonths(-1)), At(month), month.AddMonths(-1).ToString("MMMM yyyy")),
            "lastseason" => (At(last.Start), At(last.EndExclusive), $"{last.Label} season"),
            "all" => (null, null, "All time"),
            _ => (At(season.Start), At(season.EndExclusive), $"{season.Label} season"),
        };
    }

    /// <summary>Hours of an entry inside [from, to). A running session counts up to now (at most 24 h).</summary>
    public static double Clip(HoursEntry e, DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)
    {
        var end = e.EndUtc ?? (now < e.StartUtc + HoursStore.LongSession ? now : e.StartUtc + HoursStore.LongSession);
        var s = from is { } f && e.StartUtc < f ? f : e.StartUtc;
        var x = to is { } t && end > t ? t : end;
        return Math.Max(0, (x - s).TotalHours);
    }
}
