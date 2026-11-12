using System.Collections.Concurrent;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store.Payments;

public sealed record CaptureResult(bool Completed, string Status, string? CaptureId, int AmountCents, string? Currency);

public interface IPaymentGateway
{
    PayPalMode Mode { get; }

    /// <summary>Creates a provider order for the stored order's server-computed lines. Returns the provider order id.</summary>
    Task<string> CreateOrderAsync(Order order, CancellationToken ct);

    /// <summary>Captures an approved provider order (idempotent per local order).</summary>
    Task<CaptureResult> CaptureAsync(string providerOrderId, string localOrderId, CancellationToken ct);

    /// <summary>Verifies a webhook delivery is genuinely from the provider.</summary>
    Task<bool> VerifyWebhookAsync(IHeaderDictionary headers, string body, CancellationToken ct);
}

/// <summary>
/// Development-only stand-in so the whole checkout can be clicked through without PayPal.
/// Program.cs refuses to start with this gateway outside Development.
/// </summary>
public sealed class FakePaymentGateway(IOptions<PayPalOptions> options) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, (int Cents, string Currency)> _created = new();

    public PayPalMode Mode => PayPalMode.Fake;

    public Task<string> CreateOrderAsync(Order order, CancellationToken ct)
    {
        var id = "FAKE-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        _created[id] = (order.TotalCents, options.Value.Currency);
        return Task.FromResult(id);
    }

    public Task<CaptureResult> CaptureAsync(string providerOrderId, string localOrderId, CancellationToken ct) =>
        Task.FromResult(_created.TryGetValue(providerOrderId, out var o)
            ? new CaptureResult(true, "COMPLETED", "FAKECAP-" + providerOrderId[5..], o.Cents, o.Currency)
            : new CaptureResult(false, "NOT_FOUND", null, 0, null));

    public Task<bool> VerifyWebhookAsync(IHeaderDictionary headers, string body, CancellationToken ct) => Task.FromResult(false);
}
