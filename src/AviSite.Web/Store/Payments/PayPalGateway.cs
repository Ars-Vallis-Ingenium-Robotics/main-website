using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store.Payments;

/// <summary>
/// PayPal Orders v2 over plain HttpClient (no SDK dependency).
/// Docs: https://developer.paypal.com/docs/api/orders/v2/
/// </summary>
public sealed class PayPalGateway(HttpClient http, IOptions<PayPalOptions> options, TimeProvider clock, ILogger<PayPalGateway> log) : IPaymentGateway
{
    private readonly PayPalOptions _opt = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public PayPalMode Mode => _opt.Mode;

    public async Task<string> CreateOrderAsync(Order order, CancellationToken ct)
    {
        var items = new JsonArray();
        foreach (var l in order.Lines)
        {
            items.Add(new JsonObject
            {
                ["name"] = Truncate(l.Slug == Donations.Slug ? l.Name : $"{l.Name} ({l.Size})", 127),
                ["sku"] = Truncate(l.Slug == Donations.Slug ? l.Slug : $"{l.Slug}-{l.Size}", 127),
                ["quantity"] = l.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["unit_amount"] = Amount(l.UnitCents, order.Currency),
                ["category"] = l.Slug == Donations.Slug ? "DONATION" : "PHYSICAL_GOODS",
            });
        }

        var body = new JsonObject
        {
            ["intent"] = "CAPTURE",
            ["purchase_units"] = new JsonArray
            {
                new JsonObject
                {
                    ["reference_id"] = order.Id,
                    ["custom_id"] = order.Id,      // echoed back on captures/webhooks
                    ["invoice_id"] = order.Code,   // must be unique per PayPal account
                    ["description"] = Truncate(order.IsDonation ? $"Donation to {_opt.BrandName} ({order.Code})" : $"{_opt.BrandName} order {order.Code}", 127),
                    ["amount"] = new JsonObject
                    {
                        ["currency_code"] = order.Currency,
                        ["value"] = Money.ToPayPal(order.TotalCents),
                        ["breakdown"] = new JsonObject { ["item_total"] = Amount(order.TotalCents, order.Currency) },
                    },
                    ["items"] = items,
                },
            },
            ["payment_source"] = new JsonObject
            {
                ["paypal"] = new JsonObject
                {
                    ["experience_context"] = new JsonObject
                    {
                        ["brand_name"] = Truncate(_opt.BrandName, 127),
                        ["shipping_preference"] = "NO_SHIPPING", // pickup at meetings
                        ["user_action"] = "PAY_NOW",
                    },
                },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "v2/checkout/orders") { Content = Json(body) };
        req.Headers.Add("Prefer", "return=minimal");
        using var res = await SendAsync(req, ct);
        var json = await ReadAsync(res, ct);
        if (!res.IsSuccessStatusCode) throw new PayPalException($"Create order failed ({(int)res.StatusCode})", json);
        return json?["id"]?.GetValue<string>() ?? throw new PayPalException("Create order returned no id", json);
    }

    public async Task<CaptureResult> CaptureAsync(string providerOrderId, string localOrderId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"v2/checkout/orders/{Uri.EscapeDataString(providerOrderId)}/capture")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        // No PayPal-Request-Id on purpose: after INSTRUMENT_DECLINED the buyer re-approves the same PayPal order and we must
        // retry the capture for real. Double-capture is still impossible: PayPal answers ORDER_ALREADY_CAPTURED (handled below).
        req.Headers.Add("Prefer", "return=representation");
        using var res = await SendAsync(req, ct);
        var json = await ReadAsync(res, ct);

        if (!res.IsSuccessStatusCode)
        {
            // Already captured (e.g. webhook raced us, or user double-clicked): read the order instead.
            var raw = json?.ToJsonString() ?? "";
            if (res.StatusCode == HttpStatusCode.UnprocessableEntity && raw.Contains("ORDER_ALREADY_CAPTURED"))
                return await GetOrderCaptureAsync(providerOrderId, ct);
            if (res.StatusCode == HttpStatusCode.UnprocessableEntity && raw.Contains("INSTRUMENT_DECLINED"))
                return new CaptureResult(false, "INSTRUMENT_DECLINED", null, 0, null);
            log.LogWarning("PayPal capture failed for {PayPalOrder}: {Status} {Body}", providerOrderId, (int)res.StatusCode, json?.ToJsonString());
            return new CaptureResult(false, $"HTTP_{(int)res.StatusCode}", null, 0, null);
        }
        return ParseCapture(json);
    }

