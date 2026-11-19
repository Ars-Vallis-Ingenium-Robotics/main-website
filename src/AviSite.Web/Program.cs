using System.Threading.RateLimiting;
using AviSite.Web.Components;
using AviSite.Web.Endpoints;
using AviSite.Web.Options;
using AviSite.Web.Security;
using AviSite.Web.Store;
using AviSite.Web.Store.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.FileProviders;

// `dotnet run -- hash-password` prints a hash for Admin:PasswordHash and exits.
if (args is ["hash-password", ..])
{
    Console.Write("Admin password: ");
    var pw = Console.ReadLine() ?? "";
    if (pw.Length < 12) { Console.Error.WriteLine("Use at least 12 characters."); return 1; }
    Console.WriteLine(AdminPassword.Hash(pw));
    return 0;
}

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.Configure<SiteOptions>(builder.Configuration.GetSection(SiteOptions.Section));
services.Configure<StoreOptions>(builder.Configuration.GetSection(StoreOptions.Section));
services.Configure<PayPalOptions>(builder.Configuration.GetSection(PayPalOptions.Section));
services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.Section));
services.Configure<DiscordOptions>(builder.Configuration.GetSection(DiscordOptions.Section));

services.AddRazorComponents();
services.AddHttpContextAccessor();
services.AddAntiforgery(o => o.HeaderName = "RequestVerificationToken");
services.AddHealthChecks();
services.AddSingleton(TimeProvider.System);

// Keys protect the cart cookie and admin cookie; persist them so a restart doesn't log people out / empty carts.
var dataDir = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Store:DataDirectory"] ?? "App_Data");
services.AddDataProtection()
    .SetApplicationName("AviSite")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDir, "keys")));

// Store
services.AddSingleton<ICatalog, JsonCatalog>();
services.AddSingleton<IOrderStore, JsonOrderStore>();
// Dynamic QR codes (/dynamicqr/{slug}, edited in /admin/qr)
services.Configure<DynamicQrOptions>(builder.Configuration.GetSection(DynamicQrOptions.Section));
services.AddSingleton<SiteDb>();
services.AddSingleton<IDynamicQrStore, SqliteDynamicQrStore>();
services.AddSingleton<HoursStore>();
services.AddSingleton<ProfileStore>();
services.AddHttpClient("google-sheet");
services.AddHttpClient("google-signin", c => c.Timeout = TimeSpan.FromSeconds(15));
services.AddHttpClient("discord-oauth", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("AVI-Hours-Discord-Link/1.0");
});
// Discord bot management (/admin/discord/bot): pause, servers, channel posts. Calls Discord only with Discord__BotToken set.
services.AddSingleton<DiscordBotStore>();
services.AddHttpClient(AviSite.Web.Discord.DiscordApi.ClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://avirobotics.org, 1.0)");
});
services.AddSingleton<AviSite.Web.Discord.DiscordApi>();
services.AddSingleton<AviSite.Web.Discord.DiscordPosts>();
services.AddHostedService<AviSite.Web.Discord.DiscordScheduler>();
// Countdowns (/countdown, edited in /admin/countdowns)
services.AddSingleton<ICountdownStore, JsonCountdownStore>();
services.AddSingleton<IToolUsageStore, JsonToolUsageStore>();
services.AddSingleton<IAccountStore, JsonAccountStore>();
services.AddSingleton<Pricing>();
services.AddSingleton<PaymentReconciler>();
services.AddSingleton<PhotoCatalog>();
// Site copy lives in content/*.md (see content/README.md).
services.AddSingleton<AviSite.Web.Content.SiteContent>();
services.AddSingleton<AviSite.Web.Content.ClubData>();
services.AddSingleton<AviSite.Web.Content.People>();
services.AddScoped<AviSite.Web.Content.Copy>();
services.Configure<NonprofitOptions>(builder.Configuration.GetSection(NonprofitOptions.Section));
services.Configure<SmsSiteOptions>(builder.Configuration.GetSection(SmsSiteOptions.Section));
services.AddScoped<SmsUrls>();
services.AddScoped<CartCookie>();

var payPalMode = builder.Configuration.GetValue<PayPalMode?>("PayPal:Mode") ?? PayPalMode.Fake;
if (payPalMode == PayPalMode.Fake)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("PayPal:Mode=Fake is only allowed in Development. Set Sandbox or Live with credentials.");
    services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
}
else if (payPalMode == PayPalMode.Off)
{
    services.AddSingleton<IPaymentGateway, DisabledPaymentGateway>();
}
else
{
    if (string.IsNullOrWhiteSpace(builder.Configuration["PayPal:ClientId"]) || string.IsNullOrWhiteSpace(builder.Configuration["PayPal:ClientSecret"]))
        throw new InvalidOperationException("PayPal:ClientId and PayPal:ClientSecret are required (use env vars or user-secrets).");
    if (!builder.Environment.IsDevelopment())
    {
        // Sandbox in production = anyone with a free PayPal developer account gets real shirts for free.
        if (payPalMode != PayPalMode.Live && !builder.Configuration.GetValue<bool>("PayPal:AllowSandboxOutsideDevelopment"))
            throw new InvalidOperationException("PayPal:Mode must be Live outside Development (set PayPal:AllowSandboxOutsideDevelopment=true only on a private staging box).");
        if (string.IsNullOrWhiteSpace(builder.Configuration["PayPal:WebhookId"]))
            throw new InvalidOperationException("PayPal:WebhookId is required outside Development so delayed payments and refunds reconcile.");
    }
    services.AddHttpClient<PayPalGateway>((sp, http) =>
        {
            http.BaseAddress = builder.Configuration.GetSection(PayPalOptions.Section).Get<PayPalOptions>()!.ApiBase;
            http.Timeout = TimeSpan.FromSeconds(20);
        })
        // The gateway is held by a singleton (shared OAuth token cache), so recycle connections ourselves to pick up DNS changes.
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
        .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
    services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<PayPalGateway>());
}

