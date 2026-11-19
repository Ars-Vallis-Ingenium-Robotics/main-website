using System.Globalization;
using System.Text.RegularExpressions;
using AviSite.Web.Options;
using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

/// <summary>
/// One Discord server the bot answers in. The site owner adds it and ties it to an AVI team or organization;
/// that team's leads (and the owner) choose its channels, weekly summary and meeting clock panel.
/// </summary>
public sealed class DiscordGuild
{
    public string GuildId { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>The AVI group this server belongs to. Its leads manage the server's settings.</summary>
    public string Team { get; set; } = "";
    /// <summary>Channels where slash commands answer. Empty: any channel.</summary>
    public List<string> CommandChannels { get; set; } = [];
    /// <summary>Where the weekly summary is posted.</summary>
    public string? UpdatesChannel { get; set; }
    public bool WeeklySummary { get; set; }
    /// <summary>Also post the organization's leaderboard on Mondays (only if the organization turned its leaderboard on).</summary>
    public bool WeeklyLeaderboard { get; set; }
    /// <summary>Where the clock-in panel (Clock in / Clock out buttons) is posted.</summary>
    public string? PanelChannel { get; set; }
    /// <summary>Weekly meeting (Pacific). With a panel channel, a fresh clock panel is posted 15 minutes before it.</summary>
    public DayOfWeek? MeetingDay { get; set; }
    public TimeOnly? MeetingTime { get; set; }
    public string? PanelMessageId { get; set; }
    public string? LastSummary { get; set; }
    public string? LastPanel { get; set; }
    public string AddedBy { get; set; } = "";
    public DateTimeOffset AddedUtc { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
}

public sealed record DiscordBotState(bool Paused, string? ChangedBy, DateTimeOffset? ChangedUtc);

public sealed record DiscordActivity(string Kind, string Outcome, int Count, DateTimeOffset Last);

/// <summary>Discord bot state in site.db, shared by every app instance (no in-memory copies).</summary>
public sealed class DiscordBotStore(SiteDb db, TimeProvider clock)
{
    /// <summary>Activity older than this is dropped.</summary>
    public static readonly TimeSpan ActivityKept = TimeSpan.FromDays(30);
    public const string SummaryKey = "last_summary";
    public const string PanelKey = "last_panel";

    private const string Cols = "guild_id, name, team, command_channels, updates_channel, weekly_summary, panel_channel, meeting_day, meeting_time, " +
        "panel_message_id, last_summary, last_panel, added_by, added_utc, updated_by, updated_utc, weekly_leaderboard";

    public DateTimeOffset Now => clock.GetUtcNow();

    private static DiscordGuild Read(SqliteDataReader r) => new()
    {
        GuildId = r.GetString(0), Name = r.GetString(1), Team = r.GetString(2),
        CommandChannels = DiscordBotRules.SplitIds(r.GetString(3)),
        UpdatesChannel = SiteDb.StrOrNull(r, 4), WeeklySummary = r.GetInt64(5) != 0, PanelChannel = SiteDb.StrOrNull(r, 6),
        MeetingDay = r.IsDBNull(7) ? null : (DayOfWeek)(int)r.GetInt64(7),
        MeetingTime = SiteDb.StrOrNull(r, 8) is { } t && TimeOnly.TryParseExact(t, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null,
        PanelMessageId = SiteDb.StrOrNull(r, 9), LastSummary = SiteDb.StrOrNull(r, 10), LastPanel = SiteDb.StrOrNull(r, 11),
        AddedBy = r.GetString(12), AddedUtc = SiteDb.Time(r.GetString(13)), UpdatedBy = SiteDb.StrOrNull(r, 14), UpdatedUtc = SiteDb.TimeOrNull(r, 15),
        WeeklyLeaderboard = r.GetInt64(16) != 0,
    };

    // ---------- Pause ----------

    public DiscordBotState State()
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "SELECT paused, changed_by, changed_utc FROM discord_bot WHERE id = 1");
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DiscordBotState(r.GetInt64(0) != 0, SiteDb.StrOrNull(r, 1), SiteDb.TimeOrNull(r, 2)) : new DiscordBotState(false, null, null);
    }

    public void SetPaused(bool paused, string by)
    {
        using var c = db.Open();
        SiteDb.Exec(c, "UPDATE discord_bot SET paused = $p, changed_by = $by, changed_utc = $now WHERE id = 1", null,
            ("$p", paused ? 1 : 0), ("$by", by), ("$now", SiteDb.Iso(Now)));
    }

    // ---------- Servers ----------

    public List<DiscordGuild> List()
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, $"SELECT {Cols} FROM discord_guilds ORDER BY name COLLATE NOCASE");
        using var r = cmd.ExecuteReader();
        var list = new List<DiscordGuild>();
        while (r.Read()) list.Add(Read(r));
        return list;
    }

    public DiscordGuild? Get(string? guildId)
    {
        if (guildId is null) return null;
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, $"SELECT {Cols} FROM discord_guilds WHERE guild_id = $id", null, ("$id", guildId));
        using var r = cmd.ExecuteReader();
        return r.Read() ? Read(r) : null;
    }

    /// <summary>True once the owner has added any server: from then on only listed servers get answers.</summary>
    public bool AnyServers()
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "SELECT EXISTS (SELECT 1 FROM discord_guilds)");
        return (long)cmd.ExecuteScalar()! != 0;
    }

    /// <summary>Adds a server, or changes its name and team if it's already there.</summary>
    public void Upsert(string guildId, string name, string team, string by)
    {
        using var c = db.Open();
        var now = SiteDb.Iso(Now);
        SiteDb.Exec(c, """
            INSERT INTO discord_guilds (guild_id, name, team, added_by, added_utc) VALUES ($id, $name, $team, $by, $now)
            ON CONFLICT(guild_id) DO UPDATE SET name = excluded.name, team = excluded.team, updated_by = $by, updated_utc = $now
            """, null, ("$id", guildId), ("$name", name), ("$team", team), ("$by", by), ("$now", now));
    }

    public bool Remove(string guildId)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "DELETE FROM discord_guilds WHERE guild_id = $id", null, ("$id", guildId));
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>The settings a server's managers change: channels, weekly summary, meeting panel.</summary>
    public bool SaveSettings(DiscordGuild g, string by)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, """
            UPDATE discord_guilds SET command_channels = $cmd, updates_channel = $upd, weekly_summary = $weekly, weekly_leaderboard = $board, panel_channel = $panel,
                meeting_day = $day, meeting_time = $time, updated_by = $by, updated_utc = $now
            WHERE guild_id = $id
            """, null, ("$id", g.GuildId), ("$cmd", string.Join(',', g.CommandChannels)), ("$upd", g.UpdatesChannel),
            ("$weekly", g.WeeklySummary ? 1 : 0), ("$board", g.WeeklyLeaderboard ? 1 : 0), ("$panel", g.PanelChannel), ("$day", g.MeetingDay is { } d ? (int)d : null),
            ("$time", g.MeetingTime?.ToString("HH:mm", CultureInfo.InvariantCulture)), ("$by", by), ("$now", SiteDb.Iso(Now)));
        return cmd.ExecuteNonQuery() > 0;
    }

    public void SetPanelMessage(string guildId, string? messageId)
    {
        using var c = db.Open();
        SiteDb.Exec(c, "UPDATE discord_guilds SET panel_message_id = $m WHERE guild_id = $id", null, ("$id", guildId), ("$m", messageId));
    }

    /// <summary>
    /// Marks a scheduled post as done for <paramref name="key"/> (a week or a date). Only the first caller gets true,
    /// so two app instances (or two ticks) never post the same summary or panel twice.
    /// </summary>
    public bool TryClaim(string guildId, string column, string key)
    {
        if (column is not (SummaryKey or PanelKey)) throw new ArgumentOutOfRangeException(nameof(column));
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, $"UPDATE discord_guilds SET {column} = $k WHERE guild_id = $id AND ({column} IS NULL OR {column} <> $k)",
            null, ("$id", guildId), ("$k", key));
        return cmd.ExecuteNonQuery() > 0;
    }

    // ---------- Activity (for the status page) ----------

    /// <summary>Logs what happened, never who: the command or button, the server, and how it ended.</summary>
    public void Record(string kind, string outcome, string? guildId)
    {
        try
        {
            using var c = db.Open();
            SiteDb.Exec(c, "INSERT INTO discord_events (at_utc, guild_id, kind, outcome) VALUES ($at, $g, $k, $o)", null,
                ("$at", SiteDb.Iso(Now)), ("$g", guildId), ("$k", kind.Length > 40 ? kind[..40] : kind), ("$o", outcome));
            if (Random.Shared.Next(50) == 0)
                SiteDb.Exec(c, "DELETE FROM discord_events WHERE at_utc < $cut", null, ("$cut", SiteDb.Iso(Now - ActivityKept)));
        }
        catch (SqliteException) { /* the status log must never break a reply */ }
    }

    public List<DiscordActivity> Activity(DateTimeOffset since, string? guildId = null)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, """
            SELECT kind, outcome, COUNT(*), MAX(at_utc) FROM discord_events
            WHERE at_utc >= $since AND ($g IS NULL OR guild_id = $g)
            GROUP BY kind, outcome ORDER BY COUNT(*) DESC
            """, null, ("$since", SiteDb.Iso(since)), ("$g", guildId));
        using var r = cmd.ExecuteReader();
        var list = new List<DiscordActivity>();
        while (r.Read()) list.Add(new DiscordActivity(r.GetString(0), r.GetString(1), (int)r.GetInt64(2), SiteDb.Time(r.GetString(3))));
        return list;
    }

    public int LinkedAccounts()
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "SELECT COUNT(*) FROM discord_links");
        return (int)(long)cmd.ExecuteScalar()!;
    }
}

