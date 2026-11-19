using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AviSite.Web.Discord;
using AviSite.Web.Options;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace AviSite.Web.Endpoints;

/// <summary>Stateless Discord Interactions webhook: slash commands and clock-panel buttons. No gateway connection; every reply is private.</summary>
public static class DiscordInteractionEndpoints
{
    private const int MaxBodyBytes = 64 * 1024;
    private const int EphemeralFlag = 64;
    private static readonly TimeSpan SignatureSkew = TimeSpan.FromMinutes(5);
    private static readonly HoursSettings DefaultHoursSettings = new("", false, 8, 1);

    public static void MapDiscordInteractionEndpoints(this WebApplication app) =>
        // Discord signs the exact raw body; browser antiforgery tokens are neither present nor needed here.
        app.MapPost("/discord/interactions", HandleAsync).DisableAntiforgery();

    private static async Task<IResult> HandleAsync(HttpContext http, IOptions<DiscordOptions> options,
        IAccountStore accounts, HoursStore hours, DiscordBotStore bot, TimeProvider clock)
    {
        // Keep Discord's signed webhook response codes intact; the site's HTML 404 re-execution is for browsers.
        if (http.Features.Get<IStatusCodePagesFeature>() is { } statusPages) statusPages.Enabled = false;
        var config = options.Value;
        if (!config.InteractionsConfigured) return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (http.Request.ContentLength is > MaxBodyBytes) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var body = await ReadBodyAsync(http.Request, http.RequestAborted);
        if (body is null) return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (!VerifyRequest(http.Request, body, config.PublicKey!, clock.GetUtcNow())) return Results.Unauthorized();

        JsonDocument document;
        try { document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 32 }); }
        catch (JsonException) { return Results.BadRequest(); }

        using (document)
        {
            var root = document.RootElement;
            if (String(root, "application_id") != config.ApplicationId) return Results.Unauthorized();
            if (!root.TryGetProperty("type", out var type) || !type.TryGetInt32(out var interactionType)) return Results.BadRequest();
            // Discord endpoint verification PING. Answered even while paused, so Discord keeps the endpoint.
            if (interactionType == 1) return Results.Json(new { type = 1 });
            if (interactionType is not (2 or 3)) return Results.BadRequest(); // 2: slash command, 3: button

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return Ephemeral("I couldn't read that command.");
            var isButton = interactionType == 3;
            var name = isButton ? String(data, "custom_id") ?? "" : String(data, "name") ?? "";
            var kind = (isButton ? "button:" : "/") + name;
            var guildId = String(root, "guild_id");
            var channelId = String(root, "channel_id");
            IResult Done(string outcome, IResult reply) { bot.Record(kind, outcome, guildId); return reply; }

            if (bot.State().Paused)
                return Done("paused", Ephemeral("The AVI Hours bot is paused right now. Try again later."));
            var server = bot.Get(guildId);
            // Once the owner lists servers, only those get answers. DMs and user installs (no server) stay personal.
            if (guildId is not null && server is null && bot.AnyServers())
                return Done("blocked", Ephemeral("AVI Hours isn't enabled in this server. Ask the AVI site owner to add it."));
            if (!isButton && server is { CommandChannels.Count: > 0 } && (channelId is null || !server.CommandChannels.Contains(channelId)))
                return Done("channel", Ephemeral("Hours commands work in " + string.Join(", ", server.CommandChannels.Select(DiscordBotRules.Mention)) + " in this server."));

            var discordUserId = UserId(root);
            if (discordUserId is null) return Done("error", Ephemeral("I couldn't identify the invoking Discord account."));

            var account = await accounts.FindByDiscordUserIdAsync(discordUserId, http.RequestAborted);
            if (account is null || (!account.SiteWide && account.Teams.Count == 0))
                return Done("unlinked", Ephemeral(LinkPrompt(config)));

            var reply = isButton
                ? name switch
                {
                    DiscordCommands.ClockIn => await ButtonClockIn(account, server, accounts, hours, config, http.RequestAborted),
                    DiscordCommands.ClockOut => ButtonClockOut(account, hours),
                    DiscordCommands.ClockStatus => ClockStatus(account, hours),
                    _ => null,
                }
                : name switch
                {
                    "myhours" => MyHours(data, account, hours),
                    "clockstatus" => ClockStatus(account, hours),
                    "teamhours" => await TeamHours(data, account, server, accounts, hours, http.RequestAborted),
                    _ => null,
                };
            return reply is null ? Done("unknown", Ephemeral("That command isn't supported.")) : Done("ok", reply);
        }
    }

    /// <summary>Clock-panel button: clock in to the server's team (or one under it), with the reason picked later on the site.</summary>
    private static async Task<IResult> ButtonClockIn(Account account, DiscordGuild? server, IAccountStore accounts, HoursStore store,
        DiscordOptions config, CancellationToken ct)
    {
        if (store.Running(account.Email) is { } running)
            return Ephemeral($"You're already clocked in · {HoursText.Cat(running.Category)} · {HoursText.Duration(running.Duration(store.Now))}.");
        var tree = await accounts.GroupsAsync(ct);
        var loggable = HoursRules.LoggableTeams(Access.For(account, tree)).Select(g => g.Id).ToList();
        // Prefer a team inside this server's organization; among those, the one used last.
        HashSet<string> here = server is null ? [] : tree.WithDescendants([server.Team]);
        var candidates = loggable.Where(here.Contains).ToList();
        if (candidates.Count == 0) candidates = loggable;
        var last = store.LastUsed(account.Email)?.Team;
        var team = last is not null && candidates.Contains(last) ? last : candidates.FirstOrDefault();
        if (team is null) return Ephemeral("You're not on a team you can log hours for yet. Ask your team lead to add you.");
        if (store.ClockIn(account.Email, team, "") is null) return Ephemeral("You're already clocked in.");
        var site = config.PublicBaseUrl is { Length: > 0 } b ? $" Pick what you worked on later: {b.TrimEnd('/')}/admin/hours" : "";
        return Ephemeral($"Clocked in · {tree.Path(team)}.{site}");
    }

    private static IResult ButtonClockOut(Account account, HoursStore store)
    {
        var e = store.ClockOut(account.Email, null);
        if (e is null) return Ephemeral("You're not clocked in.");
        // Same rule as the site: a forgotten clock-out is capped instead of becoming a 30-hour entry.
        if (e.Duration(store.Now) > HoursStore.LongSession)
        {
            e.EndUtc = e.StartUtc + HoursStore.LongSession;
            store.Update(e, account.Email);
            return Ephemeral($"Clocked out. That session ran over {HoursStore.LongSession.TotalHours:0} h, so it was capped; fix the end time on the site.");
        }
        return Ephemeral($"Clocked out · {HoursText.Duration(e.Duration(store.Now))} · {HoursText.Cat(e.Category)}.");
    }

    private static IResult MyHours(JsonElement data, Account account, HoursStore store)
    {
        var range = Range(data);
        var now = store.Now;
        var (from, to, label) = HoursRanges.Resolve(range, DefaultHoursSettings, now);
        var entries = store.List(emails: [account.Email], from: from, to: to);
        var total = entries.Sum(e => HoursRanges.Clip(e, from, to, now));
        if (total <= 0) return Ephemeral($"You have no logged hours for {label.ToLowerInvariant()}.");

        var categories = entries.GroupBy(e => HoursText.Cat(e.Category))
            .Select(g => (Name: g.Key, Hours: g.Sum(e => HoursRanges.Clip(e, from, to, now))))
            .Where(x => x.Hours > 0).OrderByDescending(x => x.Hours).Take(8).ToList();
        var lines = categories.Select(x => $"• {x.Name}: {HoursText.Hours(x.Hours)} h");
        return Ephemeral($"Your hours · {label}: **{HoursText.Hours(total)} h**\n" + string.Join('\n', lines));
    }

    private static IResult ClockStatus(Account account, HoursStore store)
    {
        var running = store.Running(account.Email);
        if (running is null) return Ephemeral("You're not clocked in.");
        var elapsed = HoursText.Duration(running.Duration(store.Now));
        return Ephemeral($"Clocked in · {running.Team} · {HoursText.Cat(running.Category)} · {elapsed}.");
    }

    private static async Task<IResult> TeamHours(JsonElement data, Account account, DiscordGuild? server, IAccountStore accounts,
        HoursStore store, CancellationToken ct)
    {
        var tree = await accounts.GroupsAsync(ct);
        var access = Access.For(account, tree);
        var managed = HoursRules.ManagedTeams(access);
        if (managed.Count == 0) return Ephemeral("Team hours are available only to a linked team lead, officer, or owner.");

        var requested = Option(data, "team")?.Trim().ToLowerInvariant();
        if (requested is null && managed.Count == 1) requested = managed.First();
        if (requested is null && server is not null && managed.Contains(server.Team)) requested = server.Team; // this server's team
        if (requested is null)
        {
            var choices = managed.OrderBy(id => tree.Path(id), StringComparer.OrdinalIgnoreCase).Take(30)
                .Select(id => $"• {tree.Path(id)} (`{id}`)");
            return Ephemeral("Choose a team ID with `/teamhours team:<id>`. You can manage:\n" + string.Join('\n', choices));
        }
        if (!managed.Contains(requested) || tree.Find(requested) is null)
            return Ephemeral("You don't have permission to view hours for that team.");

        var scope = tree.WithDescendants([requested]);
        var now = store.Now;
        var (from, to, label) = HoursRanges.Resolve(Range(data), DefaultHoursSettings, now);
        var entries = store.List(teams: scope, from: from, to: to);
        var total = entries.Sum(e => HoursRanges.Clip(e, from, to, now));
        if (total <= 0) return Ephemeral($"No team hours are logged for {label.ToLowerInvariant()}.");
        return Ephemeral($"{tree.Path(requested)} · {label}: **{HoursText.Hours(total)} h**");
    }

    private static string Range(JsonElement data)
    {
        var value = Option(data, "range");
        return value is "week" or "month" or "all" ? value : "week";
    }

    private static string? Option(JsonElement data, string name)
    {
        if (!data.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array) return null;
        foreach (var option in options.EnumerateArray())
            if (String(option, "name") == name && option.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    private static string? UserId(JsonElement root)
    {
        if (root.TryGetProperty("member", out var member) && member.TryGetProperty("user", out var memberUser))
            return String(memberUser, "id");
        return root.TryGetProperty("user", out var user) ? String(user, "id") : null;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string LinkPrompt(DiscordOptions config) => config.LinkingConfigured
        ? $"Link your AVI account before using Hours commands: {config.PublicBaseUrl!.TrimEnd('/')}/admin/discord"
        : "Link your Discord account from the AVI account menu before using Hours commands.";

    private static IResult Ephemeral(string content)
    {
        // Discord caps message content at 2,000 characters. Mentions are explicitly disabled for user-entered labels.
        if (content.Length > 1900) content = content[..1900];
        return Results.Json(new
        {
            type = 4,
            data = new { content, flags = EphemeralFlag, allowed_mentions = new { parse = Array.Empty<string>() } },
        });
    }

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                if (read == 0) break;
                if (output.Length + read > MaxBodyBytes) return null;
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static bool VerifyRequest(HttpRequest request, byte[] body, string publicKeyHex, DateTimeOffset now)
    {
        var signatureHex = request.Headers["X-Signature-Ed25519"].ToString();
        var timestamp = request.Headers["X-Signature-Timestamp"].ToString();
        if (signatureHex.Length != 128 || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)) return false;

        DateTimeOffset signedAt;
        try { signedAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
        catch (ArgumentOutOfRangeException) { return false; }
        var skew = now - signedAt;
        if (skew < -SignatureSkew || skew > SignatureSkew) return false;

        try
        {
            var publicKey = Convert.FromHexString(publicKeyHex);
            var signature = Convert.FromHexString(signatureHex);
            if (publicKey.Length != 32 || signature.Length != 64) return false;
            var timestampBytes = Encoding.UTF8.GetBytes(timestamp);
            var signedMessage = new byte[timestampBytes.Length + body.Length];
            Buffer.BlockCopy(timestampBytes, 0, signedMessage, 0, timestampBytes.Length);
            Buffer.BlockCopy(body, 0, signedMessage, timestampBytes.Length, body.Length);

            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(signedMessage, 0, signedMessage.Length);
            return verifier.VerifySignature(signature);
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
