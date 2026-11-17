using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>
/// /admin/people actions. Who can do what comes from <see cref="Access"/>:
/// owners manage everyone (owners themselves only come from server config); officers manage everyone without site-wide access;
/// team leads manage their teams and every team under them. Results go back as short codes (no emails in URLs).
/// Adding people and teams is handled on the People page itself so a typo doesn't wipe the form.
/// </summary>
public static class AccountAdminEndpoints
{
    public static void MapPeopleAdminEndpoints(this WebApplication app)
    {
        var people = app.MapGroup("/admin/people").RequireAuthorization(AdminEndpoints.PeoplePolicy);

        people.MapPost("/team/add", async ([FromForm] string email, [FromForm] string group, [FromForm] AccountRole role, HttpContext http, IAccountStore store) =>
        {
            var me = await http.AccessAsync(store);
            var target = await store.FindAsync(email);
            if (target is null || !me.CanManageGroup(group) || role is not (AccountRole.Lead or AccountRole.Member) || me.Outranks(target)) return Back(error: "denied");
            if (target.Email == me.Account?.Email) return Back(error: "self");
            target.Teams.RemoveAll(t => t.Group == group);
            target.Teams.Add(new Membership { Group = group, Role = role });
            return await store.SaveAsync(target) ? Back(done: "saved") : Back(error: "denied");
        });

        people.MapPost("/team/remove", async ([FromForm] string email, [FromForm] string group, HttpContext http, IAccountStore store) =>
        {
            var me = await http.AccessAsync(store);
            var target = await store.FindAsync(email);
            if (target is null || !me.CanManageGroup(group) || me.Outranks(target)) return Back(error: "denied");
            if (target.Email == me.Account?.Email) return Back(error: "self");
            target.Teams.RemoveAll(t => t.Group == group);
            // Nobody keeps an account that can't open anything.
            if (!target.SiteWide && target.Teams.Count == 0)
                return await store.RemoveAsync(target.Email) ? Back(done: "removed") : Back(error: "denied");
            return await store.SaveAsync(target) ? Back(done: "saved") : Back(error: "denied");
        });

        people.MapPost("/access", async ([FromForm] string email, [FromForm] AccountRole role, HttpContext http, IAccountStore store) =>
        {
            var me = await http.AccessAsync(store);
            var target = await store.FindAsync(email);
            if (!me.IsOwner || target is null || target.FromConfig || role is not (AccountRole.Officer or AccountRole.Member)) return Back(error: "denied");
            if (target.Email == me.Account?.Email) return Back(error: "self");
            if (role == AccountRole.Member && target.Teams.Count == 0) return Back(error: "no-teams");
            target.Role = role;
            return await store.SaveAsync(target) ? Back(done: "saved") : Back(error: "denied");
        });

        people.MapPost("/team/delete", async ([FromForm] string id, HttpContext http, IAccountStore store, IDynamicQrStore codes) =>
        {
            var me = await http.AccessAsync(store);
            if (me.Tree.Find(id) is not { } team || !me.CanEditTeamsUnder(team.Parent)) return Back(error: "denied");
            if ((await codes.ListAsync()).Any(c => c.Team == id)) return Back(error: "team-has-codes");
            return await store.RemoveGroupAsync(id) ? Back(done: "team-removed") : Back(error: "team-busy");
        });

        people.MapPost("/remove", async ([FromForm] string email, HttpContext http, IAccountStore store) =>
        {
            var me = await http.AccessAsync(store);
            var target = await store.FindAsync(email);
            if (target is null || !CanRemove(me, target)) return Back(error: "denied");
            if (target.Email == me.Account?.Email) return Back(error: "self");
            return await store.RemoveAsync(target.Email) ? Back(done: "removed") : Back(error: "denied");
        });
    }

    /// <summary>Remove the whole person: owners anyone (but config owners), officers anyone without site-wide access,
    /// leads only people who are entirely inside the teams they manage.</summary>
    public static bool CanRemove(Access me, Account target) =>
        me.CanEditPerson(target) && (me.SiteWide || target.Teams.All(t => me.Manages.Contains(t.Group)));

