using System.Threading.RateLimiting;
using AviSite.Web.Components;
using AviSite.Web.Endpoints;
using AviSite.Web.Options;
using AviSite.Web.Security;
using AviSite.Web.Store;
using AviSite.Web.Store.Payments;
using Microsoft.AspNetCore.DataProtection;
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
services.AddSingleton<Pricing>();
services.AddSingleton<PaymentReconciler>();
services.AddScoped<CartCookie>();

var payPalMode = builder.Configuration.GetValue<PayPalMode?>("PayPal:Mode") ?? PayPalMode.Fake;
if (payPalMode == PayPalMode.Fake)
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("PayPal:Mode=Fake is only allowed in Development. Set Sandbox or Live with credentials.");
    services.AddSingleton<IPaymentGateway, FakePaymentGateway>();
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
services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy(AdminEndpoints.LoginRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(5) }));
    // Checkout + PayPal API: each call can create an order and/or hit PayPal.
    o.AddPolicy(StoreRateLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(10) }));
});

// nginx terminates TLS on the same box and forwards to Kestrel on loopback (same as SecWeb).
services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseBaselineSecurityHeaders();

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
app.MapRazorComponents<App>();

app.Logger.LogInformation("PayPal mode: {Mode}", payPalMode);
app.Run();
return 0;

public partial class Program
{
    public const string StoreRateLimit = "store";
}
