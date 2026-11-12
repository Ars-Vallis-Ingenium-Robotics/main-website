using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

public sealed record QuoteLine(Product Product, string Size, int Qty, int UnitCents)
{
    public int LineCents => UnitCents * Qty;
}

public sealed record Quote(IReadOnlyList<QuoteLine> Lines, int TotalCents, IReadOnlyList<string> Problems)
{
    public bool IsEmpty => Lines.Count == 0;
    public int ItemCount => Lines.Sum(l => l.Qty);
}

/// <summary>
/// The only place prices are computed. Everything that reaches PayPal comes from
/// here, never from the browser.
/// </summary>
public sealed class Pricing(ICatalog catalog, IOptions<StoreOptions> options)
{
    public Quote Price(IEnumerable<CartLine> cart)
    {
        var opts = options.Value;
        var lines = new List<QuoteLine>();
        var problems = new List<string>();

        foreach (var item in cart)
        {
            var product = catalog.Find(item.Slug);
            if (product is null) { problems.Add("An item in your cart is no longer available and was removed."); continue; }
            if (!catalog.IsOrderable(product)) { problems.Add($"{product.Name} is no longer taking orders and was removed."); continue; }
            var size = product.FindSize(item.Size);
            if (size is null) { problems.Add($"{product.Name} isn't available in {item.Size} and was removed."); continue; }

            var qty = Math.Clamp(item.Qty, 1, opts.MaxQuantityPerLine);
            if (qty != item.Qty) problems.Add($"{product.Name} ({size.Code}) quantity was adjusted to {qty}.");

            lines.Add(new QuoteLine(product, size.Code, qty, product.PriceCents + size.UpchargeCents));
            if (lines.Count >= opts.MaxLinesPerOrder) break;
        }

        return new Quote(lines, lines.Sum(l => l.LineCents), problems.Distinct().ToList());
    }
}
