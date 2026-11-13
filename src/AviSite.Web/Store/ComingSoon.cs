using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>While Store:ComingSoon is on, everything that leads to buying a product bounces back to /shop.</summary>
public static class ComingSoonGate
{
    // Donation receipts and payment pages live under /shop/orders and /shop/pay, so those stay open.
    private static readonly string[] Open = ["/shop/orders/", "/shop/pay/"];

    public static IApplicationBuilder UseStoreComingSoon(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var store = ctx.RequestServices.GetRequiredService<IOptionsMonitor<StoreOptions>>().CurrentValue;
        var path = ctx.Request.Path.Value ?? "/";
        if (store.ComingSoon
            && (path.StartsWith("/cart/", StringComparison.OrdinalIgnoreCase)
                || (path.StartsWith("/shop/", StringComparison.OrdinalIgnoreCase)
                    && !Open.Any(o => path.StartsWith(o, StringComparison.OrdinalIgnoreCase)))))
        {
            ctx.Response.Redirect("/shop");
            return;
        }
        await next();
    });
}
