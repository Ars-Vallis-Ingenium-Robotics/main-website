using System.Globalization;
using System.Text.RegularExpressions;
using AviSite.Web.Endpoints;
using AviSite.Web.Store;

namespace AviSite.Web.Discord;

/// <summary>What the bot posts in channels: the clock panel and the weekly team summary. Totals only, never names.</summary>
public sealed partial class DiscordPosts(DiscordApi api, DiscordBotStore bot, HoursStore hours, IAccountStore accounts)
{
    /// <summary>
    /// The organization's leaderboard for a server, or null if the organization hasn't turned one on.
    /// Same rows as the site's leaderboard on /admin/hours.
    /// </summary>
    public async Task<Leaderboard?> LeaderboardForAsync(string team, CancellationToken ct = default)
    {
        var tree = await accounts.GroupsAsync(ct);
        var people = (await accounts.ListAsync(ct)).ToDictionary(a => a.Email);
        return HoursRules.Leaderboard(HoursRules.OrgOf(team, tree), hours, tree, people);
    }

    /// <summary>"AVI Robotics leaderboard · 2026–27" and the top <paramref name="top"/>. Names only (never emails), no pings.</summary>
    public static string LeaderboardText(Leaderboard lb, int top = 10, string? me = null)
    {
        var lines = new List<string> { $"**{Escape(lb.OrgName)} leaderboard · {lb.Season}**" };
        if (lb.Rows.Count == 0) lines.Add("No hours logged this season yet.");
        foreach (var (row, i) in lb.Rows.Take(top).Select((r, i) => (r, i)))
        {
            var line = $"{i + 1}. {NameOf(row)} · {HoursText.Hours(row.Hours)} h";
            lines.Add(row.Email == me ? $"**{line}** ← you" : line);
        }
        if (me is not null && lb.RankOf(me) is { } rank && rank > top)
            lines.Add($"You're #{rank} with {HoursText.Hours(lb.Rows[rank - 1].Hours)} h.");
        return string.Join('\n', lines);
    }

    /// <summary>A display name safe to show in a Discord channel: never an email address, markdown escaped.</summary>
    private static string NameOf(LeaderboardRow row) =>
        row.Name is { } n && !n.Contains('@') ? Escape(n) : "AVI member";

    [GeneratedRegex(@"([\\*_~`|>\[\]])")]
    private static partial Regex MarkdownChars();
    private static string Escape(string s) => MarkdownChars().Replace(s, "\\$1");

    public async Task<bool> PostLeaderboardAsync(DiscordGuild g, CancellationToken ct = default)
    {
        if (g.UpdatesChannel is not { } channel) return false;
        if (await LeaderboardForAsync(g.Team, ct) is not { } lb) return false;
        var id = await api.PostAsync(channel, LeaderboardText(lb), null, ct);
        bot.Record("post:leaderboard", id is null ? "error" : "ok", g.GuildId);
        return id is not null;
    }

    /// <summary>Posts a fresh clock panel and removes the previous one, so the channel keeps a single set of buttons.</summary>
    public async Task<bool> PostClockPanelAsync(DiscordGuild g, string? heading = null, CancellationToken ct = default)
    {
        if (g.PanelChannel is not { } channel) return false;
        var tree = await accounts.GroupsAsync(ct);
        var title = heading ?? $"**{tree.Name(g.Team)} · clock in**";
        var content = $"{title}\nTap **Clock in** when you arrive and **Clock out** when you leave. Replies are only visible to you. " +
            "First time? Link Discord from the AVI account menu (Discord link).";
        var id = await api.PostAsync(channel, content, DiscordCommands.ClockPanelButtons(), ct);
        if (id is null) { bot.Record("post:panel", "error", g.GuildId); return false; }
        if (g.PanelMessageId is { } old && old != id) await api.DeleteAsync(channel, old, ct); // the old panel may already be gone; that's fine
        bot.SetPanelMessage(g.GuildId, id);
        bot.Record("post:panel", "ok", g.GuildId);
        return true;
    }

