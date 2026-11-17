using System.Globalization;
using AviSite.Web.Store;
using Microsoft.AspNetCore.Mvc;

namespace AviSite.Web.Endpoints;

/// <summary>Who may log, see and change hours. Built on <see cref="Access"/>, like every admin tool.</summary>
public static class HoursRules
{
    /// <summary>The organization a team belongs to (its top group). Settings, categories and leaderboards are per organization.</summary>
    public static string OrgOf(string team, GroupTree tree)
    {
        var id = team;
        for (var g = tree.Find(team); g?.Parent is not null; g = tree.Find(g.Parent)) id = g.Parent;
        return id;
    }

    /// <summary>Teams someone can clock into: every team they're on or manage (owners with no team: AVI Robotics).</summary>
    public static List<AccountGroup> LoggableTeams(Access a)
    {
        var teams = a.Tree.Ordered(g => a.BelongsTo.Contains(g.Id)).ToList();
        if (teams.Count == 0 && a.IsOwner && a.Tree.Find(JsonAccountStore.AviRoot) is { } avi) teams.Add(avi);
        return teams;
    }

    /// <summary>Their own entries, or anyone's on a team they manage (unless that person leads above them).</summary>
    public static bool CanEdit(Access a, HoursEntry e, Account? person) =>
        e.Email == a.Account?.Email || (a.CanManageGroup(e.Team) && (person is null || !a.Outranks(person)));

    /// <summary>Teams whose hours they can see and fix (team page, report).</summary>
    public static HashSet<string> ManagedTeams(Access a) => a.IsOwner ? a.Tree.All.Select(g => g.Id).ToHashSet() : a.Manages;

    /// <summary>Organizations whose hours settings (leaderboard, season, categories) they can change: the owner, and each organization's leads.</summary>
    public static IEnumerable<AccountGroup> SettingsOrgs(Access a) =>
        a.Tree.Children(null).Where(o => a.IsOwner || a.Leads.Contains(o.Id));

    /// <summary>A date and two clock times (Pacific) → UTC start and end. An end earlier than the start means past midnight.</summary>
    public static string? ParseTimes(string? date, string? start, string? end, DateTimeOffset now, out DateTimeOffset s, out DateTimeOffset e)
    {
        s = e = default;
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return "date";
        if (!TimeOnly.TryParse(start, CultureInfo.InvariantCulture, out var ts) || !TimeOnly.TryParse(end, CultureInfo.InvariantCulture, out var te)) return "time";
        s = HoursText.FromPacific(d.ToDateTime(ts));
        var endDay = te <= ts ? d.AddDays(1) : d;
        e = HoursText.FromPacific(endDay.ToDateTime(te));
        if (e - s > HoursStore.LongSession) return "too-long";
        if (s > now.AddMinutes(5)) return "future";
        return null;
    }
}

