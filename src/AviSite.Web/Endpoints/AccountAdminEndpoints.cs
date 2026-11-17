using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>
/// /admin/people actions. Owners manage everyone (except owners, who only come from server config);
/// group leads add and remove members of their own group. Results go back as short codes (no emails in URLs).
/// </summary>
public static class AccountAdminEndpoints
{
    public static void MapPeopleAdminEndpoints(this WebApplication app)
    {
        var people = app.MapGroup("/admin").RequireAuthorization(AdminEndpoints.PeoplePolicy);

        people.MapPost("/people/save", async ([FromForm] string email, [FromForm] string? name, [FromForm] AccountRole role, [FromForm] string? group,
            HttpContext http, IAccountStore store) =>
        {
            var me = await http.CurrentAsync(store);
            var result = me is null ? "denied" : await SaveAsync(me, email, name, role, group, store);
            return result is "added" or "saved" ? Back(done: result) : Back(error: result);
        });

        people.MapPost("/people/remove", async ([FromForm] string email, HttpContext http, IAccountStore store) =>
        {
            var me = await http.CurrentAsync(store);
            var target = await store.FindAsync(email);
            if (me is null || target is null) return Back(error: "denied");
            if (target.Email == me.Email) return Back(error: "self");
            if (!AccountRules.CanManage(me, target, target.Role, target.Group)) return Back(error: "denied");
            return await store.RemoveAsync(target.Email) ? Back(done: "removed") : Back(error: "denied");
        });

        people.MapPost("/groups/add", async ([FromForm] string id, [FromForm] string name, [FromForm] string? kind, HttpContext http, IAccountStore store) =>
        {
            var me = await http.CurrentAsync(store);
            var result = me is null ? "denied" : await AddGroupAsync(me, id, name, kind, store);
            return result == "group" ? Back(done: result) : Back(error: result);
        });
    }

    /// <summary>Adds or changes a person. Returns "added"/"saved", or an error code the People page explains.</summary>
    public static async Task<string> SaveAsync(Account me, string? email, string? name, AccountRole role, string? group, IAccountStore store)
    {
        if (AccountRules.CheckEmail(email) is not null) return "email";
        email = AccountRules.Normalize(email);
        group = string.IsNullOrWhiteSpace(group) ? null : group.Trim().ToLowerInvariant();
        if (me.Role == AccountRole.Lead) group = me.Group; // leads can only fill their own group
        if (role is AccountRole.Lead or AccountRole.Member)
        {
            if (group is null || (await store.GroupsAsync()).All(g => g.Id != group)) return "group";
        }
        else group = null;

        var target = await store.FindAsync(email);
        if (target?.Email == me.Email) return "self";
        if (!AccountRules.CanManage(me, target, role, group)) return "denied";
        var ok = await store.SaveAsync(new Account
        {
            Email = email, Name = string.IsNullOrWhiteSpace(name) ? target?.Name ?? email : name.Trim(),
            Role = role, Group = group, AddedBy = target?.AddedBy ?? me.Email,
        });
        return !ok ? "denied" : target is null ? "added" : "saved";
    }

    /// <summary>Owners only. Returns "group" on success, or an error code.</summary>
    public static async Task<string> AddGroupAsync(Account me, string? id, string? name, string? kind, IAccountStore store)
    {
        if (me.Role != AccountRole.Owner) return "denied";
        id = (id ?? "").Trim().ToLowerInvariant();
        if (AccountRules.CheckGroupId(id) is not null || string.IsNullOrWhiteSpace(name)) return "group-id";
        var added = await store.AddGroupAsync(new AccountGroup { Id = id, Name = name.Trim(), Kind = kind == "partner" ? "partner" : "avi" });
        return added ? "group" : "group-taken";
    }

    private static IResult Back(string? done = null, string? error = null) =>
        Results.LocalRedirect("/admin/people" + (done is not null ? "?done=" + done : error is not null ? "?error=" + error : ""));
}
