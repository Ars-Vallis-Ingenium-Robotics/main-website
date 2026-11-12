using System.Globalization;

namespace AviSite.Web.Store;

public sealed class Product
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string Tagline { get; init; } = "";
    public string Description { get; init; } = "";
    public required int PriceCents { get; init; }
    public IReadOnlyList<ProductSize> Sizes { get; init; } = [];
    public string? Image { get; init; }
    /// <summary>Photo keys from photos.json (real photos of the product being worn). First one is the main image.</summary>
    public IReadOnlyList<string> Photos { get; init; } = [];
    public string? ImageAlt { get; init; }
    /// <summary>Optional team tag, e.g. "sms", used for accent color.</summary>
    public string? Team { get; init; }
    public bool Active { get; init; } = true;
    /// <summary>Last day (Pacific time) orders are accepted. Null = open-ended.</summary>
    public DateOnly? OrdersClose { get; init; }

    public ProductSize? FindSize(string code) =>
        Sizes.FirstOrDefault(s => string.Equals(s.Code, code, StringComparison.OrdinalIgnoreCase));
}

public sealed record ProductSize(string Code, int UpchargeCents = 0);

public sealed record CartLine(string Slug, string Size, int Qty);

public enum OrderStatus
{
    /// <summary>Created at checkout, not paid yet.</summary>
    Pending,
    Paid,
    /// <summary>PayPal reported a capture whose amount/currency didn't match. A human must look.</summary>
    NeedsReview,
    Denied,
    Refunded,
    Cancelled,
}

public sealed class Buyer
{
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
}

public sealed record OrderLine(string Slug, string Name, string Size, int Qty, int UnitCents)
{
    public int LineCents => UnitCents * Qty;
}

public sealed record OrderEvent(DateTimeOffset AtUtc, string What);

public sealed class Order
{
    /// <summary>Unguessable id used in URLs (128 bits, base64url).</summary>
    public required string Id { get; init; }
    /// <summary>Short human code for pickup lists, e.g. AVI-7KQ3M.</summary>
    public required string Code { get; init; }
    public required DateTimeOffset CreatedUtc { get; init; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public required Buyer Buyer { get; init; }
    public required IReadOnlyList<OrderLine> Lines { get; init; }
    public required int TotalCents { get; init; }
    public required string Currency { get; init; }

    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
    public DateTimeOffset? PaidUtc { get; set; }
    public DateTimeOffset? PickedUpUtc { get; set; }
    public List<OrderEvent> History { get; init; } = [];

    public void Log(string what) => History.Add(new OrderEvent(DateTimeOffset.UtcNow, what));
}

public static class Money
{
    /// <summary>Display format: $25.00</summary>
    public static string Format(int cents) =>
        (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>PayPal wire format: "25.00" (invariant, two decimals).</summary>
    public static string ToPayPal(int cents) =>
        (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static bool TryParsePayPal(string? value, out int cents)
    {
        cents = 0;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)) return false;
        var scaled = d * 100m;
        if (scaled != decimal.Truncate(scaled) || scaled < 0 || scaled > int.MaxValue) return false;
        cents = (int)scaled;
        return true;
    }
}
