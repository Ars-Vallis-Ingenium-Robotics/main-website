using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

public enum PurchaseStatus { Submitted, Approved, Rejected, Ordered, PartiallyReceived, Received }

public sealed class PurchaseLine
{
    public long Id { get; set; }
    public string ItemName { get; set; } = "";
    public string? ItemUrl { get; set; }
    public string? Description { get; set; }
    public int Quantity { get; set; }
    public int QuantityReceived { get; set; }
    public long UnitPriceCents { get; set; }
    public long LineTotalCents => checked((long)Quantity * UnitPriceCents);
    public int Remaining => Quantity - QuantityReceived;
}

public sealed record PurchaseEvent(string Action, string ActorEmail, DateTimeOffset AtUtc, string? Note);

public sealed class PurchaseRequest
{
    public string Id { get; set; } = "";
    public string Code { get; set; } = "";
    public string? PurchaseOrderCode { get; set; }
    public string OrganizationId { get; set; } = "";
    public string TeamId { get; set; } = "";
    public string RequesterEmail { get; set; } = "";
    public string Title { get; set; } = "";
    public string Supplier { get; set; } = "";
    public string? SupplierUrl { get; set; }
    public string Purpose { get; set; } = "";
    public string Rationale { get; set; } = "";
    public string? Subsystem { get; set; }
    public DateOnly? RequestedDelivery { get; set; }
    public long EstimatedTaxCents { get; set; }
    public long EstimatedShippingCents { get; set; }
    public PurchaseStatus Status { get; set; }
    public string? VendorOrderReference { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? OrderedUtc { get; set; }
    public DateTimeOffset? ReceivedUtc { get; set; }
    public List<PurchaseLine> Lines { get; set; } = [];
    public List<PurchaseEvent> Events { get; set; } = [];
    public long EstimatedSubtotalCents => Lines.Sum(l => l.LineTotalCents);
    public long EstimatedTotalCents => checked(EstimatedSubtotalCents + EstimatedTaxCents + EstimatedShippingCents);
}

public sealed record PurchaseLineDraft(string ItemName, string? ItemUrl, string? Description, int Quantity, long UnitPriceCents);
public sealed record PurchaseDraft(
    string OrganizationId,
    string TeamId,
    string RequesterEmail,
    string Title,
    string Supplier,
    string? SupplierUrl,
    string Purpose,
    string Rationale,
    string? Subsystem,
    DateOnly? RequestedDelivery,
    long EstimatedTaxCents,
    long EstimatedShippingCents,
    IReadOnlyList<PurchaseLineDraft> Lines);

/// <summary>Organization-scoped requisitions and their approval, order and receiving audit trail.</summary>
public sealed class PurchasingStore(SiteDb db, TimeProvider clock)
{
    private const string HeaderColumns = "id, code, po_code, organization_id, team_id, requester_email, title, supplier, supplier_url, purpose, rationale, subsystem, requested_delivery, estimated_tax_cents, estimated_shipping_cents, status, vendor_order_reference, created_utc, updated_utc, ordered_utc, received_utc";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public bool HasRequestsForGroup(string groupId)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "SELECT EXISTS(SELECT 1 FROM purchase_requests WHERE team_id=$group)", null, ("$group", groupId));
        return Convert.ToInt64(cmd.ExecuteScalar(), Invariant) != 0;
    }

    public PurchaseRequest? Get(string id)
    {
        using var c = db.Open();
        var request = ReadHeader(c, id);
        if (request is null) return null;
        request.Lines = ReadLines(c, id);
        request.Events = ReadEvents(c, id);
        return request;
    }

    public IReadOnlyList<PurchaseRequest> ListVisible(Access access)
    {
        using var c = db.Open();
        var ids = new List<string>();
        var groups = access.Manages.Concat(access.BelongsTo).Distinct().ToList();
        var where = access.IsOwner ? "1=1" : groups.Count == 0
            ? "requester_email = $email"
            : $"(requester_email = $email OR team_id IN ({string.Join(',', groups.Select((_, i) => "$g" + i))}))";
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT id FROM purchase_requests WHERE {where} ORDER BY created_utc DESC LIMIT 500";
            if (!access.IsOwner) cmd.Parameters.AddWithValue("$email", access.Account?.Email ?? "");
            for (var i = 0; i < groups.Count && !access.IsOwner; i++) cmd.Parameters.AddWithValue("$g" + i, groups[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }
        return ids.Select(id => Get(id)).Where(x => x is not null).Cast<PurchaseRequest>().ToList();
    }

    public PurchaseRequest Create(PurchaseDraft draft)
    {
        var now = clock.GetUtcNow();
        var id = NewId();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var serial = NextNumber(c, tx, draft.OrganizationId, now.Year, "REQ");
        var org = CodePart(draft.OrganizationId);
        var request = new PurchaseRequest
        {
            Id = id,
            Code = $"REQ-{org}-{now.Year}-{serial:0000}",
            OrganizationId = draft.OrganizationId,
            TeamId = draft.TeamId,
            RequesterEmail = draft.RequesterEmail,
            Title = draft.Title,
            Supplier = draft.Supplier,
            SupplierUrl = draft.SupplierUrl,
            Purpose = draft.Purpose,
            Rationale = draft.Rationale,
            Subsystem = draft.Subsystem,
            RequestedDelivery = draft.RequestedDelivery,
            EstimatedTaxCents = draft.EstimatedTaxCents,
            EstimatedShippingCents = draft.EstimatedShippingCents,
            Status = PurchaseStatus.Submitted,
            CreatedUtc = now,
            UpdatedUtc = now,
            Lines = draft.Lines.Select(l => new PurchaseLine { ItemName = l.ItemName, ItemUrl = l.ItemUrl, Description = l.Description, Quantity = l.Quantity, UnitPriceCents = l.UnitPriceCents }).ToList(),
        };
        SiteDb.Exec(c, $"""
            INSERT INTO purchase_requests ({HeaderColumns}) VALUES
            ($id,$code,NULL,$org,$team,$requester,$title,$supplier,$supplierUrl,$purpose,$rationale,$subsystem,$delivery,$tax,$shipping,'Submitted',NULL,$now,$now,NULL,NULL)
            """, tx,
            ("$id", request.Id), ("$code", request.Code), ("$org", request.OrganizationId), ("$team", request.TeamId),
            ("$requester", request.RequesterEmail), ("$title", request.Title), ("$supplier", request.Supplier), ("$supplierUrl", request.SupplierUrl),
            ("$purpose", request.Purpose), ("$rationale", request.Rationale), ("$subsystem", request.Subsystem),
            ("$delivery", request.RequestedDelivery?.ToString("yyyy-MM-dd", Invariant)), ("$tax", request.EstimatedTaxCents),
            ("$shipping", request.EstimatedShippingCents), ("$now", SiteDb.Iso(now)));
        foreach (var (line, position) in request.Lines.Select((line, i) => (line, i)))
            SiteDb.Exec(c, "INSERT INTO purchase_request_lines (request_id, position, item_name, item_url, description, quantity, unit_price_cents) VALUES ($request,$position,$name,$url,$description,$quantity,$unit)", tx,
                ("$request", id), ("$position", position), ("$name", line.ItemName), ("$url", line.ItemUrl), ("$description", line.Description), ("$quantity", line.Quantity), ("$unit", line.UnitPriceCents));
        AddEvent(c, tx, id, "Submitted", draft.RequesterEmail, now, null);
        tx.Commit();
        return Get(id)!;
    }

    /// <summary>Only one decision may win; the requester cannot approve their own requisition.</summary>
    public bool Decide(string id, string actor, bool approve, string? note)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var now = clock.GetUtcNow();
        var action = approve ? "Approved" : "Rejected";
        using var cmd = SiteDb.Cmd(c, "UPDATE purchase_requests SET status=$status, updated_utc=$now WHERE id=$id AND status='Submitted' AND requester_email<>$actor",
            tx, ("$status", action), ("$now", SiteDb.Iso(now)), ("$id", id), ("$actor", actor));
        if (cmd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
        AddEvent(c, tx, id, action, actor, now, note);
        tx.Commit();
        return true;
    }

    /// <summary>Creates an internal PO code only after approval; vendor reference remains a separate value.</summary>
    public bool MarkOrdered(string id, string actor, DateTimeOffset orderedUtc, string? vendorReference)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var request = ReadHeader(c, id, tx);
        if (request is null) { tx.Rollback(); return false; }
        var requestDay = DateOnly.FromDateTime(request.CreatedUtc.ToPacific().DateTime);
        var orderDay = DateOnly.FromDateTime(orderedUtc.ToPacific().DateTime);
        if (request.Status != PurchaseStatus.Approved || orderDay < requestDay || orderDay > DateOnly.FromDateTime(clock.GetUtcNow().ToPacific().DateTime))
        { tx.Rollback(); return false; }
        var serial = NextNumber(c, tx, request.OrganizationId, orderedUtc.Year, "PO");
        var poCode = $"PO-{CodePart(request.OrganizationId)}-{orderedUtc.Year}-{serial:0000}";
        var now = clock.GetUtcNow();
        using var cmd = SiteDb.Cmd(c, "UPDATE purchase_requests SET status='Ordered', po_code=$po, vendor_order_reference=$reference, ordered_utc=$ordered, updated_utc=$now WHERE id=$id AND status='Approved'",
            tx, ("$po", poCode), ("$reference", vendorReference), ("$ordered", SiteDb.Iso(orderedUtc)), ("$now", SiteDb.Iso(now)), ("$id", id));
        if (cmd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
        AddEvent(c, tx, id, "Ordered", actor, now, string.IsNullOrWhiteSpace(vendorReference) ? poCode : $"{poCode} · vendor ref {vendorReference}");
        tx.Commit();
        return true;
    }

    /// <summary>Records one receipt operation; partial deliveries are supported and quantities can never exceed the request.</summary>
    public bool Receive(string id, string actor, DateTimeOffset receivedUtc, IReadOnlyDictionary<long, int> quantities)
    {
        if (quantities.Count == 0 || quantities.Any(q => q.Key <= 0 || q.Value <= 0)) return false;
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var request = ReadHeader(c, id, tx);
        if (request is null || request.Status is not (PurchaseStatus.Ordered or PurchaseStatus.PartiallyReceived)
            || request.OrderedUtc is null || receivedUtc < request.OrderedUtc || receivedUtc > clock.GetUtcNow())
        { tx.Rollback(); return false; }

        foreach (var (lineId, quantity) in quantities)
        {
            int requested, received;
            using (var cmd = SiteDb.Cmd(c, "SELECT quantity, received_quantity FROM purchase_request_lines WHERE id=$line AND request_id=$request", tx,
                ("$line", lineId), ("$request", id)))
            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) { tx.Rollback(); return false; }
                requested = r.GetInt32(0); received = r.GetInt32(1);
            }
            if (quantity > requested - received) { tx.Rollback(); return false; }
            SiteDb.Exec(c, "UPDATE purchase_request_lines SET received_quantity=received_quantity+$qty WHERE id=$line AND request_id=$request",
                tx, ("$qty", quantity), ("$line", lineId), ("$request", id));
            SiteDb.Exec(c, "INSERT INTO purchase_receipts (request_id, line_id, quantity, received_utc, received_by) VALUES ($request,$line,$qty,$at,$by)",
                tx, ("$request", id), ("$line", lineId), ("$qty", quantity), ("$at", SiteDb.Iso(receivedUtc)), ("$by", actor));
        }
        long outstanding;
        using (var cmd = SiteDb.Cmd(c, "SELECT COUNT(*) FROM purchase_request_lines WHERE request_id=$id AND received_quantity<quantity", tx, ("$id", id)))
            outstanding = (long)cmd.ExecuteScalar()!;
        var nextStatus = outstanding == 0 ? PurchaseStatus.Received : PurchaseStatus.PartiallyReceived;
        var now = clock.GetUtcNow();
        SiteDb.Exec(c, "UPDATE purchase_requests SET status=$status, received_utc=CASE WHEN $status='Received' THEN $received ELSE NULL END, updated_utc=$now WHERE id=$id",
            tx, ("$status", nextStatus.ToString()), ("$received", SiteDb.Iso(receivedUtc)), ("$now", SiteDb.Iso(now)), ("$id", id));
        var note = $"Received {quantities.Values.Sum()} unit(s)";
        AddEvent(c, tx, id, nextStatus.ToString(), actor, now, note);
        tx.Commit();
        return true;
    }

    private static PurchaseRequest? ReadHeader(SqliteConnection c, string id, SqliteTransaction? tx = null)
    {
        using var cmd = SiteDb.Cmd(c, $"SELECT {HeaderColumns} FROM purchase_requests WHERE id=$id", tx, ("$id", id));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new PurchaseRequest
        {
            Id = r.GetString(0), Code = r.GetString(1), PurchaseOrderCode = SiteDb.StrOrNull(r, 2), OrganizationId = r.GetString(3), TeamId = r.GetString(4),
            RequesterEmail = r.GetString(5), Title = r.GetString(6), Supplier = r.GetString(7), SupplierUrl = SiteDb.StrOrNull(r, 8),
            Purpose = r.GetString(9), Rationale = r.GetString(10), Subsystem = SiteDb.StrOrNull(r, 11),
            RequestedDelivery = r.IsDBNull(12) ? null : DateOnly.ParseExact(r.GetString(12), "yyyy-MM-dd", Invariant),
            EstimatedTaxCents = r.GetInt64(13), EstimatedShippingCents = r.GetInt64(14), Status = Enum.Parse<PurchaseStatus>(r.GetString(15)),
            VendorOrderReference = SiteDb.StrOrNull(r, 16), CreatedUtc = SiteDb.Time(r.GetString(17)), UpdatedUtc = SiteDb.Time(r.GetString(18)),
            OrderedUtc = SiteDb.TimeOrNull(r, 19), ReceivedUtc = SiteDb.TimeOrNull(r, 20),
        };
    }

    private static List<PurchaseLine> ReadLines(SqliteConnection c, string id)
    {
        var lines = new List<PurchaseLine>();
        using var cmd = SiteDb.Cmd(c, "SELECT id,item_name,item_url,description,quantity,received_quantity,unit_price_cents FROM purchase_request_lines WHERE request_id=$id ORDER BY position", null, ("$id", id));
        using var r = cmd.ExecuteReader();
        while (r.Read()) lines.Add(new PurchaseLine { Id = r.GetInt64(0), ItemName = r.GetString(1), ItemUrl = SiteDb.StrOrNull(r, 2), Description = SiteDb.StrOrNull(r, 3), Quantity = r.GetInt32(4), QuantityReceived = r.GetInt32(5), UnitPriceCents = r.GetInt64(6) });
        return lines;
    }

    private static List<PurchaseEvent> ReadEvents(SqliteConnection c, string id)
    {
        var events = new List<PurchaseEvent>();
        using var cmd = SiteDb.Cmd(c, "SELECT action,actor_email,at_utc,note FROM purchase_events WHERE request_id=$id ORDER BY id", null, ("$id", id));
        using var r = cmd.ExecuteReader();
        while (r.Read()) events.Add(new PurchaseEvent(r.GetString(0), r.GetString(1), SiteDb.Time(r.GetString(2)), SiteDb.StrOrNull(r, 3)));
        return events;
    }

    private static void AddEvent(SqliteConnection c, SqliteTransaction tx, string id, string action, string actor, DateTimeOffset at, string? note) =>
        SiteDb.Exec(c, "INSERT INTO purchase_events (request_id, action, actor_email, at_utc, note) VALUES ($request,$action,$actor,$at,$note)", tx,
            ("$request", id), ("$action", action), ("$actor", actor), ("$at", SiteDb.Iso(at)), ("$note", note));

    private static int NextNumber(SqliteConnection c, SqliteTransaction tx, string organizationId, int year, string kind)
    {
        using var cmd = SiteDb.Cmd(c, "INSERT INTO purchasing_sequences (organization_id,year,kind,last_number) VALUES ($org,$year,$kind,1) ON CONFLICT(organization_id,year,kind) DO UPDATE SET last_number=last_number+1 RETURNING last_number",
            tx, ("$org", organizationId), ("$year", year), ("$kind", kind));
        return Convert.ToInt32(cmd.ExecuteScalar(), Invariant);
    }

    private static string CodePart(string org) => Regex.Replace(org, "[^a-zA-Z0-9]+", "-").Trim('-').ToUpperInvariant();
    private static string NewId() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
