using System.Globalization;
using System.Text;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>
/// Optional module registration (do not edit Program.cs for isolated integration):
/// services.AddDecisionMatrixModule();
/// app.MapDecisionMatrixEndpoints();
/// Place the service call beside AddSingleton<PurchasingStore>() and endpoint call beside MapPurchasingEndpoints().
/// </summary>
public static class DecisionMatrixEndpoints
{
    public static IServiceCollection AddDecisionMatrixModule(this IServiceCollection services)
    { services.AddSingleton(sp => new DecisionMatrixStore(sp.GetRequiredService<SiteDb>(),sp.GetRequiredService<TimeProvider>()).EnsureSchema()); return services; }
    public static void MapDecisionMatrixEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/admin/decision-matrices").RequireAuthorization(AdminEndpoints.PurchasingPolicy);
        // A bound form field enables the host's antiforgery middleware before reading the repeated fields.
        group.MapPost("/save", ([FromForm] string? teamId, HttpContext http, IAccountStore accounts, DecisionMatrixStore store) =>
            SaveAsync(null, teamId, http, accounts, store));
        group.MapPost("/{id}/update", (string id, [FromForm] string? teamId, HttpContext http, IAccountStore accounts, DecisionMatrixStore store) =>
            SaveAsync(id, teamId, http, accounts, store));
        group.MapPost("/{id}/archive", (string id,[FromForm]string? teamId,HttpContext http,IAccountStore accounts,DecisionMatrixStore store)=>SetArchived(id,teamId,true,http,accounts,store));
        group.MapPost("/{id}/restore", (string id,[FromForm]string? teamId,HttpContext http,IAccountStore accounts,DecisionMatrixStore store)=>SetArchived(id,teamId,false,http,accounts,store));
        group.MapGet("/{id}/export.csv", async (string id,[FromQuery]string? teamId,HttpContext http,IAccountStore accounts,DecisionMatrixStore store)=>
        { var access=await http.AccessAsync(accounts); if(teamId is null||!access.CanSee(teamId))return Results.NotFound(); var d=store.Get(id,HoursRules.OrgOf(teamId,access.Tree),teamId); if(d is null)return Results.NotFound(); http.Response.Headers.CacheControl="no-store"; var b=new StringBuilder("alternative,weighted_score,rank,criterion,weight,score,rationale\n"); for(var a=0;a<d.Results.Count;a++) for(var i=0;i<d.State.Criteria.Count;i++){var result=d.Results[a]; var s=d.State.Scores.First(x=>x.Alternative==a&&x.Criterion==i); b.AppendJoin(',',Csv(result.Alternative),Csv(result.WeightedScore?.ToString("0.0000",CultureInfo.InvariantCulture)),Csv(result.Rank?.ToString()),Csv(d.State.Criteria[i].Name),Csv(d.State.Criteria[i].Weight.ToString(CultureInfo.InvariantCulture)),Csv(s.Score?.ToString()),Csv(s.Rationale)).Append('\n');} return Results.File(Encoding.UTF8.GetBytes(b.ToString()),"text/csv",$"decision-matrix-{id}.csv"); });
    }
    private static async Task<IResult> SaveAsync(string? editId, string? teamId, HttpContext http, IAccountStore accounts, DecisionMatrixStore store)
    {
        var access = await http.AccessAsync(accounts);
        if (access.Account?.Email is not { } actor || teamId is null || access.Tree.Find(teamId) is null
            || !access.CanSee(teamId) || !access.CanManageGroup(teamId)) return Results.NotFound();
        var org = HoursRules.OrgOf(teamId, access.Tree);
        if (editId is not null && store.Get(editId, org, teamId) is not { Archived: false }) return Results.NotFound();

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var alternatives = form["alternative"].ToArray(); var aNotes = form["alternativeNotes"].ToArray();
        var criteria = form["criterion"].ToArray(); var weights = form["weight"].ToArray(); var cNotes = form["criterionNotes"].ToArray();
        var rawScores = form["score"].ToArray(); var rationales = form["rationale"].ToArray();
        IResult Invalid() => editId is null
            ? Results.Redirect($"/admin/decision-matrices?status=invalid&alternatives={alternatives.Length}&criteria={criteria.Length}")
            : Results.Redirect($"/admin/decision-matrices/{Uri.EscapeDataString(editId)}/edit?teamId={Uri.EscapeDataString(teamId)}&status=invalid");

        if (!DecisionMatrixRules.ValidDimensions(alternatives.Length, criteria.Length)
            || alternatives.Length != aNotes.Length || criteria.Length != weights.Length || criteria.Length != cNotes.Length
            || rawScores.Length != alternatives.Length * criteria.Length || rawScores.Length != rationales.Length) return Invalid();

        try
        {
            var draft = new MatrixDraft(editId ?? Guid.NewGuid().ToString("N"), org, teamId, form["title"].ToString(),
                alternatives.Select((name, i) => new MatrixAlternative(name ?? "", aNotes[i] ?? "")).ToArray(),
                criteria.Select((name, i) => new MatrixCriterion(name ?? "", decimal.Parse(weights[i] ?? "", NumberStyles.Float, CultureInfo.InvariantCulture), cNotes[i] ?? "")).ToArray(),
                rawScores.Select((score, i) => new MatrixScore(i / criteria.Length, i % criteria.Length,
                    string.IsNullOrWhiteSpace(score) ? null : int.Parse(score, CultureInfo.InvariantCulture), rationales[i] ?? "")).ToArray());
            var versions = form!["expectedUpdatedUtc"];
            var expectedVersion = versions.Count == 1 ? versions[0] : null;
            return store.Save(draft, actor, editId is not null, expectedVersion)
                ? Back("saved")
                : editId is null ? Results.NotFound() : Results.Text("This matrix changed in another session. Reload it before saving again.", statusCode: StatusCodes.Status409Conflict);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { return Invalid(); }
    }

    private static async Task<IResult> SetArchived(string id,string? teamId,bool archived,HttpContext http,IAccountStore accounts,DecisionMatrixStore store)
    { var access=await http.AccessAsync(accounts); var team=teamId is null?null:access.Tree.Find(teamId); var actor=access.Account?.Email; if(team is null||actor is null||!access.CanSee(team.Id)||!access.CanManageGroup(team.Id))return Results.NotFound(); return store.Archive(id,HoursRules.OrgOf(team.Id,access.Tree),team.Id,actor,archived)?Back(archived?"archived":"restored"):Results.NotFound(); }

    private static IResult Back(string status) => Results.Redirect("/admin/decision-matrices?status=" + Uri.EscapeDataString(status));
    private static string Csv(string? value){value??="";var first=0;while(first<value.Length&&(char.IsWhiteSpace(value[first])||char.IsControl(value[first])||char.GetUnicodeCategory(value[first])==UnicodeCategory.Format))first++;if((value.Length>0&&value[0] is '\t' or '\r' or '\n')||(first<value.Length&&"=+-@＝＋－＠".Contains(value[first])))value="'"+value;return value.IndexOfAny([',','"','\n','\r'])>=0?"\""+value.Replace("\"","\"\"")+"\"":value;}
}
