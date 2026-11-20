using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Discord;

/// <summary>What Discord says about the application (owner status page).</summary>
public sealed record DiscordAppInfo(string Name, int? Servers, int? UserInstalls, string? InteractionsUrl);

/// <summary>
/// The few Discord REST calls the bot makes with its token: app status, command registration, posting and removing
/// its own messages. The token never leaves this class and is never logged; failures log only the HTTP status.
/// </summary>
public sealed class DiscordApi(IHttpClientFactory clients, IOptions<DiscordOptions> options, ILogger<DiscordApi> log)
{
    public const string ClientName = "discord-bot";
    private const string Api = "https://discord.com/api/v10";

    /// <summary>View Channel + Send Messages: all the bot needs to post summaries and clock panels.</summary>
    public const long PostPermissions = 1024 + 2048;

    public bool Configured => options.Value.BotConfigured;

    /// <summary>Discord's "add to server" link with the bot scope and posting permissions.</summary>
    public string? InviteUrl => DiscordOptions.IsSnowflake(options.Value.ApplicationId)
        ? $"https://discord.com/oauth2/authorize?client_id={options.Value.ApplicationId}&scope=bot+applications.commands&permissions={PostPermissions}"
        : null;

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, Api + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", options.Value.BotToken);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string path, object? body, string what, CancellationToken ct)
    {
        if (!Configured) return null;
        try
        {
            using var request = Request(method, path, body);
            using var response = await clients.CreateClient(ClientName).SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                log.LogWarning("Discord {What} failed with status {Status}", what, (int)response.StatusCode);
                return null;
            }
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return JsonDocument.Parse("{}");
            return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            log.LogWarning("Discord {What} failed ({ExceptionType})", what, ex.GetType().Name);
            return null;
        }
    }

    public async Task<DiscordAppInfo?> ApplicationAsync(CancellationToken ct = default)
    {
        using var json = await SendAsync(HttpMethod.Get, "/applications/@me", null, "application status", ct);
        if (json is null) return null;
        var root = json.RootElement;
        return new DiscordAppInfo(Str(root, "name") ?? "", Int(root, "approximate_guild_count"), Int(root, "approximate_user_install_count"),
            Str(root, "interactions_endpoint_url"));
    }

    /// <summary>Points Discord's Interactions Endpoint at <paramref name="url"/>. Discord PINGs it first and refuses if that fails.</summary>
    public async Task<bool> SetInteractionsUrlAsync(string url, CancellationToken ct = default)
    {
        using var json = await SendAsync(HttpMethod.Patch, "/applications/@me", new { interactions_endpoint_url = url }, "interactions URL update", ct);
        return json is not null;
    }

    /// <summary>Replaces the app's slash commands with <see cref="DiscordCommands.All"/>, in one server (fast) or globally.</summary>
    public async Task<bool> RegisterCommandsAsync(string? guildId, CancellationToken ct = default)
    {
        var scope = guildId is null ? "" : $"/guilds/{guildId}";
        using var json = await SendAsync(HttpMethod.Put, $"/applications/{options.Value.ApplicationId}{scope}/commands", DiscordCommands.All, "command registration", ct);
        return json is not null;
    }

    /// <summary>Posts a message (no pings, ever). Returns its ID, or null if Discord refused (bot not in the server, no permission…).</summary>
    public async Task<string?> PostAsync(string channelId, string content, object? components = null, CancellationToken ct = default)
    {
        object body = components is null
            ? new { content, allowed_mentions = new { parse = Array.Empty<string>() } }
            : new { content, components, allowed_mentions = new { parse = Array.Empty<string>() } };
        using var json = await SendAsync(HttpMethod.Post, $"/channels/{channelId}/messages", body, "channel post", ct);
        return json is null ? null : Str(json.RootElement, "id");
    }

    public async Task<bool> DeleteAsync(string channelId, string messageId, CancellationToken ct = default)
    {
        using var json = await SendAsync(HttpMethod.Delete, $"/channels/{channelId}/messages/{messageId}", null, "message delete", ct);
        return json is not null;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}

/// <summary>The slash commands, the same ones scripts/register-discord-commands.mjs registers.</summary>
public static class DiscordCommands
{
    private static readonly object Range = new
    {
        type = 3, name = "range", description = "Hours window", required = false,
        choices = new[] { new { name = "This week", value = "week" }, new { name = "This month", value = "month" }, new { name = "All time", value = "all" } },
    };

    public static readonly object[] All =
    [
        new { name = "myhours", description = "View your own AVI Hours totals", options = new[] { Range } },
        new
        {
            name = "clock", description = "Clock in, clock out, or check your status",
            options = new[]
            {
                new { type = 1, name = "in", description = "Start a clock session" },
                new { type = 1, name = "out", description = "End your current clock session" },
                new { type = 1, name = "status", description = "Check your current clock status" },
            },
        },
        new { name = "clockstatus", description = "Check your own clock-in status" },
        new { name = "leaderboard", description = "This season's Hours leaderboard (if your organization shows one)" },
        new
        {
            name = "teamhours", description = "View aggregate Hours for a team you lead",
            options = new[] { Range, new { type = 3, name = "team", description = "Team ID (optional if you manage one team)", required = false } },
        },
    ];

    /// <summary>Button IDs on the clock panel.</summary>
    public const string ClockIn = "avi:clock:in";
    public const string ClockOut = "avi:clock:out";
    public const string ClockStatus = "avi:clock:status";

    /// <summary>One row: Clock in (green), Clock out (red), My status (grey).</summary>
    public static object ClockPanelButtons() => new[]
    {
        new
        {
            type = 1,
            components = new[]
            {
                new { type = 2, style = 3, label = "Clock in", custom_id = ClockIn },
                new { type = 2, style = 4, label = "Clock out", custom_id = ClockOut },
                new { type = 2, style = 2, label = "My status", custom_id = ClockStatus },
            },
        },
    };
}
