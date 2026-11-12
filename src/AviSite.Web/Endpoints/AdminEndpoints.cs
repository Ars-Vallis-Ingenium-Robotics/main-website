using System.Security.Claims;
using System.Text;
using AviSite.Web.Options;
using AviSite.Web.Security;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Endpoints;

public static class AdminEndpoints
{
    public const string Scheme = "AviAdmin";
    public const string Policy = "ShopAdmin";
    public const string LoginRateLimit = "admin-login";

    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/admin");

        admin.MapPost("/login", async ([FromForm] string password, [FromForm] string? returnUrl, HttpContext http, IOptions<AdminOptions> options) =>
        {
            if (!AdminPassword.Verify(password, options.Value.PasswordHash))
                return Results.LocalRedirect("/admin/login?error=1");

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Admin")], Scheme);
            await http.SignInAsync(Scheme, new ClaimsPrincipal(identity));
            var target = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith("/admin", StringComparison.Ordinal) && !returnUrl.StartsWith("//") ? returnUrl : "/admin";
            return Results.LocalRedirect(target);
        }).RequireRateLimiting(LoginRateLimit);

        admin.MapPost("/logout", async (HttpContext http, Microsoft.AspNetCore.Antiforgery.IAntiforgery af) =>
        {
            // No form-bound parameters, so the antiforgery middleware won't check this one for us.
            if (!await af.IsRequestValidAsync(http)) return Results.BadRequest();
            await http.SignOutAsync(Scheme);
            return Results.LocalRedirect("/");
        });

        var secured = admin.MapGroup("").RequireAuthorization(Policy);

        secured.MapPost("/orders/{id}/pickup", async (string id, [FromForm] bool pickedUp, IOrderStore orders, TimeProvider clock) =>
        {
            await orders.UpdateAsync(id, o =>
            {
                if (o.Status != OrderStatus.Paid) return false;
                o.PickedUpUtc = pickedUp ? clock.GetUtcNow() : null;
                o.Log(pickedUp ? "Marked picked up" : "Pickup undone");
                return true;
            });
            return Results.LocalRedirect("/admin#order-" + Uri.EscapeDataString(id));
        });

        secured.MapGet("/orders.csv", async (IOrderStore orders) =>
        {
            var sb = new StringBuilder("code,created_utc,status,name,email,phone,product,size,qty,unit,line_total,order_total,picked_up\n");
            foreach (var o in await orders.ListAsync())
                foreach (var l in o.Lines)
                    sb.AppendJoin(',', Csv(o.Code), Csv(o.CreatedUtc.ToString("u")), Csv(o.Status.ToString()), Csv(o.Buyer.Name), Csv(o.Buyer.Email),
                        Csv(o.Buyer.Phone), Csv(l.Name), Csv(l.Size), l.Qty, Money.ToPayPal(l.UnitCents), Money.ToPayPal(l.LineCents),
                        Money.ToPayPal(o.TotalCents), o.PickedUpUtc is null ? "no" : "yes").Append('\n');
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"avi-orders-{DateTime.UtcNow:yyyyMMdd}.csv");
        });
    }

    /// <summary>CSV-escape and neutralise spreadsheet formula injection (=, +, -, @ prefixes).</summary>
    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}

public static class AdminAuthSetup
{
    public static IServiceCollection AddAdminAuth(this IServiceCollection services, bool isDevelopment)
    {
        services.AddAuthentication() // no default scheme: stays out of the way if ASP.NET Identity is added later
            .AddCookie(AdminEndpoints.Scheme, o =>
            {
                o.Cookie.Name = "avi_admin";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.Cookie.Path = "/admin";
                o.LoginPath = "/admin/login";
                o.AccessDeniedPath = "/admin/login";
                o.ExpireTimeSpan = TimeSpan.FromHours(8);
                o.SlidingExpiration = true;
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminEndpoints.Policy, p => p.AddAuthenticationSchemes(AdminEndpoints.Scheme).RequireAuthenticatedUser().RequireRole("Admin"));
        return services;
    }
}