/// <summary>Who may do what with the bot, and parsing for what people type. Built on <see cref="Access"/>.</summary>
public static partial class DiscordBotRules
{
    /// <summary>Pause, add/remove servers, register commands: the owner of the main site only.</summary>
    public static bool CanAdminister(Access a) => a.IsOwner;

    /// <summary>A server's channels and posts: the owner, and leads of the server's team (or a team above it). Officers aren't.</summary>
    public static bool CanManage(Access a, DiscordGuild g) => a.IsOwner || a.Leads.Contains(g.Team);

    /// <summary>Seeing a server's settings: its managers, plus officers (read only, any organization).</summary>
    public static bool CanView(Access a, DiscordGuild g) => CanManage(a, g) || a.IsOfficer;

    public static bool CanOpenPage(Access a, IEnumerable<DiscordGuild> servers) =>
        a.IsOwner || a.IsOfficer || (a.Leads.Count > 0 && servers.Any(g => CanManage(a, g)));

    [GeneratedRegex(@"\d{17,20}")]
    private static partial Regex Snowflakes();

    /// <summary>Channel or server IDs from what someone pasted: "123…", "&lt;#123…&gt;", or a Discord link. At most 25.</summary>
    public static List<string> ParseIds(string? text) =>
        string.IsNullOrWhiteSpace(text) ? [] : Snowflakes().Matches(text).Select(m => m.Value).Distinct().Take(25).ToList();

    /// <summary>One ID (the last one in a pasted channel link is the channel).</summary>
    public static string? ParseId(string? text) => ParseIds(text) is { Count: > 0 } ids ? ids[^1] : null;

    public static List<string> SplitIds(string stored) =>
        stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(DiscordOptions.IsSnowflake).ToList();

    public static string Mention(string channelId) => $"<#{channelId}>";
}