/// <summary>
/// The buttons on /admin/hours and /admin/hours/team. Clocking in and out are each two taps on the page
/// (a button that opens a confirm, then the confirm), so a stray tap never starts or ends a session.
/// Every action lands back on the page it came from with a short result code.
/// </summary>
public static class HoursEndpoints
{
    public static void MapHoursEndpoints(this WebApplication app)
    {
        // Short link for posters and QR codes: /hours?team=sms&category=Build opens the clock with those picked.
        app.MapGet("/hours", (HttpContext http) => Results.Redirect("/admin/hours" + http.Request.QueryString));

        var hours = app.MapGroup("/admin/hours").RequireAuthorization(AdminEndpoints.QrPolicy);

        // Everything in a range as a spreadsheet, for sponsor reports, letters and awards.
        hours.MapGet("/export.csv", async (string? team, string? range, HttpContext http, IAccountStore accounts, HoursStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            var managed = HoursRules.ManagedTeams(me);
            if (managed.Count == 0) return Results.NotFound();
            var scope = !string.IsNullOrEmpty(team) && managed.Contains(team) ? me.Tree.WithDescendants([team]) : managed;
            var org = !string.IsNullOrEmpty(team) && managed.Contains(team) ? HoursRules.OrgOf(team, me.Tree) : HoursRules.OrgOf(managed.First(), me.Tree);
            var (from, to, label) = HoursRanges.Resolve(range, store.Settings(org), store.Now);
            var names = (await accounts.ListAsync()).ToDictionary(a => a.Email, JsonAccountStore.DisplayName);
            var sb = new System.Text.StringBuilder("name,email,organization,team,category,date,start,end,hours,note,source\n");
            foreach (var e in store.List(teams: scope, from: from, to: to).Where(e => !e.Running).OrderBy(e => e.StartUtc))
            {
                var s = e.StartUtc.ToPacific(); var en = e.EndUtc!.Value.ToPacific();
                sb.AppendJoin(',', Csv(names.GetValueOrDefault(e.Email, e.Email)), Csv(e.Email), Csv(me.Tree.Name(HoursRules.OrgOf(e.Team, me.Tree))),
                    Csv(me.Tree.Name(e.Team)), Csv(e.Category), s.ToString("yyyy-MM-dd"), s.ToString("HH:mm"), en.ToString("HH:mm"),
                    HoursRanges.Clip(e, from, to, store.Now).ToString("0.00", CultureInfo.InvariantCulture), Csv(e.Note), e.Source).Append('\n');
            }
            var file = $"hours-{(team ?? "all")}-{label.ToLowerInvariant().Replace(' ', '-').Replace("–", "-")}.csv";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", file);
        });

        hours.MapPost("/in", async ([FromForm] string team, [FromForm] string category, HttpContext http, IAccountStore accounts, HoursStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (me.Account is null) return Back("/admin/hours", error: "account");
            if (HoursRules.LoggableTeams(me).All(g => g.Id != team)) return Back("/admin/hours", error: "team");
            if (!store.Categories(HoursRules.OrgOf(team, me.Tree)).Contains(category)) return Back("/admin/hours", error: "category");
            return store.ClockIn(me.Account.Email, team, category) is null ? Back("/admin/hours", error: "already-in") : Back("/admin/hours", done: "in");
        });

        hours.MapPost("/out", async ([FromForm] string? note, HttpContext http, IAccountStore accounts, HoursStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            if (me.Account is null) return Back("/admin/hours", error: "account");
            var e = store.ClockOut(me.Account.Email, note);
            if (e is null) return Back("/admin/hours", error: "not-in");
            // A forgotten clock-out shouldn't silently become a 30-hour entry: cap it and say so.
            if (e.Duration(store.Now) > HoursStore.LongSession)
            {
                e.EndUtc = e.StartUtc + HoursStore.LongSession;
                store.Update(e, me.Account.Email);
                return Back("/admin/hours", done: "out-capped");
            }
            return Back("/admin/hours", done: "out");
        });

        // Add (no id) or change an entry. Members: their own. Leads: anyone's on their teams (email given).
        hours.MapPost("/save", async ([FromForm] long? id, [FromForm] string? email, [FromForm] string team, [FromForm] string category,
            [FromForm] string? date, [FromForm] string? start, [FromForm] string? end, [FromForm] string? note, [FromForm] string? back,
            HttpContext http, IAccountStore accounts, HoursStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            var to = SafeBack(back);
            if (me.Account is null) return Back(to, error: "account");
            var err = HoursRules.ParseTimes(date, start, end, store.Now, out var s, out var e);
            if (err is not null) return Back(to, error: err);
            if (!store.Categories(HoursRules.OrgOf(team, me.Tree)).Contains(category)) return Back(to, error: "category");
            note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 200)];

            if (id is { } existingId)
            {
                var entry = store.Get(existingId);
                if (entry is null) return Back(to, error: "gone");
                var person = await accounts.FindAsync(entry.Email);
                if (!HoursRules.CanEdit(me, entry, person)) return Back(to, error: "denied");
                // Moving it to another team needs the same right on the new team.
                var mine = entry.Email == me.Account.Email;
                if (mine ? HoursRules.LoggableTeams(me).All(g => g.Id != team) && !me.CanManageGroup(team) : !me.CanManageGroup(team)) return Back(to, error: "team");
                entry.Team = team; entry.Category = category; entry.StartUtc = s; entry.EndUtc = e; entry.Note = note;
                return store.Update(entry, me.Account.Email) ? Back(to, done: "saved") : Back(to, error: "gone");
            }

            var who = string.IsNullOrWhiteSpace(email) ? me.Account.Email : AccountRules.Normalize(email);
            if (who == me.Account.Email)
            {
                if (HoursRules.LoggableTeams(me).All(g => g.Id != team)) return Back(to, error: "team");
            }
            else
            {
                var person = await accounts.FindAsync(who);
                if (person is null || !me.CanManageGroup(team) || me.Outranks(person)) return Back(to, error: "denied");
            }
            store.Add(new HoursEntry { Email = who, Team = team, Category = category, StartUtc = s, EndUtc = e, Note = note, Source = who == me.Account.Email ? "manual" : "lead" }, me.Account.Email);
            return Back(to, done: "added");
        });

        hours.MapPost("/delete", async ([FromForm] long id, [FromForm] string? back, HttpContext http, IAccountStore accounts, HoursStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            var to = SafeBack(back);
            var entry = store.Get(id);
            if (entry is null) return Back(to, error: "gone");
            if (!HoursRules.CanEdit(me, entry, await accounts.FindAsync(entry.Email))) return Back(to, error: "denied");
            return store.Delete(id) ? Back(to, done: "deleted") : Back(to, error: "gone");
        });

        // Leads: end someone's forgotten session at the time they actually left.
        hours.MapPost("/stop", async ([FromForm] long id, [FromForm] string? date, [FromForm] string? end, [FromForm] string? back,
            HttpContext http, IAccountStore accounts, HoursStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            var to = SafeBack(back);
            var entry = store.Get(id);
            if (entry is null || !entry.Running) return Back(to, error: "gone");
            if (!HoursRules.CanEdit(me, entry, await accounts.FindAsync(entry.Email))) return Back(to, error: "denied");
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || !TimeOnly.TryParse(end, CultureInfo.InvariantCulture, out var te))
                return Back(to, error: "time");
            var stop = HoursText.FromPacific(d.ToDateTime(te));
            if (stop <= entry.StartUtc) return Back(to, error: "before-start");
            if (stop - entry.StartUtc > HoursStore.LongSession) return Back(to, error: "too-long");
            if (stop > store.Now.AddMinutes(5)) return Back(to, error: "future");
            entry.EndUtc = stop;
            return store.Update(entry, me.Account?.Email ?? "officer password") ? Back(to, done: "stopped") : Back(to, error: "gone");
        });

        hours.MapPost("/settings", async ([FromForm] string org, [FromForm] bool? leaderboard, [FromForm] int seasonMonth, [FromForm] int seasonDay,
            [FromForm] string? categories, HttpContext http, IAccountStore accounts, HoursStore store) =>
        {
            var me = await http.AccessAsync(accounts);
            const string to = "/admin/hours/settings";
            if (HoursRules.SettingsOrgs(me).All(o => o.Id != org)) return Back(to, error: "denied");
            if (seasonMonth is < 1 or > 12 || seasonDay is < 1 or > 28) return Back(to, error: "season");
            var cats = (categories ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (cats.Count == 0) return Back(to, error: "categories");
            store.SaveSettings(new HoursSettings(org, leaderboard == true, seasonMonth, seasonDay)); // unticked box = not sent
            store.SaveCategories(org, cats);
            return Back(to, done: "settings");
        });
    }

    /// <summary>CSV-escape and neutralise spreadsheet formula injection (=, +, -, @ prefixes).</summary>
    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    private static string SafeBack(string? back) =>
        back is not null && back.StartsWith("/admin/hours", StringComparison.Ordinal) && !back.Contains("//") && !back.Contains('\\') ? back.Split('?')[0] : "/admin/hours";

    private static IResult Back(string to, string? done = null, string? error = null) =>
        Results.LocalRedirect(to + (done is not null ? "?done=" + done : error is not null ? "?error=" + error : ""));
}
