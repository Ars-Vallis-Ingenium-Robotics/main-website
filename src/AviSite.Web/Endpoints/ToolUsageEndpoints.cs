using AviSite.Web.Store;

namespace AviSite.Web.Endpoints;

/// <summary>
/// POST /api/use/{key}: the QR maker's download buttons send navigator.sendBeacon here. Body is ignored,
/// nothing is stored but the counter. Same-origin only, rate limited per address (in memory, never saved).
/// </summary>
public static class ToolUsageEndpoints
{
    public const string RateLimit = "tool-usage";

    public static void MapToolUsageEndpoints(this WebApplication app)
    {
        app.MapPost("/api/use/{key}", async (string key, HttpContext http, IToolUsageStore store, CancellationToken ct) =>
        {
            var site = http.Request.Headers["Sec-Fetch-Site"].ToString();
            // Other sites can't post counts; ".open" is counted by the page itself. Plain 400 either way.
            if (site is not ("" or "same-origin") || !ToolUsageEvents.IsKnown(key) || key.EndsWith(".open")) return Results.BadRequest();
            if (!ToolUsageEvents.LooksLikeBot(http.Request.Headers.UserAgent.ToString())) await store.CountAsync(key, ct);
            return Results.NoContent();
        }).DisableAntiforgery().RequireRateLimiting(RateLimit);
    }
}
