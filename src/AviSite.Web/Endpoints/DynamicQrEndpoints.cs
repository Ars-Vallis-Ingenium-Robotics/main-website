using AviSite.Web.Store;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Endpoints;

/// <summary>
/// /dynamicqr/{slug}: the address printed on our QR codes. Counts the scan, tells the Google Sheet
/// (production only, in the background), then redirects. The old static pages did the same in the browser.
/// </summary>
public static class DynamicQrEndpoints
{
    public const string ScanRateLimit = "dynamic-qr-scan";

    public static void MapDynamicQrEndpoints(this WebApplication app)
    {
        app.MapMethods("/dynamicqr/{slug}", ["GET"], Handle).RequireRateLimiting(ScanRateLimit);
        app.MapMethods("/dynamicqr/{slug}", ["HEAD"], Handle);
        app.MapMethods("/dynamicqr/{slug}/index.html", ["GET"], Handle).RequireRateLimiting(ScanRateLimit);
        app.MapMethods("/dynamicqr/{slug}/index.html", ["HEAD"], Handle);
    }

    private static async Task<IResult> Handle(string slug, HttpContext http, IDynamicQrStore store,
        IHttpClientFactory clients, IOptions<DynamicQrOptions> options, ILoggerFactory logs, CancellationToken ct)
    {
        // HEAD (link checkers, previews) redirects without counting.
        var code = HttpMethods.IsHead(http.Request.Method) ? await store.GetAsync(slug, ct) : await store.ScanAsync(slug, http.Request.Headers.UserAgent.ToString(), ct);
        if (code is null) return Results.NotFound();
        http.Response.Headers.CacheControl = "no-store"; // every scan must reach us
        if (!code.Active) return Results.Redirect("/");

        var o = options.Value;
        if (!HttpMethods.IsHead(http.Request.Method) && o.SendToGoogleSheet && !string.IsNullOrWhiteSpace(o.GoogleScriptUrl))
        {
            var url = $"{o.GoogleScriptUrl}?ua={Uri.EscapeDataString(http.Request.Headers.UserAgent.ToString())}&campaign={Uri.EscapeDataString(code.Campaign)}";
            var log = logs.CreateLogger("DynamicQr");
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    using var res = await clients.CreateClient("google-sheet").GetAsync(url, cts.Token);
                }
                catch (Exception ex) { log.LogWarning(ex, "Google Sheet ping failed for {Slug}", code.Slug); }
            });
        }
        return Results.Redirect(code.Target);
    }
}
