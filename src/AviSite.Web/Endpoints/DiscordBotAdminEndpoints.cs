using System.Globalization;
using AviSite.Web.Discord;
using AviSite.Web.Options;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Endpoints;

/// <summary>
/// The buttons on /admin/discord/bot. The site owner pauses the bot, lists the servers it may answer in and ties each to a
/// team; each server's managers (that team's leads, and the owner) set its channels, weekly summary and meeting clock panel.
/// Officers can look but not change. Every action lands back on the page with a short result code.
/// </summary>
public static class DiscordBotAdminEndpoints
{
    public const string Page = "/admin/discord/bot";

    public static void MapDiscordBotAdminEndpoints(this WebApplication app)
    {
        var bot = app.MapGroup(Page).RequireAuthorization(AdminEndpoints.QrPolicy);

        // Pause / resume. Discord's endpoint check (PING) is still answered while paused.
        bot.MapPost("/state", async ([FromForm] string action, HttpContext http, IAccountStore accounts, DiscordBotStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (!DiscordBotRules.CanAdminister(me) || me.Account is null) return Back(error: "denied");
            if (action is not ("pause" or "resume")) return Back(error: "action");
            store.SetPaused(action == "pause", me.Account.Email);
            return Back(done: action == "pause" ? "paused" : "resumed");
        });

        // Add a server (or rename it / move it to another team).
        bot.MapPost("/servers", async ([FromForm] string? guild, [FromForm] string? name, [FromForm] string? team,
            HttpContext http, IAccountStore accounts, DiscordBotStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (!DiscordBotRules.CanAdminister(me) || me.Account is null) return Back(error: "denied");
            // A pasted server link (discord.com/channels/SERVER/CHANNEL) has the server first.
            var id = DiscordBotRules.ParseIds(guild) is { Count: > 0 } ids ? ids[0] : null;
            if (id is null) return Back(error: "guild");
            if (team is null || me.Tree.Find(team) is null) return Back(error: "team");
            var label = string.IsNullOrWhiteSpace(name) ? me.Tree.Name(team) + " Discord" : name.Trim();
            if (label.Length > 80) label = label[..80];
            store.Upsert(id, label, team, me.Account.Email);
            return Back(done: "server", anchor: "server-" + id);
        });

        bot.MapPost("/servers/{id}/remove", async (string id, [FromForm] bool confirm, HttpContext http, IAccountStore accounts, DiscordBotStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (!DiscordBotRules.CanAdminister(me)) return Back(error: "denied");
            if (!confirm) return Back(error: "confirm", anchor: "server-" + id);
            return store.Remove(id) ? Back(done: "removed") : Back(error: "gone");
        });

        // A server's own settings: its managers.
        bot.MapPost("/servers/{id}/settings", async (string id, [FromForm] string? commandChannels, [FromForm] string? updatesChannel,
            [FromForm] bool? weeklySummary, [FromForm] bool? weeklyLeaderboard, [FromForm] string? panelChannel, [FromForm] string? meetingDay, [FromForm] string? meetingTime,
            HttpContext http, IAccountStore accounts, DiscordBotStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            var g = store.Get(id);
            if (g is null) return Back(error: "gone");
            if (!DiscordBotRules.CanManage(me, g) || me.Account is null) return Back(error: "denied", anchor: "server-" + id);

            g.CommandChannels = DiscordBotRules.ParseIds(commandChannels);
            g.UpdatesChannel = DiscordBotRules.ParseId(updatesChannel);
            g.WeeklySummary = weeklySummary == true && g.UpdatesChannel is not null;
            g.WeeklyLeaderboard = weeklyLeaderboard == true && g.UpdatesChannel is not null;
            g.PanelChannel = DiscordBotRules.ParseId(panelChannel);
            g.MeetingDay = int.TryParse(meetingDay, NumberStyles.None, CultureInfo.InvariantCulture, out var d) && d is >= 0 and <= 6 ? (DayOfWeek)d : null;
            g.MeetingTime = TimeOnly.TryParse(meetingTime, CultureInfo.InvariantCulture, out var t) ? new TimeOnly(t.Hour, t.Minute) : null;
            if ((g.MeetingDay is null) != (g.MeetingTime is null)) return Back(error: "meeting", anchor: "server-" + id);
            if (g.MeetingDay is not null && g.PanelChannel is null) return Back(error: "panel-channel", anchor: "server-" + id);
            if (!string.IsNullOrWhiteSpace(updatesChannel) && g.UpdatesChannel is null) return Back(error: "channel", anchor: "server-" + id);
            if (!string.IsNullOrWhiteSpace(panelChannel) && g.PanelChannel is null) return Back(error: "channel", anchor: "server-" + id);
            return store.SaveSettings(g, me.Account.Email) ? Back(done: "saved", anchor: "server-" + id) : Back(error: "gone");
        });

        // Post now: the clock panel, or last week's summary (to check the channel and permissions).
        bot.MapPost("/servers/{id}/post", async (string id, [FromForm] string what, HttpContext http, IAccountStore accounts,
            DiscordBotStore store, DiscordApi api, DiscordPosts posts) =>
        {
            var me = await http.AccessAsync(accounts);
            var g = store.Get(id);
            if (g is null) return Back(error: "gone");
            if (!DiscordBotRules.CanManage(me, g)) return Back(error: "denied", anchor: "server-" + id);
            if (!api.Configured) return Back(error: "no-token", anchor: "server-" + id);
            if (store.State().Paused) return Back(error: "paused", anchor: "server-" + id);
            var ok = what switch
            {
                "panel" => g.PanelChannel is not null && await posts.PostClockPanelAsync(g, null, http.RequestAborted),
                "summary" => g.UpdatesChannel is not null && await posts.PostWeeklySummaryAsync(g,
                    HoursText.WeekStart(DateOnly.FromDateTime(store.Now.ToPacific().DateTime)).AddDays(-7), http.RequestAborted),
                "leaderboard" => g.UpdatesChannel is not null && await posts.PostLeaderboardAsync(g, http.RequestAborted),
                _ => false,
            };
            return ok ? Back(done: "posted", anchor: "server-" + id) : Back(error: "post", anchor: "server-" + id);
        });

        // Register the slash commands (one server: shows up at once; global: can take a while). Owner only.
        bot.MapPost("/commands", async ([FromForm] string? scope, HttpContext http, IAccountStore accounts, DiscordApi api) =>
        {
            var me = await http.AccessAsync(accounts);
            if (!DiscordBotRules.CanAdminister(me)) return Back(error: "denied");
            if (!api.Configured) return Back(error: "no-token");
            var guild = scope == "global" ? null : DiscordBotRules.ParseId(scope);
            if (scope != "global" && guild is null) return Back(error: "guild");
            return await api.RegisterCommandsAsync(guild, http.RequestAborted) ? Back(done: "commands") : Back(error: "commands");
        });

        // Point Discord's Interactions Endpoint at this site (Discord checks it with a PING first). Owner only.
        bot.MapPost("/endpoint", async ([FromForm] bool confirm, HttpContext http, IAccountStore accounts, DiscordApi api, IOptions<DiscordOptions> options) =>
        {
            var me = await http.AccessAsync(accounts);
            if (!DiscordBotRules.CanAdminister(me)) return Back(error: "denied");
            if (!confirm) return Back(error: "confirm");
            if (!api.Configured || options.Value.PublicBaseUrl is not { Length: > 0 } baseUrl) return Back(error: "no-token");
            return await api.SetInteractionsUrlAsync(InteractionsUrl(baseUrl), http.RequestAborted) ? Back(done: "endpoint") : Back(error: "endpoint");
        });
    }

    public static string InteractionsUrl(string publicBaseUrl) => publicBaseUrl.TrimEnd('/') + "/discord/interactions";

    private static IResult Back(string? done = null, string? error = null, string? anchor = null) =>
        Results.LocalRedirect(Page + (done is not null ? "?done=" + done : error is not null ? "?error=" + error : "") + (anchor is null ? "" : "#" + anchor));
}