    private async Task<CaptureResult> GetOrderCaptureAsync(string providerOrderId, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"v2/checkout/orders/{Uri.EscapeDataString(providerOrderId)}");
        using var res = await SendAsync(req, ct);
        var json = await ReadAsync(res, ct);
        return res.IsSuccessStatusCode ? ParseCapture(json) : new CaptureResult(false, $"HTTP_{(int)res.StatusCode}", null, 0, null);
    }

    private static CaptureResult ParseCapture(JsonNode? json)
    {
        var capture = json?["purchase_units"]?[0]?["payments"]?["captures"]?[0];
        // Only the capture's own status counts; an order-level COMPLETED without a capture is not money in hand.
        var status = capture?["status"]?.GetValue<string>() ?? "NO_CAPTURE";
        var id = capture?["id"]?.GetValue<string>();
        var currency = capture?["amount"]?["currency_code"]?.GetValue<string>();
        Money.TryParsePayPal(capture?["amount"]?["value"]?.GetValue<string>(), out var cents);
        return new CaptureResult(status == "COMPLETED", status, id, cents, currency);
    }

    public async Task<bool> VerifyWebhookAsync(IHeaderDictionary headers, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_opt.WebhookId)) { log.LogWarning("Webhook received but PayPal:WebhookId is not configured"); return false; }

        JsonNode? evt;
        try { evt = JsonNode.Parse(body); } catch (JsonException) { return false; }

        var payload = new JsonObject
        {
            ["auth_algo"] = headers["PAYPAL-AUTH-ALGO"].ToString(),
            ["cert_url"] = headers["PAYPAL-CERT-URL"].ToString(),
            ["transmission_id"] = headers["PAYPAL-TRANSMISSION-ID"].ToString(),
            ["transmission_sig"] = headers["PAYPAL-TRANSMISSION-SIG"].ToString(),
            ["transmission_time"] = headers["PAYPAL-TRANSMISSION-TIME"].ToString(),
            ["webhook_id"] = _opt.WebhookId,
            ["webhook_event"] = evt,
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/notifications/verify-webhook-signature") { Content = Json(payload) };
        using var res = await SendAsync(req, ct);
        var json = await ReadAsync(res, ct);
        return res.IsSuccessStatusCode && json?["verification_status"]?.GetValue<string>() == "SUCCESS";
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(ct));
        return await http.SendAsync(req, ct);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && clock.GetUtcNow() < _tokenExpires) return _token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && clock.GetUtcNow() < _tokenExpires) return _token;
            using var req = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token")
            {
                Content = new FormUrlEncodedContent([new("grant_type", "client_credentials")]),
            };
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_opt.ClientId}:{_opt.ClientSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            using var res = await http.SendAsync(req, ct);
            var json = await ReadAsync(res, ct);
            if (!res.IsSuccessStatusCode) throw new PayPalException($"OAuth failed ({(int)res.StatusCode})", null); // don't log token bodies
            _token = json!["access_token"]!.GetValue<string>();
            var ttl = json["expires_in"]?.GetValue<int>() ?? 300;
            _tokenExpires = clock.GetUtcNow().AddSeconds(Math.Max(30, ttl - 120));
            return _token;
        }
        finally { _tokenLock.Release(); }
    }

    private static JsonObject Amount(int cents, string currency) => new() { ["currency_code"] = currency, ["value"] = Money.ToPayPal(cents) };
    private static StringContent Json(JsonNode node) => new(node.ToJsonString(), Encoding.UTF8, "application/json");
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    private static async Task<JsonNode?> ReadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        var text = await res.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return JsonNode.Parse(text); } catch (JsonException) { return null; }
    }
}

public sealed class PayPalException(string message, JsonNode? body) : Exception(message)
{
    public string? Body { get; } = body?.ToJsonString();
}
