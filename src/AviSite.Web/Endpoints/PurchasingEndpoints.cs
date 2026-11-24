using System.Globalization;
using System.Text.RegularExpressions;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>Minimal-form endpoints for the tenant-scoped purchasing lifecycle.</summary>
public static class PurchasingEndpoints
{
    private const int MaxLines = 20;
    private const int MaxQuantity = 100_000;
    private const long MaxUnitPriceCents = 1_000_000_00; // $1,000,000 per unit cap
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void MapPurchasingEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/purchasing").RequireAuthorization(AdminEndpoints.PurchasingPolicy);

        group.MapPost("/requests", async ([FromForm] string? teamId, [FromForm] string? title, [FromForm] string? supplier,
            [FromForm] string? supplierUrl, [FromForm] string? purpose, [FromForm] string? rationale, [FromForm] string? subsystem,
            [FromForm] string? requestedDelivery, [FromForm] string? estimatedTax, [FromForm] string? estimatedShipping,
            HttpContext http, IAccountStore accounts, PurchasingStore store, TimeProvider clock) =>
        {
            // Repeated line fields are read from the already-antiforgery-validated form collection.
            var form = await http.Request.ReadFormAsync(http.RequestAborted);
            var itemName = form["itemName"].ToArray(); var itemUrl = form["itemUrl"].ToArray();
            var itemDescription = form["itemDescription"].ToArray(); var quantity = form["quantity"].ToArray();
            var unitPrice = form["unitPrice"].ToArray();
            var access = await http.AccessAsync(accounts);
            var actor = access.Account?.Email;
            if (actor is null) return Results.NotFound(); // no attributable requester
            if (access.Tree.Find(teamId) is null || !access.CanSee(teamId!)) return Back(error: "team");

            title = Clean(title, 120);
            supplier = Clean(supplier, 120);
            purpose = Clean(purpose, 1000);
            rationale = Clean(rationale, 1000);
            subsystem = Clean(subsystem, 100);
            if (title is null || supplier is null || purpose is null || rationale is null) return Back(error: "required");
            if (!TryHttpUrl(supplierUrl, out supplierUrl)) return Back(error: "url");
            if (!DateOnly.TryParseExact(requestedDelivery, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var delivery))
            {
                if (!string.IsNullOrWhiteSpace(requestedDelivery)) return Back(error: "date");
            }
            else if (delivery < TodayPacific(clock)) return Back(error: "date");

            if (!TryCents(estimatedTax, out var tax) || !TryCents(estimatedShipping, out var shipping)) return Back(error: "cost");
            var arrays = new[] { itemName?.Length ?? 0, itemUrl?.Length ?? 0, itemDescription?.Length ?? 0, quantity?.Length ?? 0, unitPrice?.Length ?? 0 };
            if (arrays.Distinct().Count() != 1) return Back(error: "lines");
            var count = arrays[0];
            if (count > MaxLines) return Back(error: "lines");
            var lines = new List<PurchaseLineDraft>();
            for (var i = 0; i < count; i++)
            {
                var name = Clean(itemName![i], 160);
                var urlText = Clean(itemUrl![i], 2048);
                var description = Clean(itemDescription![i], 500);
                var rawQty = quantity![i]?.Trim();
                var rawPrice = unitPrice![i]?.Trim();
                var touched = name is not null || urlText is not null || description is not null || !string.IsNullOrWhiteSpace(rawQty) || !string.IsNullOrWhiteSpace(rawPrice);
                if (!touched) continue;
                if (name is null || string.IsNullOrWhiteSpace(rawQty) || string.IsNullOrWhiteSpace(rawPrice)
                    || !int.TryParse(rawQty, NumberStyles.None, Invariant, out var qty) || qty is < 1 or > MaxQuantity
                    || !TryCents(rawPrice, out var unit) || unit > MaxUnitPriceCents) return Back(error: "lines");
                if (!TryHttpUrl(urlText, out urlText)) return Back(error: "url");
                lines.Add(new PurchaseLineDraft(name, urlText, description, qty, unit));
            }
            if (lines.Count == 0) return Back(error: "lines");
            if (lines.Sum(l => checked((long)l.Quantity * l.UnitPriceCents)) + tax + shipping > 100_000_000_000_000L) return Back(error: "cost");

            var orgId = HoursRules.OrgOf(teamId!, access.Tree); // derive tenant from the validated group; never trust a posted org id
            var request = store.Create(new PurchaseDraft(orgId, teamId!, actor, title, supplier, supplierUrl, purpose, rationale, subsystem,
                string.IsNullOrWhiteSpace(requestedDelivery) ? null : delivery, tax, shipping, lines));
            return Back(done: "submitted", id: request.Code);
        });