services.AddAdminAuth(builder.Environment.IsDevelopment());
var storeLimit = builder.Environment.IsDevelopment() ? 1000 : 30; // dev: test suites hit checkout repeatedly
var globalLimit = builder.Environment.IsDevelopment() ? 20_000 : 3_600; // generous per-IP fixed-window backstop; route-specific caps remain tighter where needed
services.AddRateLimiter(o =>
{
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = globalLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (context, _) =>
    {
        // Don't let the branded 404 re-execution turn a rate-limit response into a misleading 404/400.
        if (context.HttpContext.Features.Get<IStatusCodePagesFeature>() is { } statusPages)
            statusPages.Enabled = false;
        return ValueTask.CompletedTask;
    };
    o.AddPolicy(AdminEndpoints.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(5) }));
    // Anonymous tool counters: plenty for a person clicking around, useless for padding the numbers.
    o.AddPolicy(ToolUsageEndpoints.RateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10) }));
    // Checkout + PayPal API: each call can create an order and/or hit PayPal.
    o.AddPolicy(StoreRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = storeLimit, Window = TimeSpan.FromMinutes(10) }));
    // QR scans update SQLite and can fan out to Google Sheets. Keep a high per-IP ceiling for event bursts,
    // while preventing one source from hammering the anonymous scan path.
    o.AddPolicy(DynamicQrEndpoints.ScanRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 300, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

// nginx terminates TLS on the same box and forwards to Kestrel on loopback (same as SecWeb).
services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

// Open the database (applying any new schema steps) and import old JSON data now, not on the first request,
// so a problem shows up at startup instead of in the middle of someone's scan.
app.Services.GetRequiredService<IDynamicQrStore>();

// Accounts, QR codes, orders and sign-in keys live in the data directory. In production it must sit outside the
// release folders, or every deploy starts empty (Store__DataDirectory in deploy/systemd/avi-site-prod.service).
// (Testing is throwaway on purpose: it keeps its data inside the release.)
if (app.Environment.IsProduction() && Path.GetFullPath(dataDir).StartsWith(Path.GetFullPath(builder.Environment.ContentRootPath), StringComparison.Ordinal))
    app.Logger.LogWarning("Data directory {Dir} is inside the release folder: it will be EMPTY after the next deploy. Set Store__DataDirectory to a path outside releases/.", Path.GetFullPath(dataDir));

app.UseForwardedHeaders();
app.UseSmsHostRouting(builder.Configuration["SmsSite:Host"] ?? "sms.avirobotics.org");
app.UseStoreComingSoon();
app.UseRouting(); // explicit, so routing sees the rewritten /sms path
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    // Nginx terminates TLS and sets HSTS; emitting it here produced a duplicate header.
}
app.UseBaselineSecurityHeaders();
if (app.Configuration.GetValue<bool>("Site:NoIndex"))
    app.Use((ctx, next) => { ctx.Response.Headers["X-Robots-Tag"] = "noindex, nofollow"; return next(); });

// Legacy pages from the old GitHub Pages site (QR codes point here). Served byte-for-byte, before the app's CSP.
var legacyRoot = Path.Combine(app.Environment.ContentRootPath, "legacy-static");
if (Directory.Exists(legacyRoot))
{
    var legacy = new PhysicalFileProvider(legacyRoot);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = legacy });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = legacy });
}

app.UseAppContentSecurityPolicy();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/healthz");
app.MapCartEndpoints();
app.MapPayPalEndpoints();
app.MapAdminEndpoints();
app.MapAccountAuth();
app.MapPeopleAdminEndpoints();
app.MapQrLookEndpoints();
app.MapHoursEndpoints();
app.MapDiscordInteractionEndpoints();
app.MapDiscordAccountEndpoints();
app.MapDiscordBotAdminEndpoints();
app.MapPeopleEndpoints();
app.MapProfileEndpoints();
app.MapDynamicQrEndpoints();
app.MapToolUsageEndpoints();
// Browsers ask for /favicon.ico on their own (and for PDFs, feeds, etc.): answer with the right mark per host.
app.MapGet("/favicon.ico", (HttpContext ctx, IWebHostEnvironment env) =>
{
    ctx.Response.Headers.CacheControl = "public, max-age=86400";
    return Results.File(Path.Combine(env.WebRootPath, "img", "brand", ctx.OnSmsHost() ? "sms-favicon.ico" : "favicon.ico"), "image/x-icon");
});
app.MapRazorComponents<App>();

app.Logger.LogInformation("Environment: {Env} · PayPal mode: {Mode} · store coming soon: {Soon}",
    app.Environment.EnvironmentName, payPalMode, app.Configuration.GetValue<bool>("Store:ComingSoon"));
app.Run();
return 0;

public partial class Program
{
    public const string StoreRateLimit = "store";
}
