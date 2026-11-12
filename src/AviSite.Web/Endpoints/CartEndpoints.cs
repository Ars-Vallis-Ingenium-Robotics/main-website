using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>Plain HTML form posts (antiforgery-validated) → redirect. Works with JavaScript off.</summary>
public static class CartEndpoints
{
    public static void MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        var cart = app.MapGroup("/cart");

        cart.MapPost("/add", ([FromForm] string slug, [FromForm] string? size, [FromForm] int? qty, ICatalog catalog, CartCookie cookie) =>
        {
            var product = catalog.Find(slug);
            if (product is null || !catalog.IsOrderable(product)) return Results.LocalRedirect("/shop?error=unavailable");

            var chosen = size is null ? null : product.FindSize(size);
            if (chosen is null) return Results.LocalRedirect($"/shop/{Uri.EscapeDataString(product.Slug)}?error=size");

            cookie.Add(product.Slug, chosen.Code, Math.Max(1, qty ?? 1));
            return Results.LocalRedirect($"/shop/cart?added={Uri.EscapeDataString(product.Slug)}");
        });

        cart.MapPost("/update", ([FromForm] int index, [FromForm] int qty, CartCookie cookie) =>
        {
            cookie.SetQuantity(index, qty);
            return Results.LocalRedirect("/shop/cart");
        });

        cart.MapPost("/remove", ([FromForm] int index, CartCookie cookie) =>
        {
            cookie.SetQuantity(index, 0);
            return Results.LocalRedirect("/shop/cart");
        });
    }
}