    public sealed record AddResult(int Added, int Updated, List<string> Skipped, string? Error);

    /// <summary>
    /// Adds one or many people to a team (or as officers). Each line: an email, optionally followed by a name
    /// ("ada@example.com, Ada Lovelace"; commas or tabs, so a pasted spreadsheet column works).
    /// People who already have an account just get the team added.
    /// </summary>
    public static async Task<AddResult> AddPeopleAsync(Access me, string? text, string? group, AccountRole teamRole, bool officer, IAccountStore store)
    {
        group = string.IsNullOrWhiteSpace(group) ? null : group;
        if (officer && !me.IsOwner) return new(0, 0, [], "denied");
        if (!officer && group is null) return new(0, 0, [], "group");
        if (group is not null && !me.CanManageGroup(group)) return new(0, 0, [], "denied");
        if (teamRole is not (AccountRole.Lead or AccountRole.Member)) teamRole = AccountRole.Member;

        var lines = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0) return new(0, 0, [], "empty");
        if (lines.Count > 300) return new(0, 0, [], "too-many");

        int added = 0, updated = 0;
        var skipped = new List<string>();
        foreach (var line in lines)
        {
            var parts = line.Split([',', '\t', ';'], 2, StringSplitOptions.TrimEntries);
            // "Name <email>" and "email, name" both work.
            var email = parts.FirstOrDefault(p => p.Contains('@')) ?? "";
            var name = parts.FirstOrDefault(p => !p.Contains('@'));
            if (line.Contains('<') && line.Contains('>'))
            {
                var lt = line.IndexOf('<'); var gt = line.IndexOf('>', lt);
                if (gt > lt) { email = line[(lt + 1)..gt]; name = line[..lt].Trim().Trim('"', ','); }
            }
            if (AccountRules.CheckEmail(email) is not null) { skipped.Add($"{line} (not an email)"); continue; }
            email = AccountRules.Normalize(email);
            if (email == me.Account?.Email) { skipped.Add($"{email} (that's you)"); continue; }

            var target = await store.FindAsync(email);
            if (target is { FromConfig: true }) { skipped.Add($"{email} (owner)"); continue; }
            if (target is not null && me.Outranks(target)) { skipped.Add($"{email} (leads above you)"); continue; }
            var account = target ?? new Account { Email = email, Name = string.IsNullOrWhiteSpace(name) ? email : name, AddedBy = me.Account?.Email };
            if (target is not null && !string.IsNullOrWhiteSpace(name) && target.Name == target.Email) account.Name = name;
            if (officer) account.Role = AccountRole.Officer;
            if (group is not null)
            {
                var existing = account.Teams.FirstOrDefault(t => t.Group == group);
                if (existing is null) account.Teams.Add(new Membership { Group = group, Role = teamRole });
                else if (teamRole == AccountRole.Lead) existing.Role = AccountRole.Lead; // never quietly demote a lead
            }
            if (!await store.SaveAsync(account)) { skipped.Add($"{email} (can't change)"); continue; }
            if (target is null) added++; else updated++;
        }
        return new(added, updated, skipped, null);
    }

    /// <summary>Adds an organization (owner) or a sub-team (owner, or a lead of the parent or anything above it).</summary>
    public static async Task<string> AddGroupAsync(Access me, string? id, string? name, string? parent, IAccountStore store)
    {
        parent = string.IsNullOrWhiteSpace(parent) ? null : parent;
        if (!me.CanEditTeamsUnder(parent)) return "denied";
        id = (id ?? "").Trim().ToLowerInvariant();
        if (AccountRules.CheckGroupId(id) is not null || string.IsNullOrWhiteSpace(name)) return "group-id";
        var added = await store.AddGroupAsync(new AccountGroup { Id = id, Name = name.Trim(), Parent = parent });
        return added ? "group" : "group-taken";
    }

    private static IResult Back(string? done = null, string? error = null) =>
        Results.LocalRedirect("/admin/people" + (done is not null ? "?done=" + done : error is not null ? "?error=" + error : ""));
}
