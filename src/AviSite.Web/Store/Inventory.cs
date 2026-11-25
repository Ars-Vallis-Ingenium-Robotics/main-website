using Microsoft.Data.Sqlite;

namespace AviSite.Web.Store;

public sealed record InventoryItem(string Id, string OrganizationId, string TeamId, string Name, string Sku, string Category, string Location, string Unit, decimal Balance, decimal Minimum, string? Supplier, string? Notes);
public sealed record InventoryTransaction(long Id, string ItemId, string Kind, decimal Quantity, decimal BalanceAfter, string Actor, DateTimeOffset AtUtc, string Reason);

/// <summary>Organization inventory with append-only, attributable stock movements.</summary>
public sealed class InventoryStore
{
    private readonly SiteDb db;
    private readonly TimeProvider clock;
    public InventoryStore(SiteDb db, TimeProvider clock) { this.db = db; this.clock = clock; Initialize(); }
    public void Initialize()
    {
        using var c = db.Open();
        SiteDb.Exec(c, """
            CREATE TABLE IF NOT EXISTS inventory_items (id TEXT PRIMARY KEY, organization_id TEXT NOT NULL, team_id TEXT NOT NULL, name TEXT NOT NULL, sku TEXT NOT NULL, category TEXT NOT NULL, location TEXT NOT NULL, unit TEXT NOT NULL, minimum REAL NOT NULL CHECK(minimum >= 0), supplier TEXT, notes TEXT, created_by TEXT NOT NULL, created_utc TEXT NOT NULL, UNIQUE(organization_id, sku));
            CREATE INDEX IF NOT EXISTS inventory_items_org_name ON inventory_items(organization_id, name COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS inventory_transactions (id INTEGER PRIMARY KEY, item_id TEXT NOT NULL REFERENCES inventory_items(id), kind TEXT NOT NULL CHECK(kind IN ('Receive','Issue','Adjust')), quantity REAL NOT NULL, balance_after REAL NOT NULL CHECK(balance_after >= 0), actor TEXT NOT NULL, at_utc TEXT NOT NULL, reason TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS inventory_transactions_item ON inventory_transactions(item_id, id DESC);
            """);
    }

