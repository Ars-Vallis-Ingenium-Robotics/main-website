using System.Text.Json;
using AviSite.Web.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>
/// Cart stored client-side in a data-protected cookie (no server session needed).
/// Contents are only slug/size/qty; prices are always recomputed by <see cref="Pricing"/>.
/// </summary>
public sealed class CartCookie(IDataProtectionProvider dp, IHttpContextAccessor http, IOptions<StoreOptions> options, IWebHostEnvironment env)
{
    public const string CookieName = "avi_cart";
    private readonly IDataProtector _protector = dp.CreateProtector("AviSite.Cart.v1");

    private HttpContext Context => http.HttpContext ?? throw new InvalidOperationException("No active HTTP request.");

    public List<CartLine> Read()
    {
        if (!Context.Request.Cookies.TryGetValue(CookieName, out var raw) || string.IsNullOrEmpty(raw)) return [];
        try
        {
            var json = _protector.Unprotect(raw);
            var lines = JsonSerializer.Deserialize<List<CartLine>>(json, JsonDefaults.Options) ?? [];
            return lines
                .Where(l => !string.IsNullOrWhiteSpace(l.Slug) && !string.IsNullOrWhiteSpace(l.Size) && l.Qty > 0)
                .Take(options.Value.MaxLinesPerOrder)
                .ToList();
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            return []; // tampered, from an old key ring, or garbage: start fresh
        }
    }

    public int Count() => Read().Sum(l => l.Qty);

    public void Write(IReadOnlyCollection<CartLine> lines)
    {
        if (lines.Count == 0) { Clear(); return; }
        var json = JsonSerializer.Serialize(lines, JsonDefaults.Options);
        Context.Response.Cookies.Append(CookieName, _protector.Protect(json), new CookieOptions
        {
            HttpOnly = true,
            Secure = Context.Request.IsHttps || !env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            IsEssential = true, // strictly necessary for the shop to work
            MaxAge = TimeSpan.FromDays(14),
            Path = "/",
        });
    }

    public void Clear() => Context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });

    /// <summary>Adds or merges a line, respecting the per-line quantity cap.</summary>
    public void Add(string slug, string size, int qty)
    {
        var max = options.Value.MaxQuantityPerLine;
        var lines = Read();
        var i = lines.FindIndex(l => l.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase) && l.Size.Equals(size, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) lines[i] = lines[i] with { Qty = Math.Min(max, lines[i].Qty + qty) };
        else if (lines.Count < options.Value.MaxLinesPerOrder) lines.Add(new CartLine(slug, size, Math.Clamp(qty, 1, max)));
        Write(lines);
    }

    public void SetQuantity(int index, int qty)
    {
        var lines = Read();
        if (index < 0 || index >= lines.Count) return;
        if (qty <= 0) lines.RemoveAt(index);
        else lines[index] = lines[index] with { Qty = Math.Min(options.Value.MaxQuantityPerLine, qty) };
        Write(lines);
    }
}
