using System.Text.RegularExpressions;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

public static class ProjectPlanEndpoints
{
    /// <summary>Integration: call builder.Services.AddProjectPlanModule() beside the other module registrations;
    /// call app.MapProjectPlanEndpoints() beside the other endpoint mappings in Program.cs.</summary>
    public static void AddProjectPlanModule(this IServiceCollection services)
    { services.AddSingleton<ProjectPlans>(); services.AddHostedService<ProjectPlanModuleInitializer>(); }

    public static void MapProjectPlanEndpoints(this WebApplication app)
    {
        var group=app.MapGroup("/admin/projects").RequireAuthorization(AdminEndpoints.QrPolicy);
        group.MapPost("/save", async ([FromForm]string? id,[FromForm]string? teamId,[FromForm]string? title,[FromForm]string? description,HttpContext http,IAccountStore accounts,ProjectPlans store)=>
        { var access=await http.AccessAsync(accounts); var actor=access.Account?.Email; var team=access.Tree.Find(teamId); if(actor is null||team is null||!access.CanSee(team.Id)) return Results.NotFound(); var existing=string.IsNullOrWhiteSpace(id)?null:store.Get(id); if(existing is not null&&(existing.TeamId!=team.Id||!access.CanManageGroup(team.Id)))return Results.NotFound(); if(existing is null&&!access.CanManageGroup(team.Id))return Results.NotFound(); title=Clean(title,120); description=Clean(description,2000); if(title is null||description is null)return Back("validation"); var key=store.Save(existing?.Id,team.Id,title,description,actor); return Back("saved",key); });
        group.MapPost("/{id}/archive",async ([FromRoute]string id,HttpContext http,IAccountStore accounts,ProjectPlans store)=>{var a=await http.AccessAsync(accounts);var p=store.Get(id);if(a.Account?.Email is not{} actor||p is null||a.Tree.Find(p.TeamId) is null||!a.CanManageGroup(p.TeamId))return Results.NotFound();store.Archive(id,actor);return Back("archived",id);});
        group.MapPost("/{id}/task",async ([FromRoute]string id,[FromForm]string? taskId,[FromForm]string? title,[FromForm]string? ownerEmail,[FromForm]string? status,[FromForm]string? startDate,[FromForm]string? dueDate,[FromForm]int progress,[FromForm]string? description,HttpContext http,IAccountStore accounts,ProjectPlans store)=>{var a=await http.AccessAsync(accounts);var p=store.Get(id);if(a.Account?.Email is not{} actor||p is null||a.Tree.Find(p.TeamId) is null||!a.CanManageGroup(p.TeamId))return Results.NotFound();title=Clean(title,120);ownerEmail=Clean(ownerEmail,254);description=Clean(description,2000);if(title is null||ownerEmail is null||!Regex.IsMatch(ownerEmail,"^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")||description is null)return Back("validation",id);var form=await http.Request.ReadFormAsync(http.RequestAborted);var deps=form["dependency"].Select(x=>x).ToArray();var result=store.SaveTask(id,taskId,title,ownerEmail,status??"Planned",startDate??"",dueDate??"",progress,description,actor,deps);return result is null?Back("task-validation",id):Back("task-saved",id);});
    }
    private static string? Clean(string? s,int n)=>string.IsNullOrWhiteSpace(s)||s.Trim().Length>n?null:s.Trim();
    private static IResult Back(string result,string? id=null)=>Results.LocalRedirect("/admin/projects?result="+Uri.EscapeDataString(result)+(id is null?"":"&id="+Uri.EscapeDataString(id)));
}
