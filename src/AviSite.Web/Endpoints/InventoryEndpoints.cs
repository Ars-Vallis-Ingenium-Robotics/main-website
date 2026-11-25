using System.Globalization;
using System.Text;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

public static class InventoryModule
{
    public static IServiceCollection AddInventoryModule(this IServiceCollection services) { services.AddSingleton<InventoryStore>(); return services; }
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/admin/inventory").RequireAuthorization(AdminEndpoints.PurchasingPolicy);
        group.MapPost("/items",async([FromForm]string? teamId,[FromForm]string? name,[FromForm]string? sku,[FromForm]string? category,[FromForm]string? location,[FromForm]string? unit,[FromForm]string? minimum,[FromForm]string? openingQuantity,[FromForm]string? openingReason,[FromForm]string? supplier,[FromForm]string? notes,HttpContext http,IAccountStore accounts,InventoryStore inventory)=>
        {
            var a=await http.AccessAsync(accounts); var actor=a.Account?.Email; var team=a.Tree.Find(teamId);
            if(actor is null||team is null||!a.CanSee(team.Id)||!a.CanManageGroup(team.Id))return Results.NotFound();
            var org=Organization(team.Id,a.Tree); name=Clean(name,120);sku=Clean(sku,60);category=Clean(category,80);location=Clean(location,120);unit=Clean(unit,30);
            if(name is null||sku is null||category is null||location is null||unit is null||!TryQty(minimum,out var min)||!TryQty(openingQuantity,out var opening)||Clean(openingReason,240)is not{} why||(opening>0&&string.IsNullOrWhiteSpace(why)))return Back("invalid");
            try{inventory.Create(org,team.Id,name,sku,category,location,unit,min,Clean(supplier,120),Clean(notes,1000),actor,opening,opening==0?"Opening balance: zero":why);}catch(Microsoft.Data.Sqlite.SqliteException ex)when(ex.SqliteErrorCode==19){return Back("duplicate");} return Back("created");
        });
        group.MapPost("/movement",async([FromForm]string? itemId,[FromForm]string? organizationId,[FromForm]string? kind,[FromForm]string? quantity,[FromForm]string? reason,HttpContext http,IAccountStore accounts,InventoryStore inventory)=>
        {
            var a=await http.AccessAsync(accounts);var actor=a.Account?.Email;var org=a.Tree.Find(organizationId);
            if(actor is null||org is null||org.Parent is not null||!a.Tree.All.Any(g=>IsWithin(g.Id,org.Id,a.Tree)&&a.CanSee(g.Id)))return Results.NotFound();
            if(!TryQty(quantity,out var qty)||qty<=0||kind is not("Receive"or"Issue"or"Adjust")||Clean(reason,240)is not{} why)return Back("invalid");
            return inventory.Move(itemId??"",org.Id,kind,qty,why,actor,a)?Back("saved"):Back("stock");
        });
        group.MapGet("/items.csv",async(HttpContext http,IAccountStore accounts,InventoryStore inventory)=>
        {
            var a=await http.AccessAsync(accounts);var orgId=http.Request.Query["organizationId"].ToString();var org=a.Tree.Find(orgId);
            if(org is null||org.Parent is not null||!a.Tree.All.Any(g=>IsWithin(g.Id,org.Id,a.Tree)&&a.CanSee(g.Id)))return Results.NotFound();var csv=new StringBuilder("name,sku,category,location,unit,on_hand,minimum,supplier,notes\n");
            foreach(var x in inventory.List(org.Id))csv.AppendJoin(',',Csv(x.Name),Csv(x.Sku),Csv(x.Category),Csv(x.Location),Csv(x.Unit),x.Balance.ToString(CultureInfo.InvariantCulture),x.Minimum.ToString(CultureInfo.InvariantCulture),Csv(x.Supplier),Csv(x.Notes)).Append('\n');
            return Results.File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv",$"inventory-{org.Id}.csv");
        });
    }
    private static string Organization(string id,GroupTree tree){var g=tree.Find(id)!;while(g.Parent is not null)g=tree.Find(g.Parent)!;return g.Id;}
    private static bool IsWithin(string child,string root,GroupTree tree){for(var g=tree.Find(child);g is not null;g=tree.Find(g.Parent))if(g.Id==root)return true;return false;}
    private static IResult Back(string status)=>Results.LocalRedirect("/admin/inventory?status="+Uri.EscapeDataString(status));
    private static string? Clean(string? v,int n){if(string.IsNullOrWhiteSpace(v))return null;v=v.Trim();return v.Length<=n?v:null;}
    private static bool TryQty(string? raw,out decimal value)=>decimal.TryParse(raw,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out value)&&value>=0&&value<=1_000_000_000m;
    private static string Csv(string? v){v??="";if(v.Length>0&&"=+-@\t\r".Contains(v[0]))v="'"+v;return v.IndexOfAny([',','"','\n','\r'])>=0?"\""+v.Replace("\"","\"\"")+"\"":v;}
}