    /// <summary>The week that started on <paramref name="weekStart"/> (a Monday, Pacific), for the server's team.</summary>
    public async Task<bool> PostWeeklySummaryAsync(DiscordGuild g, DateOnly weekStart, CancellationToken ct = default)
    {
        if (g.UpdatesChannel is not { } channel) return false;
        var tree = await accounts.GroupsAsync(ct);
        var from = HoursText.StartOfDayPacific(weekStart);
        var to = HoursText.StartOfDayPacific(weekStart.AddDays(7));
        var scope = tree.WithDescendants([g.Team]);
        var entries = hours.List(teams: scope, from: from, to: to);
        var now = hours.Now;
        var total = entries.Sum(e => HoursRanges.Clip(e, from, to, now));
        var people = entries.Where(e => HoursRanges.Clip(e, from, to, now) > 0).Select(e => e.Email).Distinct().Count();
        var week = weekStart.ToString("MMM d", CultureInfo.InvariantCulture);

        var lines = new List<string> { $"**{tree.Name(g.Team)} · week of {week}: {HoursText.Hours(total)} h** from {people} {(people == 1 ? "person" : "people")}" };
        // One line per team directly under the server's team, biggest first.
        var byChild = tree.Children(g.Team)
            .Select(child => (child.Name, Hours: entries.Where(e => tree.WithDescendants([child.Id]).Contains(e.Team)).Sum(e => HoursRanges.Clip(e, from, to, now))))
            .Where(x => x.Hours > 0).OrderByDescending(x => x.Hours).Take(10);
        lines.AddRange(byChild.Select(x => $"• {x.Name}: {HoursText.Hours(x.Hours)} h"));

        var id = await api.PostAsync(channel, string.Join('\n', lines), null, ct);
        bot.Record("post:summary", id is null ? "error" : "ok", g.GuildId);
        return id is not null;
    }
}

/// <summary>
/// Once a minute: Monday-morning summaries and the pre-meeting clock panel for every server that wants them.
/// Idle unless a bot token is configured, and while the bot is paused.
/// </summary>
public sealed class DiscordScheduler(DiscordApi api, DiscordBotStore bot, DiscordPosts posts, ILogger<DiscordScheduler> log) : BackgroundService
{
    /// <summary>Summaries go out Monday from 9:00 Pacific.</summary>
    public static readonly TimeOnly SummaryTime = new(9, 0);
    /// <summary>The meeting panel goes up this long before the meeting starts (and still goes up if the site was down then, until the meeting is 2 h in).</summary>
    public static readonly TimeSpan PanelLead = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan PanelLate = TimeSpan.FromHours(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await TickAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("Discord scheduler tick failed ({ExceptionType})", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (!api.Configured || bot.State().Paused) return;
        var now = bot.Now;
        var local = now.ToPacific();
        var today = DateOnly.FromDateTime(local.DateTime);

        foreach (var g in bot.List())
        {
            if ((g.WeeklySummary || g.WeeklyLeaderboard) && g.UpdatesChannel is not null
                && local.DayOfWeek == DayOfWeek.Monday && TimeOnly.FromDateTime(local.DateTime) >= SummaryTime)
            {
                var lastWeek = HoursText.WeekStart(today).AddDays(-7);
                if (bot.TryClaim(g.GuildId, DiscordBotStore.SummaryKey, lastWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                {
                    if (g.WeeklySummary) await posts.PostWeeklySummaryAsync(g, lastWeek, ct);
                    if (g.WeeklyLeaderboard) await posts.PostLeaderboardAsync(g, ct);
                }
            }

            if (g.PanelChannel is not null && g.MeetingDay == local.DayOfWeek && g.MeetingTime is { } time)
            {
                var start = HoursText.FromPacific(today.ToDateTime(time));
                if (now >= start - PanelLead && now < start + PanelLate
                    && bot.TryClaim(g.GuildId, DiscordBotStore.PanelKey, today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))
                {
                    var when = start.ToPacific().ToString("dddd h:mm tt", CultureInfo.InvariantCulture);
                    await posts.PostClockPanelAsync(g, $"**Meeting · {when} · clock in**", ct);
                }
            }
        }
    }
}
