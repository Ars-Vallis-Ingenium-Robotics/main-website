using System.Text.Json.Nodes;
using AviSite.Web.Store;
using AviSite.Web.Store.Payments;
using Microsoft.AspNetCore.Antiforgery;

namespace AviSite.Web.Endpoints;

public static class PayPalEndpoints
{
    public sealed record CaptureRequest(string PaypalOrderId);

    public static void MapPayPalEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/paypal");
        var store = api.MapGroup("").RequireRateLimiting(Program.StoreRateLimit);

        // Called by the PayPal button's createOrder(). Body is ignored: amounts come from the stored order.
        store.MapPost("/orders/{orderId}/create", async (string orderId, HttpContext http, IAntiforgery af,
            IOrderStore orders, IPaymentGateway gateway, ILoggerFactory lf, CancellationToken ct) =>
        {
            if (!await af.IsRequestValidAsync(http)) return Results.BadRequest(new { error = "Session expired. Refresh the page." });

            var order = await orders.GetAsync(orderId, ct);
            if (order is null) return Results.NotFound();
            if (order.Status != OrderStatus.Pending)
                return Results.Conflict(new { error = "This order is already settled.", redirect = $"/shop/orders/{order.Id}" });

            try
            {
                var providerId = await gateway.CreateOrderAsync(order, ct);
                await orders.UpdateAsync(order.Id, o =>
                {
                    if (o.Status != OrderStatus.Pending) return false;
                    o.PayPalOrderId = providerId;
                    o.Log($"PayPal order {providerId} created");
                    return true;
                }, ct);
                return Results.Ok(new { id = providerId });
            }
            catch (Exception ex) when (ex is PayPalException or HttpRequestException or TaskCanceledException)
            {
                lf.CreateLogger("PayPal").LogError(ex, "Create PayPal order failed for {Code}", order.Code);
                return Results.Problem("We couldn't reach PayPal. Please try again in a minute.", statusCode: 502);
            }
        });

        // Called by onApprove(). Captures server-side and verifies the amount before marking paid.
        store.MapPost("/orders/{orderId}/capture", async (string orderId, CaptureRequest body, HttpContext http, IAntiforgery af,
            IOrderStore orders, IPaymentGateway gateway, PaymentReconciler reconciler, CartCookie cart, ILoggerFactory lf, CancellationToken ct) =>
        {
            if (!await af.IsRequestValidAsync(http)) return Results.BadRequest(new { error = "Session expired. Refresh the page." });

            var order = await orders.GetAsync(orderId, ct);
            if (order is null) return Results.NotFound();
            if (string.IsNullOrEmpty(order.PayPalOrderId) || order.PayPalOrderId != body.PaypalOrderId)
                return Results.BadRequest(new { error = "Payment doesn't match this order." });
            if (order.Status != OrderStatus.Pending)
                return Results.Ok(new { redirect = $"/shop/orders/{order.Id}" });

            CaptureResult result;
            try { result = await gateway.CaptureAsync(order.PayPalOrderId, order.Id, ct); }
            catch (Exception ex) when (ex is PayPalException or HttpRequestException or TaskCanceledException)
            {
                lf.CreateLogger("PayPal").LogError(ex, "Capture failed for {Code}", order.Code);
                return Results.Problem("PayPal didn't confirm the payment yet. Don't pay again. Check your email or contact us.", statusCode: 502);
            }

            if (!result.Completed && result.Status is "INSTRUMENT_DECLINED" or "DECLINED")
                return Results.UnprocessableEntity(new { error = "PayPal declined that payment. Try another card or funding source.", retry = true });
            if (!result.Completed && result.Status != "PENDING")
                return Results.Problem("PayPal didn't confirm the payment. You have not been charged. Please try again.", statusCode: 502);

            // COMPLETED → Paid (or NeedsReview on mismatch). PENDING → stays Pending until the webhook settles it.
            var updated = await reconciler.ApplyCaptureAsync(order.Id, result, "browser", ct);
            if (updated?.Status is OrderStatus.Paid or OrderStatus.NeedsReview) cart.Clear(); // money was taken either way
            return Results.Ok(new { redirect = $"/shop/orders/{order.Id}" });
        });

