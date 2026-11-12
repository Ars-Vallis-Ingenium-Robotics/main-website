namespace AviSite.Web.Options;

/// <summary>
/// The Sequoia Marine Solutions microsite lives at /sms on avirobotics.org and, once DNS exists,
/// at the root of sms.avirobotics.org. Same pages, both addresses.
/// </summary>
public sealed class SmsSiteOptions
{
    public const string Section = "SmsSite";

    /// <summary>Host that serves the SMS site at its root, e.g. "sms.avirobotics.org". Also matches "sms.localhost" for testing.</summary>
    public string Host { get; set; } = "sms.avirobotics.org";

    /// <summary>Absolute AVI URL used for "back to AVI" links when browsing on the SMS host.</summary>
    public string AviBaseUrl { get; set; } = "https://avirobotics.org";
}

public static class SmsHostRouting
{
    public const string Prefix = "/sms";
    private const string ItemKey = "AviSite.OnSmsHost";

    // Paths that are shared by both hosts and must not be rewritten under /sms.
    private static readonly string[] Shared =
        ["/css/", "/js/", "/img/", "/fonts/", "/_framework/", "/favicon", "/site.webmanifest", "/healthz",
         "/cart", "/shop", "/api/", "/admin", "/error", "/not-found"];

    public static bool IsSmsHost(HttpContext ctx, string smsHost)
    {
        var host = ctx.Request.Host.Host;
        return host.Equals(smsHost, StringComparison.OrdinalIgnoreCase)
            || host.Equals("sms.localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>On sms.* hosts, serve /x from /sms/x so both addresses share one set of pages.</summary>
    public static IApplicationBuilder UseSmsHostRouting(this IApplicationBuilder app, string smsHost) => app.Use(async (ctx, next) =>
    {
        if (IsSmsHost(ctx, smsHost))
        {
            ctx.Items[ItemKey] = true;
            var path = ctx.Request.Path.Value ?? "/";
            if (path.StartsWith(Prefix + "/", StringComparison.OrdinalIgnoreCase) || path.Equals(Prefix, StringComparison.OrdinalIgnoreCase))
            {
                // Canonical form on the SMS host has no /sms prefix.
                var rest = path[Prefix.Length..];
                ctx.Response.Redirect((string.IsNullOrEmpty(rest) ? "/" : rest) + ctx.Request.QueryString, permanent: true);
                return;
            }
            if (!Shared.Any(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase)))
                ctx.Request.Path = Prefix + (path == "/" ? "" : path);
        }
        await next();
    });

    public static bool OnSmsHost(this HttpContext? ctx) => ctx?.Items.ContainsKey(ItemKey) == true;
}
