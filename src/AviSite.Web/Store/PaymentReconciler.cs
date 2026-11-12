using AviSite.Web.Store.Payments;

namespace AviSite.Web.Store;

/// <summary>
/// Single place that turns a provider capture into an order state change.
/// Used by both the browser capture call and the PayPal webhook, so whichever arrives
/// first wins and the second is a no-op.
/// </summary>
public sealed class PaymentReconciler(IOrderStore orders, TimeProvider clock, ILogger<PaymentReconciler> log)
{
    public async Task<Order?> ApplyCaptureAsync(string orderId, CaptureResult capture, string source, CancellationToken ct)
    {
        return await orders.UpdateAsync(orderId, order =>
        {
            if (order.Status is OrderStatus.Paid or OrderStatus.Refunded) return false; // already settled

            // Declines stay Pending so the buyer can retry with another funding source;
            // PENDING captures wait for the webhook. Only the webhook marks an order Denied.
            if (!capture.Completed) return false;

            order.PayPalCaptureId = capture.CaptureId;

            if (capture.AmountCents != order.TotalCents || !string.Equals(capture.Currency, order.Currency, StringComparison.OrdinalIgnoreCase))
            {
                order.Status = OrderStatus.NeedsReview;
                order.Log($"Captured {capture.AmountCents} {capture.Currency} but expected {order.TotalCents} {order.Currency} ({source})");
                log.LogError("Amount mismatch on order {Code}: captured {Captured} {CapCur}, expected {Expected} {ExpCur}",
                    order.Code, capture.AmountCents, capture.Currency, order.TotalCents, order.Currency);
                return true;
            }

            order.Status = OrderStatus.Paid;
            order.PaidUtc = clock.GetUtcNow();
            order.Log($"Paid via PayPal capture {capture.CaptureId} ({source})");
            log.LogInformation("Order {Code} paid ({Source})", order.Code, source);
            return true;
        }, ct);
    }

    /// <summary>Moves an order to <paramref name="status"/> only from one of the allowed <paramref name="from"/> states.</summary>
    public Task<Order?> MarkAsync(string orderId, OrderStatus status, string note, CancellationToken ct, OrderStatus[] from) =>
        orders.UpdateAsync(orderId, order =>
        {
            if (order.Status == status) return false;
            if (!from.Contains(order.Status))
            {
                order.Log($"Ignored: {note} (order was {order.Status})");
                log.LogWarning("Ignored transition {From}->{To} on {Code}: {Note}", order.Status, status, order.Code, note);
                return true;
            }
            order.Status = status;
            order.Log(note);
            return true;
        }, ct);

    public Task<Order?> NoteAsync(string orderId, string note, CancellationToken ct) =>
        orders.UpdateAsync(orderId, order => { order.Log(note); return true; }, ct);
}