        // PayPal → us. Verified with PayPal's verify-webhook-signature API. Never trusts the body without that.
        api.MapPost("/webhook", async (HttpRequest req, IPaymentGateway gateway, IOrderStore orders, PaymentReconciler reconciler,
            ILoggerFactory lf, CancellationToken ct) =>
        {
            var log = lf.CreateLogger("PayPal.Webhook");
            // Cheap rejections before buffering anything or calling PayPal.
            if (string.IsNullOrEmpty(req.Headers["PAYPAL-TRANSMISSION-ID"]) || string.IsNullOrEmpty(req.Headers["PAYPAL-TRANSMISSION-SIG"]))
                return Results.BadRequest();
            var sizeFeature = req.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = 256 * 1024;
            if (req.ContentLength > 256 * 1024) return Results.StatusCode(413);
            string body;
            try { using var reader = new StreamReader(req.Body); body = await reader.ReadToEndAsync(ct); }
            catch (BadHttpRequestException) { return Results.StatusCode(413); }
            if (!await gateway.VerifyWebhookAsync(req.Headers, body, ct))
            {
                log.LogWarning("Rejected unverified webhook");
                return Results.BadRequest();
            }

            var evt = JsonNode.Parse(body);
            var type = evt?["event_type"]?.GetValue<string>();
            var resource = evt?["resource"];
            var customId = resource?["custom_id"]?.GetValue<string>();
            var relatedOrderId = resource?["supplementary_data"]?["related_ids"]?["order_id"]?.GetValue<string>();
            var captureId = type == "PAYMENT.CAPTURE.REFUNDED" ? UpLinkCaptureId(resource) : resource?["id"]?.GetValue<string>();

            var order = customId is not null ? await orders.GetAsync(customId, ct) : null;
            order ??= await orders.FindByPayPalAsync(relatedOrderId, captureId, ct);
            if (order is null)
            {
                log.LogWarning("Webhook {Type} for unknown order (custom_id={CustomId}, paypal={PayPalOrder})", type, customId, relatedOrderId);
                return Results.Ok(); // ack so PayPal stops retrying; nothing to update
            }

            switch (type)
            {
                case "PAYMENT.CAPTURE.COMPLETED":
                    Money.TryParsePayPal(resource?["amount"]?["value"]?.GetValue<string>(), out var cents);
                    var currency = resource?["amount"]?["currency_code"]?.GetValue<string>();
                    await reconciler.ApplyCaptureAsync(order.Id, new CaptureResult(true, "COMPLETED", captureId, cents, currency), "webhook", ct);
                    break;
                case "PAYMENT.CAPTURE.DENIED":
                case "PAYMENT.CAPTURE.DECLINED":
                    await reconciler.MarkAsync(order.Id, OrderStatus.Denied, $"PayPal: {type}", ct, from: [OrderStatus.Pending]);
                    break;
                case "PAYMENT.CAPTURE.REVERSED":
                    await reconciler.MarkAsync(order.Id, OrderStatus.Refunded, $"PayPal: {type}", ct, from: [OrderStatus.Paid, OrderStatus.NeedsReview]);
                    break;
                case "PAYMENT.CAPTURE.REFUNDED":
                    // Refund resource carries the refunded amount; partial refunds keep the order Paid (still owed a shirt?) and are logged for a human.
                    Money.TryParsePayPal(resource?["amount"]?["value"]?.GetValue<string>(), out var refunded);
                    if (refunded >= order.TotalCents)
                        await reconciler.MarkAsync(order.Id, OrderStatus.Refunded, "PayPal: full refund", ct, from: [OrderStatus.Paid, OrderStatus.NeedsReview]);
                    else
                        await reconciler.NoteAsync(order.Id, $"PayPal: partial refund of {Money.Format(refunded)}", ct);
                    break;
                default:
                    log.LogInformation("Ignoring webhook {Type} for {Code}", type, order.Code);
                    break;
            }
            return Results.Ok();
        }).DisableAntiforgery();
    }

    private static string? UpLinkCaptureId(JsonNode? refund)
    {
        // Refund resources link to their capture: { "rel": "up", "href": ".../v2/payments/captures/{id}" }
        if (refund?["links"] is not JsonArray links) return null;
        foreach (var link in links)
        {
            if (link?["rel"]?.GetValue<string>() != "up") continue;
            var href = link["href"]?.GetValue<string>();
            var i = href?.LastIndexOf("/captures/", StringComparison.Ordinal) ?? -1;
            if (i >= 0) return href![(i + "/captures/".Length)..];
        }
        return null;
    }
}