        group.MapPost("/requests/{id}/approve", async ([FromRoute] string id, [FromForm] string? note, HttpContext http, IAccountStore accounts, PurchasingStore store) =>
            await Decide(id, true, note, http, accounts, store));
        group.MapPost("/requests/{id}/reject", async ([FromRoute] string id, [FromForm] string? note, HttpContext http, IAccountStore accounts, PurchasingStore store) =>
            await Decide(id, false, note, http, accounts, store));

        group.MapPost("/requests/{id}/order", async ([FromRoute] string id, [FromForm] string? orderedDate, [FromForm] string? vendorReference,
            HttpContext http, IAccountStore accounts, PurchasingStore store, TimeProvider clock) =>
        {
            var access = await http.AccessAsync(accounts);
            var actor = access.Account?.Email;
            var request = store.Get(id);
            if (actor is null || request is null || !CanManage(access, request)) return Results.NotFound();
            if (!DateOnly.TryParseExact(orderedDate, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date)) return Back(error: "date");
            var requestDay = DateOnly.FromDateTime(request.CreatedUtc.ToPacific().DateTime);
            if (date < requestDay || date > TodayPacific(clock)) return Back(error: "date");
            vendorReference = Clean(vendorReference, 100);
            var ok = store.MarkOrdered(id, actor, PacificStart(date), vendorReference);
            return ok ? Back(done: "ordered", id: request.Code) : Back(error: "state");
        });

        group.MapPost("/requests/{id}/receive", async ([FromRoute] string id, [FromForm] string? receivedDate, [FromForm] long[]? lineId,
            [FromForm] int[]? receiveQuantity, HttpContext http, IAccountStore accounts, PurchasingStore store, TimeProvider clock) =>
        {
            var access = await http.AccessAsync(accounts);
            var actor = access.Account?.Email;
            var request = store.Get(id);
            if (actor is null || request is null || !CanManage(access, request)) return Results.NotFound();
            if (!DateOnly.TryParseExact(receivedDate, "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var date)
                || lineId is null || receiveQuantity is null || lineId.Length != receiveQuantity.Length || lineId.Length > MaxLines)
                return Back(error: "receipt");
            var orderedDay = request.OrderedUtc is { } ordered ? DateOnly.FromDateTime(ordered.ToPacific().DateTime) : DateOnly.MaxValue;
            if (date < orderedDay || date > TodayPacific(clock)) return Back(error: "date");
            var quantities = new Dictionary<long, int>();
            for (var i = 0; i < lineId.Length; i++)
            {
                if (receiveQuantity[i] == 0) continue;
                if (receiveQuantity[i] < 0 || !quantities.TryAdd(lineId[i], receiveQuantity[i])) return Back(error: "receipt");
            }
            var ok = store.Receive(id, actor, PacificStart(date), quantities);
            return ok ? Back(done: "received", id: request.Code) : Back(error: "receipt");
        });
    }

    private static async Task<IResult> Decide(string id, bool approve, string? note, HttpContext http, IAccountStore accounts, PurchasingStore store)
    {
        var access = await http.AccessAsync(accounts);
        var actor = access.Account?.Email;
        var request = store.Get(id);
        if (actor is null || request is null || !CanManage(access, request)) return Results.NotFound();
        if (string.Equals(request.RequesterEmail, actor, StringComparison.OrdinalIgnoreCase)) return Back(error: "self");
        note = Clean(note, 240);
        if (!approve && note is null) return Back(error: "reason");
        var ok = store.Decide(id, actor, approve, note);
        return ok ? Back(done: approve ? "approved" : "rejected", id: request.Code) : Back(error: "state");
    }

    private static bool CanManage(Access access, PurchaseRequest request) => access.IsOwner || access.CanManageGroup(request.TeamId);
    private static DateOnly TodayPacific(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().ToPacific().DateTime);
    private static DateTimeOffset PacificStart(DateOnly day)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Pacific.Zone));
    }

    private static string? Clean(string? raw, int max)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var value = raw.Trim();
        return value.Length <= max ? value : null;
    }

    private static bool TryHttpUrl(string? raw, out string? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        var trimmed = raw.Trim();
        if (trimmed.Length > 2048 || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host)) return false;
        value = uri.AbsoluteUri;
        return true;
    }

    private static bool TryCents(string? raw, out long cents)
    {
        cents = 0;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!decimal.TryParse(raw.Trim(), NumberStyles.AllowDecimalPoint, Invariant, out var value) || value < 0 || value > 1_000_000m || value * 100 != decimal.Truncate(value * 100)) return false;
        cents = decimal.ToInt64(value * 100);
        return true;
    }

    private static IResult Back(string? done = null, string? error = null, string? id = null)
    {
        var query = new List<string>();
        if (done is not null) query.Add("done=" + Uri.EscapeDataString(done));
        if (error is not null) query.Add("error=" + Uri.EscapeDataString(error));
        if (id is not null) query.Add("id=" + Uri.EscapeDataString(id));
        return Results.LocalRedirect("/admin/purchasing" + (query.Count == 0 ? "" : "?" + string.Join('&', query)));
    }
}
