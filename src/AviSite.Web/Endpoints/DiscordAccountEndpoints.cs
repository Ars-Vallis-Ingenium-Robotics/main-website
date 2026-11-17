using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AviSite.Web.Options;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Endpoints;

/// <summary>Links the signed-in AVI account to a Discord snowflake using OAuth identify only.</summary>
public static class DiscordAccountEndpoints
{
    private const string CookieName = "avi_discord_oauth";
    private const string ProtectorPurpose = "AviSite.DiscordOAuth.v1";
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);

    public static void MapDiscordAccountEndpoints(this WebApplication app)
    {
        var link = app.MapGroup("/admin/discord").RequireAuthorization(AdminEndpoints.QrPolicy);
        link.MapGet("/link", (HttpContext http, IOptions<DiscordOptions> options, IDataProtectionProvider protection,
            IAccountStore accounts, TimeProvider clock) => StartLink(http, options.Value, protection, accounts, clock));
        link.MapGet("/callback", CompleteLinkAsync);
        link.MapPost("/unlink", UnlinkAsync);
    }

    private static async Task<IResult> StartLink(HttpContext http, DiscordOptions config,
        IDataProtectionProvider protection, IAccountStore accounts, TimeProvider clock)
    {
        var email = http.User.FindFirst(AccountAuth.EmailClaim)?.Value;
        if (email is null) return Results.LocalRedirect("/admin/discord?link=account");
        if (!config.LinkingConfigured) return Results.LocalRedirect("/admin/discord?link=not-configured");
        var account = await accounts.FindAsync(email, http.RequestAborted);
        if (!Active(account)) return Results.LocalRedirect("/admin/discord?link=account");
        if (!string.IsNullOrWhiteSpace(account!.DiscordUserId)) return Results.LocalRedirect("/admin/discord?link=already-linked");

        var state = Token();
        var verifier = Token();
        var payload = JsonSerializer.Serialize(new LinkState(state, verifier, account.Email, account.Stamp, clock.GetUtcNow().ToUnixTimeSeconds()));
        http.Response.Cookies.Append(CookieName, protection.CreateProtector(ProtectorPurpose).Protect(payload), new CookieOptions
        {
            HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax,
            Path = "/admin/discord", MaxAge = StateLifetime, IsEssential = true,
        });
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = config.ApplicationId, ["redirect_uri"] = config.RedirectUri,
            ["response_type"] = "code", ["scope"] = "identify", ["state"] = state,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["prompt"] = "consent",
        };
        return Results.Redirect(QueryHelpers.AddQueryString("https://discord.com/oauth2/authorize", query));
    }

    private static async Task<IResult> CompleteLinkAsync(string? code, string? state, string? error,
        HttpContext http, IOptions<DiscordOptions> options, IDataProtectionProvider protection,
        IHttpClientFactory clients, IAccountStore accounts, TimeProvider clock, ILoggerFactory loggers)
    {
        var config = options.Value;
        var logger = loggers.CreateLogger("DiscordAccountLink");
        var raw = http.Request.Cookies[CookieName];
        http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/admin/discord", Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax });
        if (!config.LinkingConfigured || error is not null || string.IsNullOrWhiteSpace(code) || raw is null)
            return Continue(http, "/admin/discord?link=failed");

        LinkState? saved;
        try { saved = JsonSerializer.Deserialize<LinkState>(protection.CreateProtector(ProtectorPurpose).Unprotect(raw)); }
        catch (CryptographicException) { return Continue(http, "/admin/discord?link=expired"); }
        if (saved is null || !Same(saved.State, state) || !Recent(saved.CreatedUnixSeconds, clock.GetUtcNow()))
            return Continue(http, "/admin/discord?link=expired");
        var signedInEmail = http.User.FindFirst(AccountAuth.EmailClaim)?.Value;
        if (signedInEmail is null || !string.Equals(AccountRules.Normalize(signedInEmail), saved.Email, StringComparison.Ordinal))
            return Continue(http, "/admin/discord?link=account");
        var account = await accounts.FindAsync(saved.Email, http.RequestAborted);
        if (!Active(account) || account!.Stamp != saved.Stamp) return Continue(http, "/admin/discord?link=account");

        try
        {
            using var tokenResponse = await clients.CreateClient("discord-oauth").PostAsync("https://discord.com/api/v10/oauth2/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = config.ApplicationId!, ["client_secret"] = config.ClientSecret!,
                    ["grant_type"] = "authorization_code", ["code"] = code,
                    ["redirect_uri"] = config.RedirectUri!, ["code_verifier"] = saved.Verifier,
                }), http.RequestAborted);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Discord OAuth token exchange failed with status {Status}", (int)tokenResponse.StatusCode);
                return Continue(http, "/admin/discord?link=failed");
            }
            using var tokenJson = await JsonDocument.ParseAsync(await tokenResponse.Content.ReadAsStreamAsync(http.RequestAborted), cancellationToken: http.RequestAborted);
            if (!tokenJson.RootElement.TryGetProperty("access_token", out var tokenElement) || tokenElement.ValueKind != JsonValueKind.String)
                return Continue(http, "/admin/discord?link=failed");
            var accessToken = tokenElement.GetString();
            if (string.IsNullOrWhiteSpace(accessToken)) return Continue(http, "/admin/discord?link=failed");

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/v10/users/@me");
            userRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            using var userResponse = await clients.CreateClient("discord-oauth").SendAsync(userRequest, http.RequestAborted);
            if (!userResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Discord identify request failed with status {Status}", (int)userResponse.StatusCode);
                return Continue(http, "/admin/discord?link=failed");
            }
            using var userJson = await JsonDocument.ParseAsync(await userResponse.Content.ReadAsStreamAsync(http.RequestAborted), cancellationToken: http.RequestAborted);
            if (!userJson.RootElement.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
                return Continue(http, "/admin/discord?link=failed");
            var discordUserId = idElement.GetString();
            if (string.IsNullOrWhiteSpace(discordUserId)) return Continue(http, "/admin/discord?link=failed");
            if (!await accounts.LinkDiscordAsync(saved.Email, discordUserId, http.RequestAborted))
                return Continue(http, "/admin/discord?link=conflict");
            return Continue(http, "/admin/discord?linked=1");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            // Never log OAuth bodies, codes, tokens, or identity response data.
            logger.LogWarning("Discord account linking failed during OAuth exchange ({ExceptionType})", ex.GetType().Name);
            return Continue(http, "/admin/discord?link=failed");
        }
    }

    private static async Task<IResult> UnlinkAsync(HttpContext http, IAntiforgery antiforgery, IAccountStore accounts)
    {
        if (!await antiforgery.IsRequestValidAsync(http)) return Results.BadRequest();
        var email = http.User.FindFirst(AccountAuth.EmailClaim)?.Value;
        if (email is null) return Results.LocalRedirect("/admin/discord?link=account");
        await accounts.UnlinkDiscordAsync(email, http.RequestAborted);
        return Results.LocalRedirect("/admin/discord?unlinked=1");
    }

    private static bool Active(Account? account) => account is not null && (account.SiteWide || account.Teams.Count > 0);

    private static bool Recent(long createdUnixSeconds, DateTimeOffset now)
    {
        try
        {
            var created = DateTimeOffset.FromUnixTimeSeconds(createdUnixSeconds);
            return created <= now.AddMinutes(1) && now - created <= StateLifetime;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool Same(string expected, string? actual) => CryptographicOperations.FixedTimeEquals(
        Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual ?? ""));

    private static string Token() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static IResult Continue(HttpContext http, string path)
    {
        var target = System.Net.WebUtility.HtmlEncode(path);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Content($"<!doctype html><meta charset=utf-8><meta name=robots content=noindex><meta http-equiv=refresh content=\"0;url={target}\"><title>Discord link</title><p>Continue: <a href=\"{target}\">return to AVI</a></p>", "text/html");
    }

    private sealed record LinkState(string State, string Verifier, string Email, string Stamp, long CreatedUnixSeconds);
}
