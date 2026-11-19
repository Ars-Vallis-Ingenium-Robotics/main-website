using AviSite.Web.Components.Shared;
using AviSite.Web.Store;

namespace AviSite.Web.Endpoints;

/// <summary>Who a report is for. Sponsor: totals and charts only, no names. Internal: adds per-person detail.</summary>
public enum ReportAudience { Internal, Sponsor }

/// <summary>
/// Everything an hours report shows, worked out once for the web page (/admin/hours/report) and the PDF download
/// (/admin/hours/report.pdf), so the two always agree.
/// </summary>
public sealed class HoursReportData
{
    public required Access Access { get; init; }
    /// <summary>Teams this person may report on, in tree order.</summary>
    public required List<AccountGroup> Teams { get; init; }
    /// <summary>The picked team, or null for "all your teams".</summary>
    public string? Team { get; init; }
    public string? Range { get; init; }
    public ReportAudience Audience { get; init; }
    public required string ScopeName { get; init; }
    public required string RangeLabel { get; init; }
    public required Brand Brand { get; init; }
    public double Total { get; init; }
    public int Sessions { get; init; }
    public int People { get; init; }
    public bool Weekly { get; init; }
    public List<ColumnChart.Point> OverTime { get; init; } = [];
    public List<(string Label, double Value)> ByCategory { get; init; } = [];
    public List<(string Label, double Value)> ByTeam { get; init; } = [];
    public List<(string Label, double Value)> ByPerson { get; init; } = [];
    public List<(string Person, List<double> Cells, double Sum)> Matrix { get; init; } = [];
    public DateTimeOffset Generated { get; init; }

    public string Query(ReportAudience? audience = null) =>
        $"team={Uri.EscapeDataString(Team ?? "")}&range={Uri.EscapeDataString(Range ?? "")}&view={((audience ?? Audience) == ReportAudience.Sponsor ? "sponsor" : "internal")}";

    public static ReportAudience ParseAudience(string? view) => view == "sponsor" ? ReportAudience.Sponsor : ReportAudience.Internal;

    /// <summary>Null if this person manages no teams (reports are for leads, officers and the owner).</summary>
    public static async Task<HoursReportData?> BuildAsync(HttpContext http, IAccountStore accounts, HoursStore store, BrandingStore branding,
        string? team, string? range, string? view)
    {
        var access = await http.AccessAsync(accounts);
        var managed = HoursRules.ManagedTeams(access);
        if (managed.Count == 0) return null;
        if (string.IsNullOrWhiteSpace(team) || !managed.Contains(team)) team = null;
        var teams = access.Tree.Ordered(g => managed.Contains(g.Id)).ToList();
        if (teams.Count == 0) return null;
        var scope = team is null ? managed : access.Tree.WithDescendants([team]);
        var org = HoursRules.OrgOf(team ?? teams[0].Id, access.Tree);
        var now = store.Now;
        var (from, to, label) = HoursRanges.Resolve(range, store.Settings(org), now);
        var entries = store.List(teams: scope, from: from, to: to);
        double H(HoursEntry e) => HoursRanges.Clip(e, from, to, now);
        var names = (await accounts.ListAsync()).ToDictionary(a => a.Email, JsonAccountStore.DisplayName);
        string Name(string e) => names.GetValueOrDefault(e, e);

        var byCategory = entries.GroupBy(e => HoursText.Cat(e.Category)).Select(g => (g.Key, g.Sum(H))).Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList();
        var byPerson = entries.GroupBy(e => e.Email).Select(g => (Name(g.Key), g.Sum(H))).Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList();

        // Over time: weeks for up to ~6 months, months beyond that.
        var overTime = new List<ColumnChart.Point>();
        var weekly = true;
        if (entries.Count > 0)
        {
            var first = DateOnly.FromDateTime((from ?? entries.Min(e => e.StartUtc)).ToPacific().DateTime);
            var last = DateOnly.FromDateTime(((to ?? now) < now ? to!.Value.AddSeconds(-1) : now).ToPacific().DateTime);
            weekly = last.DayNumber - first.DayNumber <= 190;
            var buckets = new List<(DateOnly Start, DateOnly End)>();
            if (weekly) for (var w = HoursText.WeekStart(first); w <= last; w = w.AddDays(7)) buckets.Add((w, w.AddDays(7)));
            else for (var m = new DateOnly(first.Year, first.Month, 1); m <= last; m = m.AddMonths(1)) buckets.Add((m, m.AddMonths(1)));
            overTime = buckets.Select(b =>
            {
                var bf = HoursText.StartOfDayPacific(b.Start); var bt = HoursText.StartOfDayPacific(b.End);
                var v = entries.Sum(e => HoursRanges.Clip(e, from is { } f && f > bf ? f : bf, to is { } t && t < bt ? t : bt, now));
                return new ColumnChart.Point(weekly ? b.Start.ToString("MMM d") : b.Start.ToString("MMMM yyyy"), weekly ? b.Start.ToString("M/d") : b.Start.ToString("MMM"), Math.Round(v, 1));
            }).ToList();
        }

        return new HoursReportData
        {
            Access = access, Teams = teams, Team = team, Range = range, Audience = ParseAudience(view),
            ScopeName = team is null ? "All your teams" : access.Tree.Path(team),
            RangeLabel = label,
            Brand = branding.Resolve(team ?? org, access.Tree),
            Total = entries.Sum(H),
            Sessions = entries.Count,
            People = byPerson.Count,
            Weekly = weekly,
            OverTime = overTime,
            ByCategory = byCategory,
            ByTeam = entries.GroupBy(e => e.Team).Select(g => (access.Tree.Name(g.Key), g.Sum(H))).Where(x => x.Item2 > 0).OrderByDescending(x => x.Item2).ToList(),
            ByPerson = byPerson,
            Matrix = entries.GroupBy(e => e.Email).Select(g => (Name(g.Key), byCategory.Select(c => g.Where(e => HoursText.Cat(e.Category) == c.Item1).Sum(H)).ToList(), g.Sum(H)))
                .Where(r => r.Item3 > 0).OrderByDescending(r => r.Item3).ToList(),
            Generated = now,
        };
    }
}
