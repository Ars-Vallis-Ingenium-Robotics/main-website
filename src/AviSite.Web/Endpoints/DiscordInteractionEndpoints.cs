using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AviSite.Web.Options;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace AviSite.Web.Endpoints;

/// <summary>Stateless Discord Interactions webhook. No gateway connection or public message writes.</summary>
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
        IAccountStore accounts, HoursStore hours, TimeProvider clock)
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
            if (interactionType == 1) return Results.Json(new { type = 1 }); // Discord endpoint verification PING
            if (interactionType != 2) return Results.BadRequest();

            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return Ephemeral("I couldn't read that command.");
            var command = String(data, "name");
            var discordUserId = UserId(root);
            if (discordUserId is null) return Ephemeral("I couldn't identify the invoking Discord account.");

            var account = await accounts.FindByDiscordUserIdAsync(discordUserId, http.RequestAborted);
            if (account is null || (!account.SiteWide && account.Teams.Count == 0))
                return Ephemeral(LinkPrompt(config));

            return command switch
            {
                "myhours" => MyHours(data, account, hours),
                "clockstatus" => ClockStatus(account, hours),
                "teamhours" => await TeamHours(data, account, accounts, hours, http.RequestAborted),
                _ => Ephemeral("That command isn't supported."),
            };
        }
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

    private static async Task<IResult> TeamHours(JsonElement data, Account account, IAccountStore accounts,
        HoursStore store, CancellationToken ct)
    {
        var tree = await accounts.GroupsAsync(ct);
        var access = Access.For(account, tree);
        var managed = HoursRules.ManagedTeams(access);
        if (managed.Count == 0) return Ephemeral("Team hours are available only to a linked team lead, officer, or owner.");

        var requested = Option(data, "team")?.Trim().ToLowerInvariant();
        if (requested is null && managed.Count == 1) requested = managed.First();
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
