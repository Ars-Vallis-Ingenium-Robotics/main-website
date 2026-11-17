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
            if (me is null) return Back(error: "denied");
            if (AccountRules.CheckEmail(email) is not null) return Back(error: "email");
            email = AccountRules.Normalize(email);
            group = string.IsNullOrWhiteSpace(group) ? null : group.Trim().ToLowerInvariant();
            if (me.Role == AccountRole.Lead) group = me.Group; // leads can only fill their own group
            if (role is AccountRole.Lead or AccountRole.Member)
            {
                if (group is null) return Back(error: "group");
                if ((await store.GroupsAsync()).All(g => g.Id != group)) return Back(error: "group");
            }
            else group = null;

            var target = await store.FindAsync(email);
            if (target?.Email == me.Email) return Back(error: "self");
            if (!AccountRules.CanManage(me, target, role, group)) return Back(error: "denied");
            var ok = await store.SaveAsync(new Account
            {
                Email = email, Name = string.IsNullOrWhiteSpace(name) ? target?.Name ?? email : name.Trim(),
                Role = role, Group = group, AddedBy = target?.AddedBy ?? me.Email,
            });
            return ok ? Back(done: target is null ? "added" : "saved") : Back(error: "denied");
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
            if (me?.Role != AccountRole.Owner) return Back(error: "denied");
            id = (id ?? "").Trim().ToLowerInvariant();
            if (AccountRules.CheckGroupId(id) is not null || string.IsNullOrWhiteSpace(name)) return Back(error: "group-id");
            var added = await store.AddGroupAsync(new AccountGroup { Id = id, Name = name.Trim(), Kind = kind == "partner" ? "partner" : "avi" });
            return added ? Back(done: "group") : Back(error: "group-taken");
        });
    }

    private static IResult Back(string? done = null, string? error = null) =>
        Results.LocalRedirect("/admin/people" + (done is not null ? "?done=" + done : error is not null ? "?error=" + error : ""));
}
