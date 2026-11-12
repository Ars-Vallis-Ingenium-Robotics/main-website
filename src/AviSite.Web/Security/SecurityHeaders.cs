namespace AviSite.Web.Security;

public static class SecurityHeaders
{
    // PayPal's JS SDK needs its own script/frame/connect/img origins. Inline styles are
    // allowed only because the PayPal button iframe host injects them.
    private const string Csp =
        "default-src 'self'; " +
        "script-src 'self' 'wasm-unsafe-eval' https://www.paypal.com https://*.paypal.com https://*.paypalobjects.com; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob: https://i.ytimg.com https://*.paypal.com https://*.paypalobjects.com; " +
        "font-src 'self'; " +
        "connect-src 'self' https://*.paypal.com https://*.paypalobjects.com; " +
        "frame-src https://*.paypal.com https://*.paypalobjects.com https://www.youtube-nocookie.com; " + // SMS videos (YouTube)
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Baseline headers for every response, including legacy static pages.</summary>
    public static IApplicationBuilder UseBaselineSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "strict-origin-when-cross-origin";
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(self \"https://www.paypal.com\")";
            return Task.CompletedTask;
        });
        await next();
    });

    /// <summary>Strict CSP for the app's own pages. Registered after legacy static files so old pages keep working as-is.</summary>
    public static IApplicationBuilder UseAppContentSecurityPolicy(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers.ContentSecurityPolicy = Csp;
            ctx.Response.Headers.XFrameOptions = "DENY";
            return Task.CompletedTask;
        });
        await next();
    });
}
