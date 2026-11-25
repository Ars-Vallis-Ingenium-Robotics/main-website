using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
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

public sealed record PurchaseApprovalRule(int Position, string Label, string ApproverType, string ApproverValue, long MinimumTotalCents);

public sealed record PurchasePolicyEvent(string Action, string ActorEmail, DateTimeOffset AtUtc,
    IReadOnlyList<PurchaseApprovalRule> PreviousRules, IReadOnlyList<PurchaseApprovalRule> NewRules);

public sealed class PurchaseApprovalStep
{
    public int Position { get; set; }
    public string Label { get; set; } = "";
    public string ApproverType { get; set; } = "";
    public string ApproverValue { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedUtc { get; set; }
    public string? Note { get; set; }
}

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
    public List<PurchaseApprovalStep> Approvals { get; set; } = [];
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

    public IReadOnlyList<PurchaseApprovalRule> GetPolicy(string organizationId)
    {
        using var c = db.Open();
        return ReadPolicy(c, organizationId);
    }

    public IReadOnlyList<PurchasePolicyEvent> ListPolicyEvents(string organizationId)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, "SELECT action,actor_email,at_utc,previous_policy_json,new_policy_json FROM purchase_approval_policy_events WHERE organization_id=$org ORDER BY id DESC LIMIT 10",
            null, ("$org", organizationId));
        using var r = cmd.ExecuteReader();
        var events = new List<PurchasePolicyEvent>();
        while (r.Read())
        {
            var previous = JsonSerializer.Deserialize<List<PurchaseApprovalRule>>(r.GetString(3)) ?? [];
            var current = JsonSerializer.Deserialize<List<PurchaseApprovalRule>>(r.GetString(4)) ?? [];
            events.Add(new PurchasePolicyEvent(r.GetString(0), r.GetString(1), SiteDb.Time(r.GetString(2)), previous, current));
        }
        return events;
    }

    public static bool PolicyCanComplete(string organizationId, IReadOnlyList<PurchaseApprovalRule> rules,
        IReadOnlyList<Account> accounts, GroupTree tree)
    {
        if (rules.Count == 0) return false;
        var orgGroups = tree.WithDescendants([organizationId]);
        if (orgGroups.Count == 0) return false;
        foreach (var teamId in orgGroups)
            if (!HasDistinctApproverAssignment(BuildCandidates(rules, teamId, organizationId, accounts, tree))) return false;
        return true;
    }

    public void SavePolicy(string organizationId, string actor, IReadOnlyList<PurchaseApprovalRule> rules)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var now = SiteDb.Iso(clock.GetUtcNow());
        var previousJson = JsonSerializer.Serialize(ReadPolicy(c, organizationId, tx));
        var currentJson = JsonSerializer.Serialize(rules);
        SiteDb.Exec(c, "INSERT INTO purchase_approval_policies (organization_id,updated_by,updated_utc) VALUES ($org,$actor,$now) ON CONFLICT(organization_id) DO UPDATE SET updated_by=excluded.updated_by, updated_utc=excluded.updated_utc",
            tx, ("$org", organizationId), ("$actor", actor), ("$now", now));
        SiteDb.Exec(c, "DELETE FROM purchase_approval_policy_steps WHERE organization_id=$org", tx, ("$org", organizationId));
        foreach (var rule in rules)
            SiteDb.Exec(c, "INSERT INTO purchase_approval_policy_steps (organization_id,position,label,approver_type,approver_value,minimum_total_cents) VALUES ($org,$position,$label,$type,$value,$minimum)",
                tx, ("$org", organizationId), ("$position", rule.Position), ("$label", rule.Label), ("$type", rule.ApproverType), ("$value", rule.ApproverValue), ("$minimum", rule.MinimumTotalCents));
        AddPolicyEvent(c, tx, organizationId, "Saved", actor, now, previousJson, currentJson);
        tx.Commit();
    }

    public void ResetPolicy(string organizationId, string actor)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var now = SiteDb.Iso(clock.GetUtcNow());
        var previousJson = JsonSerializer.Serialize(ReadPolicy(c, organizationId, tx));
        SiteDb.Exec(c, "DELETE FROM purchase_approval_policies WHERE organization_id=$org", tx, ("$org", organizationId));
        AddPolicyEvent(c, tx, organizationId, "Reset", actor, now, previousJson, "[]");
        tx.Commit();
    }

    public bool CanDecide(Access access, PurchaseRequest request, IReadOnlyList<Account> accounts, bool approve)
    {
        var actorEmail = AccountRules.Normalize(access.Account?.Email);
        if (actorEmail.Length == 0 || string.Equals(request.RequesterEmail, actorEmail, StringComparison.OrdinalIgnoreCase)) return false;
        var actor = accounts.FirstOrDefault(a => string.Equals(AccountRules.Normalize(a.Email), actorEmail, StringComparison.OrdinalIgnoreCase));
        if (actor is null || request.Approvals.Any(x => x.Status == "Approved" && string.Equals(x.ApprovedBy, actorEmail, StringComparison.OrdinalIgnoreCase))) return false;
        var actorAccess = Access.For(actor, access.Tree);
        var pending = request.Approvals.Where(x => x.Status == "Pending").ToList();
        if (pending.Count == 0 || !CanSatisfyRule(actorAccess, pending[0].ApproverType, pending[0].ApproverValue, request.TeamId, request.OrganizationId)) return false;
        if (!approve) return true;

        var alreadyUsed = request.Approvals.Where(x => x.Status == "Approved").Select(x => x.ApprovedBy ?? "")
            .Append(actorEmail).Append(request.RequesterEmail).Where(x => x.Length > 0)
            .Select(AccountRules.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return HasDistinctApproverAssignment(BuildCandidates(pending.Skip(1), request.TeamId, request.OrganizationId, accounts, access.Tree), alreadyUsed);
    }

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
        request.Approvals = ReadApprovals(c, id);
        return request;
    }

    public IReadOnlyList<PurchaseRequest> ListVisible(Access access)
    {
        using var c = db.Open();
        var ids = new List<string>();
        var groups = access.Manages.Concat(access.BelongsTo).Distinct().ToList();
        var organizations = groups.Select(group => OrganizationOf(group, access.Tree)).Distinct().ToList();
        var orgFilter = organizations.Count == 0 ? "0=1" : $"purchase_requests.organization_id IN ({string.Join(',', organizations.Select((_, i) => "$o" + i))})";
        var assigned = $"({orgFilter} AND EXISTS (SELECT 1 FROM purchase_request_approvals pa WHERE pa.request_id=purchase_requests.id AND pa.approver_type='Email' AND pa.approver_value=$email))";
        var where = access.IsOwner ? "1=1" : groups.Count == 0
            ? $"(requester_email = $email OR {assigned})"
            : $"(requester_email = $email OR team_id IN ({string.Join(',', groups.Select((_, i) => "$g" + i))}) OR {assigned})";
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $"SELECT id FROM purchase_requests WHERE {where} ORDER BY created_utc DESC LIMIT 500";
            if (!access.IsOwner) cmd.Parameters.AddWithValue("$email", AccountRules.Normalize(access.Account?.Email));
            for (var i = 0; i < groups.Count && !access.IsOwner; i++) cmd.Parameters.AddWithValue("$g" + i, groups[i]);
            for (var i = 0; i < organizations.Count && !access.IsOwner; i++) cmd.Parameters.AddWithValue("$o" + i, organizations[i]);
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }
        return ids.Select(id => Get(id)).Where(x => x is not null).Cast<PurchaseRequest>().ToList();
    }

    private static string OrganizationOf(string groupId, GroupTree tree)
    {
        var group = tree.Find(groupId);
        while (group?.Parent is { } parentId && tree.Find(parentId) is { } parent) group = parent;
        return group?.Id ?? groupId;
    }

    public PurchaseRequest? Create(PurchaseDraft draft, IReadOnlyList<Account> accounts, GroupTree tree)
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
        var rules = ReadPolicy(c, request.OrganizationId, tx)
            .Where(rule => rule.MinimumTotalCents <= request.EstimatedTotalCents).ToList();
        if (rules.Count == 0) rules.Add(new PurchaseApprovalRule(1, "Team manager", "TeamManager", "", 0));
        if (!HasDistinctApproverAssignment(BuildCandidates(rules, request.TeamId, request.OrganizationId, accounts, tree), [request.RequesterEmail]))
        { tx.Rollback(); return null; }
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
        foreach (var rule in rules)
            SiteDb.Exec(c, "INSERT INTO purchase_request_approvals (request_id,position,label,approver_type,approver_value,status) VALUES ($request,$position,$label,$type,$value,'Pending')",
                tx, ("$request", id), ("$position", rule.Position), ("$label", rule.Label), ("$type", rule.ApproverType), ("$value", rule.ApproverValue));
        AddEvent(c, tx, id, "Submitted", draft.RequesterEmail, now, null);
        tx.Commit();
        return Get(id)!;
    }

    /// <summary>Approves only the current stage, or rejects the request at any stage; no requester may approve their own request.</summary>
    public bool Decide(string id, string actor, Access access, IReadOnlyList<Account> accounts, bool approve, string? note)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var request = ReadHeader(c, id, tx);
        var actorEmail = AccountRules.Normalize(actor);
        var currentActor = accounts.FirstOrDefault(a => string.Equals(AccountRules.Normalize(a.Email), actorEmail, StringComparison.OrdinalIgnoreCase));
        if (request is null || currentActor is null || request.Status != PurchaseStatus.Submitted || string.Equals(request.RequesterEmail, actorEmail, StringComparison.OrdinalIgnoreCase))
        { tx.Rollback(); return false; }
        request.Approvals = ReadApprovals(c, id, tx);
        if (!CanDecide(Access.For(currentActor, access.Tree), request, accounts, approve)) { tx.Rollback(); return false; }
        var step = request.Approvals.First(x => x.Status == "Pending");
        var now = clock.GetUtcNow();
        var nextStepStatus = approve ? "Approved" : "Rejected";
        using (var cmd = SiteDb.Cmd(c, "UPDATE purchase_request_approvals SET status=$status,approved_by=$actor,approved_utc=$now,note=$note WHERE request_id=$id AND position=$position AND status='Pending'",
            tx, ("$status", nextStepStatus), ("$actor", actor), ("$now", SiteDb.Iso(now)), ("$note", note), ("$id", id), ("$position", step.Position)))
            if (cmd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
        var eventNote = string.IsNullOrWhiteSpace(note) ? step.Label : $"{step.Label} · {note}";
        AddEvent(c, tx, id, approve ? "ApprovalStepApproved" : "ApprovalStepRejected", actor, now, eventNote);
        if (!approve)
        {
            SiteDb.Exec(c, "UPDATE purchase_requests SET status='Rejected',updated_utc=$now WHERE id=$id AND status='Submitted'", tx,
                ("$now", SiteDb.Iso(now)), ("$id", id));
            AddEvent(c, tx, id, "Rejected", actor, now, note);
        }
        else
        {
            long pending;
            using (var cmd = SiteDb.Cmd(c, "SELECT COUNT(*) FROM purchase_request_approvals WHERE request_id=$id AND status='Pending'", tx, ("$id", id)))
                pending = (long)cmd.ExecuteScalar()!;
            if (pending == 0)
            {
                SiteDb.Exec(c, "UPDATE purchase_requests SET status='Approved',updated_utc=$now WHERE id=$id AND status='Submitted'", tx,
                    ("$now", SiteDb.Iso(now)), ("$id", id));
                AddEvent(c, tx, id, "Approved", actor, now, null);
            }
            else SiteDb.Exec(c, "UPDATE purchase_requests SET updated_utc=$now WHERE id=$id AND status='Submitted'", tx,
                ("$now", SiteDb.Iso(now)), ("$id", id));
        }
        tx.Commit();
        return true;
    }

    /// <summary>Allows a site owner to terminally reject a submitted request if its approval chain is orphaned.</summary>
    public bool OwnerOverrideReject(string id, string actor, Access access, string? reason)
    {
        if (!access.IsOwner || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 240) return false;
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var request = ReadHeader(c, id, tx);
        if (request is null || request.Status != PurchaseStatus.Submitted) { tx.Rollback(); return false; }
        var now = clock.GetUtcNow();
        using (var cmd = SiteDb.Cmd(c, "UPDATE purchase_request_approvals SET status='Skipped',note=$note WHERE request_id=$id AND status='Pending'",
            tx, ("$note", $"Closed by site owner: {reason.Trim()}"), ("$id", id)))
            if (cmd.ExecuteNonQuery() == 0) { tx.Rollback(); return false; }
        using (var cmd = SiteDb.Cmd(c, "UPDATE purchase_requests SET status='Rejected',updated_utc=$now WHERE id=$id AND status='Submitted'",
            tx, ("$now", SiteDb.Iso(now)), ("$id", id)))
            if (cmd.ExecuteNonQuery() != 1) { tx.Rollback(); return false; }
        AddEvent(c, tx, id, "OwnerOverrideRejected", actor, now, reason.Trim());
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

    private static List<PurchaseApprovalRule> ReadPolicy(SqliteConnection c, string organizationId, SqliteTransaction? tx = null)
    {
        var rules = new List<PurchaseApprovalRule>();
        using var cmd = SiteDb.Cmd(c, "SELECT position,label,approver_type,approver_value,minimum_total_cents FROM purchase_approval_policy_steps WHERE organization_id=$org ORDER BY position", tx, ("$org", organizationId));
        using var r = cmd.ExecuteReader();
        while (r.Read()) rules.Add(new PurchaseApprovalRule(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4)));
        return rules;
    }

    private static List<HashSet<string>> BuildCandidates(IEnumerable<PurchaseApprovalRule> rules, string teamId, string organizationId,
        IReadOnlyList<Account> accounts, GroupTree tree) =>
        BuildCandidates(rules.Select(rule => (rule.ApproverType, rule.ApproverValue)), teamId, organizationId, accounts, tree);

    private static List<HashSet<string>> BuildCandidates(IEnumerable<PurchaseApprovalStep> steps, string teamId, string organizationId,
        IReadOnlyList<Account> accounts, GroupTree tree) =>
        BuildCandidates(steps.Select(step => (step.ApproverType, step.ApproverValue)), teamId, organizationId, accounts, tree);

    private static List<HashSet<string>> BuildCandidates(IEnumerable<(string ApproverType, string ApproverValue)> steps, string teamId,
        string organizationId, IReadOnlyList<Account> accounts, GroupTree tree) => steps.Select(step => accounts
            .Where(account => CanSatisfyRule(Access.For(account, tree), step.ApproverType, step.ApproverValue, teamId, organizationId))
            .Select(account => AccountRules.Normalize(account.Email))
            .Where(email => email.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)).ToList();

    private static bool CanSatisfyRule(Access access, string type, string value, string teamId, string organizationId) => type switch
    {
        "TeamManager" => access.CanManageGroup(teamId),
        "Lead" => access.Leads.Contains(teamId),
        "Officer" => access.IsOfficer && access.CanManageGroup(teamId),
        "Owner" => access.IsOwner,
        "Email" => string.Equals(AccountRules.Normalize(value), AccountRules.Normalize(access.Account?.Email), StringComparison.OrdinalIgnoreCase)
            && IsOrganizationMember(access, organizationId),
        _ => false,
    };

    private static bool IsOrganizationMember(Access access, string organizationId)
    {
        var orgGroups = access.Tree.WithDescendants([organizationId]);
        return access.BelongsTo.Any(orgGroups.Contains);
    }

    private static bool HasDistinctApproverAssignment(IReadOnlyList<HashSet<string>> candidates, IEnumerable<string>? excluded = null)
    {
        var blocked = (excluded ?? Enumerable.Empty<string>()).Select(AccountRules.Normalize).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assigned = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool Assign(int step, HashSet<string> visited)
        {
            foreach (var email in candidates[step])
            {
                if (blocked.Contains(email) || !visited.Add(email)) continue;
                if (!assigned.TryGetValue(email, out var previousStep) || Assign(previousStep, visited))
                {
                    assigned[email] = step;
                    return true;
                }
            }
            return false;
        }
        for (var step = 0; step < candidates.Count; step++)
            if (!Assign(step, new HashSet<string>(StringComparer.OrdinalIgnoreCase))) return false;
        return true;
    }

    private static void AddPolicyEvent(SqliteConnection c, SqliteTransaction tx, string organizationId, string action, string actor,
        string atUtc, string previousJson, string currentJson) =>
        SiteDb.Exec(c, "INSERT INTO purchase_approval_policy_events (organization_id,action,actor_email,at_utc,previous_policy_json,new_policy_json) VALUES ($org,$action,$actor,$at,$previous,$current)", tx,
            ("$org", organizationId), ("$action", action), ("$actor", actor), ("$at", atUtc), ("$previous", previousJson), ("$current", currentJson));

    private static List<PurchaseApprovalStep> ReadApprovals(SqliteConnection c, string id, SqliteTransaction? tx = null)
    {
        var steps = new List<PurchaseApprovalStep>();
        using var cmd = SiteDb.Cmd(c, "SELECT position,label,approver_type,approver_value,status,approved_by,approved_utc,note FROM purchase_request_approvals WHERE request_id=$id ORDER BY position", tx, ("$id", id));
        using var r = cmd.ExecuteReader();
        while (r.Read()) steps.Add(new PurchaseApprovalStep
        {
            Position = r.GetInt32(0), Label = r.GetString(1), ApproverType = r.GetString(2), ApproverValue = r.GetString(3),
            Status = r.GetString(4), ApprovedBy = SiteDb.StrOrNull(r, 5), ApprovedUtc = SiteDb.TimeOrNull(r, 6), Note = SiteDb.StrOrNull(r, 7),
        });
        return steps;
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
