using System.Text.Json;
using AviSite.Web.Store;

namespace AviSite.Web.Endpoints;

/// <summary>
/// Saved looks for dynamic QR codes. The QR maker (/tools/qr?code=…) reads and writes these with fetch(),
/// so "QR image" always opens with the code's look and whoever reprints it gets the same design.
///
/// CSRF: the admin cookie is SameSite=Strict (never sent from other sites), the POST must be JSON
/// (a cross-site form can't send that), and the Origin header must be this site.
/// </summary>
public static class QrLookEndpoints
{
    private const int MaxLook = 8_000;
    private const int MaxLogo = 700_000; // the maker shrinks uploads to 640px PNG; this is generous

    public sealed record LookBody(JsonElement? Look, string? Logo);

    public static void MapQrLookEndpoints(this WebApplication app)
    {
        var looks = app.MapGroup("/admin/qr/{slug}/look").RequireAuthorization(AdminEndpoints.QrPolicy);

        looks.MapGet("", async (string slug, HttpContext http, IDynamicQrStore codes, IAccountStore store) =>
        {
            var c = await codes.GetAsync(slug);
            if (c is null || !DynamicQrRules.CanAccess(await http.AccessAsync(store), c)) return Results.NotFound();
            http.Response.Headers.CacheControl = "no-store";
            return Results.Json(new
            {
                slug = c.Slug,
                look = c.Look is null ? (JsonElement?)null : JsonDocument.Parse(c.Look).RootElement,
                logo = c.LookLogo,
                updated = c.LookUpdatedUtc,
            });
        });

        looks.MapPost("", async (string slug, LookBody body, HttpContext http, IDynamicQrStore codes, IAccountStore store, TimeProvider clock) =>
        {
            var origin = http.Request.Headers.Origin.ToString();
            if (origin != $"{http.Request.Scheme}://{http.Request.Host}") return Results.BadRequest();
            var c = await codes.GetAsync(slug);
            if (c is null || !DynamicQrRules.CanAccess(await http.AccessAsync(store), c)) return Results.NotFound();

            string? look = null;
            if (body.Look is { ValueKind: JsonValueKind.Object } l)
            {
                // Keep only short scalar settings: it's a set of colors, shapes and numbers, never markup.
                var clean = l.EnumerateObject()
                    .Where(p => p.Name.Length <= 20 && p.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                    .Where(p => p.Value.ValueKind != JsonValueKind.String || p.Value.GetString()!.Length <= 60)
                    .ToDictionary(p => p.Name, p => p.Value);
                look = JsonSerializer.Serialize(clean);
                if (look.Length > MaxLook) return Results.BadRequest();
            }
            var logo = body.Logo;
            if (logo is not null && (!logo.StartsWith("data:image/png;base64,", StringComparison.Ordinal) || logo.Length > MaxLogo)) return Results.BadRequest();

            var by = http.User.FindFirst(AccountAuth.EmailClaim)?.Value ?? http.User.Identity?.Name;
            await codes.UpdateAsync(slug, x =>
            {
                x.Look = look;
                x.LookLogo = look is null ? null : logo;
                x.LookUpdatedUtc = clock.GetUtcNow();
                x.LookBy = by;
            });
            return Results.NoContent();
        }).DisableAntiforgery();
    }
}
