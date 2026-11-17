using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AviSite.Web.Options;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Endpoints;

/// <summary>
/// Individual /admin accounts: "Sign in with Google", then the email decides who you are (Store/Accounts.cs).
/// Every admin request re-checks the account, so removing someone or changing their role takes effect on their next click.
///
/// The Google flow is the standard OAuth 2.0 authorization-code flow with PKCE, written out by hand (no extra packages):
///   /admin/google            → remember state + PKCE verifier + nonce in a short-lived protected cookie, send to Google
///   /admin/google/callback   → check state, swap the code for an ID token directly with Google over HTTPS (so the token
///                              needs no signature check, per Google's docs), check audience, issuer, expiry, nonce, verified email.
/// </summary>
public static class AccountAuth
{
    public const string EmailClaim = ClaimTypes.Email;
    public const string StampClaim = "avi:stamp";
    public const string AccountRoleClaim = "avi:role";
    private const string OAuthCookie = "avi_oauth";
    private const string Purpose = "AviSite.GoogleSignIn.v1";

    /// <summary>Claims for a signed-in account. Officers/owners also get role "Admin" (shop, countdowns, every QR code);
    /// leads/members get role "Team" plus their group, which limits them to that group's QR codes.</summary>
    public static ClaimsPrincipal PrincipalFor(Account a)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, JsonAccountStore.DisplayName(a)),
            new(EmailClaim, a.Email),
            new(StampClaim, a.Stamp),
            new(AccountRoleClaim, a.Role.ToString()),
            new(ClaimTypes.Role, a.Role.ToString()),
        };
        // Coarse gates only (which admin pages open). What someone can see inside a page comes from Access.For.
        if (a.SiteWide) claims.Add(new(ClaimTypes.Role, "Admin"));
        if (a.Teams.Count > 0) claims.Add(new(ClaimTypes.Role, "Team"));
        if (a.Teams.Any(t => t.Role == AccountRole.Lead)) claims.Add(new(ClaimTypes.Role, "Lead"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, AdminEndpoints.Scheme));
    }

    /// <summary>What the person behind this request may do (accounts, or the old shared passwords).</summary>
    public static async Task<Access> AccessAsync(this HttpContext http, IAccountStore store)
    {
        var tree = await store.GroupsAsync();
        if (http.User.FindFirst(EmailClaim)?.Value is { } email) return Access.For(await store.FindAsync(email), tree);
        if (http.User.IsInRole("Admin")) return Access.ForOfficerPassword(tree);
        if (http.User.FindFirst(AdminEndpoints.TeamClaim)?.Value is { } team) return Access.ForTeamPassword(team, tree);
        return Access.For(null, tree);
    }

    /// <summary>The account behind this request, or null for the shared-password logins.</summary>
    public static async Task<Account?> CurrentAsync(this HttpContext http, IAccountStore store) =>
        http.User.FindFirst(EmailClaim)?.Value is { } email ? await store.FindAsync(email) : null;

    /// <summary>Cookie hook: end the session if the account is gone; refresh it if their role or group changed.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        if (ctx.Principal?.FindFirst(EmailClaim)?.Value is not { } email) return; // shared-password login
        var store = ctx.HttpContext.RequestServices.GetRequiredService<IAccountStore>();
        var account = await store.FindAsync(email);
        if (account is null || (!account.SiteWide && account.Teams.Count == 0))
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(AdminEndpoints.Scheme);
            return;
        }
        if (ctx.Principal.FindFirst(StampClaim)?.Value != account.Stamp)
        {
            ctx.ReplacePrincipal(PrincipalFor(account));
            ctx.ShouldRenew = true;
        }
    }

    public static void MapAccountAuth(this WebApplication app)
    {
        app.MapGet("/admin/google", (string? returnUrl, HttpContext http, IOptions<AdminOptions> options, IDataProtectionProvider dp) =>
        {
            var g = options.Value.Google;
            if (!g.Enabled) return Results.LocalRedirect("/admin/login?error=google-off");
            var state = Token(); var verifier = Token(); var nonce = Token();
            var payload = JsonSerializer.Serialize(new OAuthState(state, verifier, nonce, SafeReturn(returnUrl)));
            http.Response.Cookies.Append(OAuthCookie, dp.CreateProtector(Purpose).Protect(payload), new CookieOptions
            {
                HttpOnly = true, Secure = http.Request.IsHttps, SameSite = SameSiteMode.Lax, // Lax: it has to come back from Google
                Path = "/admin/google", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true,
            });
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var q = new Dictionary<string, string?>
            {
                ["client_id"] = g.ClientId, ["redirect_uri"] = RedirectUri(http), ["response_type"] = "code",
                ["scope"] = "openid email profile", ["state"] = state, ["nonce"] = nonce,
                ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["prompt"] = "select_account",
            };
            return Results.Redirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", q));
        });

        app.MapGet("/admin/google/callback", async (string? code, string? state, string? error, HttpContext http, IOptions<AdminOptions> options,
            IDataProtectionProvider dp, IHttpClientFactory clients, IAccountStore store, TimeProvider clock, ILoggerFactory logs) =>
        {
            var log = logs.CreateLogger("GoogleSignIn");
            var g = options.Value.Google;
            var raw = http.Request.Cookies[OAuthCookie];
            http.Response.Cookies.Delete(OAuthCookie, new CookieOptions { Path = "/admin/google" });
            if (!g.Enabled || error is not null || code is null || raw is null) return Fail("cancelled");

            OAuthState? saved;
            try { saved = JsonSerializer.Deserialize<OAuthState>(dp.CreateProtector(Purpose).Unprotect(raw)); }
            catch (CryptographicException) { return Fail("expired"); }
            if (saved is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(saved.State), Encoding.ASCII.GetBytes(state ?? ""))) return Fail("expired");

            JsonElement idToken;
            try
            {
                using var res = await clients.CreateClient("google-signin").PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["code"] = code, ["client_id"] = g.ClientId!, ["client_secret"] = g.ClientSecret!, ["redirect_uri"] = RedirectUri(http),
                    ["grant_type"] = "authorization_code", ["code_verifier"] = saved.Verifier,
                }));
                var body = await res.Content.ReadAsStringAsync();
                if (!res.IsSuccessStatusCode) { log.LogWarning("Google token exchange failed: {Status}", res.StatusCode); return Fail("google"); }
                var jwt = JsonDocument.Parse(body).RootElement.GetProperty("id_token").GetString()!;
                idToken = JsonDocument.Parse(Base64UrlDecode(jwt.Split('.')[1])).RootElement;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or IndexOutOfRangeException or FormatException)
            {
                log.LogWarning(ex, "Google sign-in failed");
                return Fail("google");
            }

            var iss = Str(idToken, "iss");
            var exp = idToken.TryGetProperty("exp", out var e) && e.TryGetInt64(out var x) ? DateTimeOffset.FromUnixTimeSeconds(x) : DateTimeOffset.MinValue;
            var verified = idToken.TryGetProperty("email_verified", out var v) && (v.ValueKind == JsonValueKind.True || v.GetString() == "true");
            if (Str(idToken, "aud") != g.ClientId || iss is not ("https://accounts.google.com" or "accounts.google.com")
                || exp < clock.GetUtcNow() || Str(idToken, "nonce") != saved.Nonce || !verified)
            {
                log.LogWarning("Google ID token rejected (aud/iss/exp/nonce/email_verified)");
                return Fail("google");
            }

            var account = await store.FindAsync(Str(idToken, "email") ?? "");
            if (account is null) return Fail("not-listed");
            if (!account.SiteWide && account.Teams.Count == 0) return Fail("no-teams");
            if (account.Name == account.Email && Str(idToken, "name") is { Length: > 0 } googleName && !account.FromConfig)
            {
                account.Name = googleName; // first sign-in fills in the name if the owner didn't
                await store.SaveAsync(account);
                account = (await store.FindAsync(account.Email))!;
            }
            await SignInAsync(http, account, store);
            return Continue(http, Home(account, saved.ReturnUrl));
        }).RequireRateLimiting(AdminEndpoints.LoginRateLimit);

        if (app.Environment.IsDevelopment())
        {
            // Local testing only: pick an account instead of going through Google. Never mapped outside Development.
            app.MapPost("/admin/dev-login", async ([FromForm] string email, HttpContext http, IAccountStore store) =>
            {
                var account = await store.FindAsync(email);
                if (account is null) return Results.LocalRedirect("/admin/login?error=not-listed");
                if (!account.SiteWide && account.Teams.Count == 0) return Results.LocalRedirect("/admin/login?error=no-teams");
                await SignInAsync(http, account, store);
                return Results.LocalRedirect(Home(account, null));
            });
        }
    }

    private static async Task SignInAsync(HttpContext http, Account account, IAccountStore store)
    {
        // Stay signed in on this device (30 days, renewed as you use it) so clocking in stays two taps.
        // Removing someone still ends it on their next click (ValidateAsync).
        await http.SignInAsync(AdminEndpoints.Scheme, PrincipalFor(account), new AuthenticationProperties { IsPersistent = true });
        await store.MarkSignInAsync(account.Email);
    }

    public static string Home(Account a, string? returnUrl)
    {
        var admin = a.SiteWide;
        // Members mostly come to log hours, so that's home for everyone without site-wide access.
        var target = SafeReturn(returnUrl) ?? (admin ? "/admin" : "/admin/hours");
        return admin || target.StartsWith("/admin/qr", StringComparison.Ordinal) || target.StartsWith("/admin/people", StringComparison.Ordinal)
            || target.StartsWith("/admin/hours", StringComparison.Ordinal) ? target : "/admin/hours";
    }

    private static string? SafeReturn(string? r) =>
        !string.IsNullOrEmpty(r) && r.StartsWith("/admin", StringComparison.Ordinal) && !r.StartsWith("//") && !r.Contains('\\') ? r : null;

    private static IResult Fail(string why) => Results.LocalRedirect("/admin/login?error=" + why);

    /// <summary>The admin cookie is SameSite=Strict, and browsers won't send it on a redirect chain that started at Google.
    /// So finish with a tiny same-site page that moves on by itself (no script needed, CSP stays strict).</summary>
    private static IResult Continue(HttpContext http, string target)
    {
        var t = System.Net.WebUtility.HtmlEncode(target);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Content($"<!doctype html><meta charset=utf-8><meta name=robots content=noindex><meta http-equiv=refresh content=\"0;url={t}\"><title>Signing in…</title><p>Signed in. <a href=\"{t}\">Continue</a></p>", "text/html");
    }

    private static string RedirectUri(HttpContext http) => $"{http.Request.Scheme}://{http.Request.Host}/admin/google/callback";
    private static string Token() => Base64Url(RandomNumberGenerator.GetBytes(32));
    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string s)
    {
        s = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private sealed record OAuthState(string State, string Verifier, string Nonce, string? ReturnUrl);
}