    public IReadOnlyList<InventoryItem> List(string organizationId, string? search = null, bool lowStock = false)
    {
        using var c = db.Open();
        using var cmd = SiteDb.Cmd(c, """
            SELECT i.id,i.organization_id,i.team_id,i.name,i.sku,i.category,i.location,i.unit,
              COALESCE((SELECT t.balance_after FROM inventory_transactions t WHERE t.item_id=i.id ORDER BY t.id DESC LIMIT 1),0),
              i.minimum,i.supplier,i.notes
            FROM inventory_items i WHERE i.organization_id=$org AND ($search IS NULL OR i.name LIKE $like OR i.sku LIKE $like OR i.category LIKE $like OR i.location LIKE $like)
            AND ($low=0 OR COALESCE((SELECT t.balance_after FROM inventory_transactions t WHERE t.item_id=i.id ORDER BY t.id DESC LIMIT 1),0)<=i.minimum)
            ORDER BY i.name COLLATE NOCASE
            """, null, ("$org", organizationId), ("$search", string.IsNullOrWhiteSpace(search) ? null : search.Trim()), ("$like", "%" + (search?.Trim() ?? "") + "%"), ("$low", lowStock ? 1 : 0));
        using var r = cmd.ExecuteReader(); var rows = new List<InventoryItem>();
        while (r.Read()) rows.Add(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),(decimal)r.GetDouble(8),(decimal)r.GetDouble(9),SiteDb.StrOrNull(r,10),SiteDb.StrOrNull(r,11)));
        return rows;
    }

    public InventoryItem? Create(string organizationId, string teamId, string name, string sku, string category, string location, string unit, decimal minimum, string? supplier, string? notes, string actor, decimal openingQuantity, string openingReason)
    {
        if (openingQuantity < 0 || openingQuantity > 1_000_000_000m || (openingQuantity > 0 && string.IsNullOrWhiteSpace(openingReason))) return null;
        var id = Guid.NewGuid().ToString("N"); var now = SiteDb.Iso(clock.GetUtcNow());
        using var c = db.Open(); using var tx = c.BeginTransaction();
        SiteDb.Exec(c, "INSERT INTO inventory_items VALUES($id,$org,$team,$name,$sku,$category,$location,$unit,$min,$supplier,$notes,$actor,$now)", tx,
            ("$id",id),("$org",organizationId),("$team",teamId),("$name",name),("$sku",sku),("$category",category),("$location",location),("$unit",unit),("$min",(double)minimum),("$supplier",supplier),("$notes",notes),("$actor",actor),("$now",now));
        if (openingQuantity > 0)
            SiteDb.Exec(c, "INSERT INTO inventory_transactions(item_id,kind,quantity,balance_after,actor,at_utc,reason) VALUES($id,'Receive',$qty,$qty,$actor,$at,$reason)", tx,
                ("$id",id),("$qty",(double)openingQuantity),("$actor",actor),("$at",now),("$reason",openingReason.Trim()));
        tx.Commit(); return List(organizationId).First(x => x.Id == id);
    }

    public bool Move(string itemId, string organizationId, string kind, decimal quantity, string reason, string actor, Access access)
    {
        if (kind is not ("Receive" or "Issue" or "Adjust") || quantity <= 0 || quantity > 1_000_000_000m || string.IsNullOrWhiteSpace(reason)) return false;
        using var c = db.Open(); using var tx = c.BeginTransaction(deferred: false);
        using var cmd = SiteDb.Cmd(c, "SELECT i.team_id,COALESCE((SELECT balance_after FROM inventory_transactions WHERE item_id=i.id ORDER BY id DESC LIMIT 1),0) FROM inventory_items i WHERE i.id=$id AND i.organization_id=$org", tx, ("$id",itemId),("$org",organizationId));
        using var reader = cmd.ExecuteReader(); if (!reader.Read()) return false;
        var teamId = reader.GetString(0); var team = access.Tree.Find(teamId);
        var root = team;
        while (root?.Parent is not null) root = access.Tree.Find(root.Parent);
        if (team is null || root?.Id != organizationId || !access.CanSee(teamId)) return false;
        var before = (decimal)reader.GetDouble(1); reader.Close();
        var after = kind switch { "Receive" => before + quantity, "Issue" => before - quantity, _ => quantity };
        if (after < 0) return false;
        SiteDb.Exec(c,"INSERT INTO inventory_transactions(item_id,kind,quantity,balance_after,actor,at_utc,reason) VALUES($id,$kind,$qty,$balance,$actor,$at,$reason)",tx,("$id",itemId),("$kind",kind),("$qty",(double)quantity),("$balance",(double)after),("$actor",actor),("$at",SiteDb.Iso(clock.GetUtcNow())),("$reason",reason.Trim()));
        tx.Commit(); return true;
    }

    public IReadOnlyList<InventoryTransaction> Ledger(string itemId, string organizationId)
    {
        using var c=db.Open(); using var cmd=SiteDb.Cmd(c,"SELECT t.id,t.item_id,t.kind,t.quantity,t.balance_after,t.actor,t.at_utc,t.reason FROM inventory_transactions t JOIN inventory_items i ON i.id=t.item_id WHERE t.item_id=$id AND i.organization_id=$org ORDER BY t.id DESC",null,("$id",itemId),("$org",organizationId));
        using var r=cmd.ExecuteReader(); var rows=new List<InventoryTransaction>(); while(r.Read()) rows.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),(decimal)r.GetDouble(3),(decimal)r.GetDouble(4),r.GetString(5),SiteDb.Time(r.GetString(6)),r.GetString(7))); return rows;
    }
}
