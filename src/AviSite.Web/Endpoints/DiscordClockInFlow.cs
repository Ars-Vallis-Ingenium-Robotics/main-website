using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AviSite.Web.Discord;
using AviSite.Web.Options;
using AviSite.Web.Store;

namespace AviSite.Web.Endpoints;

/// <summary>Private, short-lived Discord component flow for choosing a clock-in team and category.</summary>
internal static class DiscordClockInFlow
{
    private const string Prefix = "avi:clock-flow:";
    private const int PageSize = 25;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumFuture = TimeSpan.FromMinutes(15);

    private sealed record Step(string Action, string UserId, string? Team, int Page, long ExpiresAt);

    public static bool IsFlow(string customId) => customId.StartsWith(Prefix, StringComparison.Ordinal);

    public static async Task<IResult> StartAsync(Account account, DiscordGuild? server, IAccountStore accounts,
        HoursStore store, DiscordOptions config, TimeProvider clock, CancellationToken ct)
    {
        if (store.Running(account.Email) is { } running)
            return Ephemeral($"You're already clocked in · {HoursText.Cat(running.Category)} · {HoursText.Duration(running.Duration(store.Now))}.");

        var tree = await accounts.GroupsAsync(ct);
        var teams = ClockTeams(account, server, tree);
        if (teams.Count == 0)
            return Ephemeral("You're not on a team you can log hours for yet. Ask your team lead to add you.");

        var defaultTeam = DefaultTeam(account, teams, store);
        var expiry = clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        return teams.Count > 1
            ? TeamPrompt(account, teams, defaultTeam, tree, config, 0, expiry, update: false)
            : CategoryPrompt(account, teams[0].Id, tree, store, config, clock, update: false);
    }

    public static async Task<IResult> HandleAsync(string customId, int componentType, JsonElement data,
        string discordUserId, Account account, DiscordGuild? server, IAccountStore accounts, HoursStore store,
        DiscordOptions config, TimeProvider clock, CancellationToken ct)
    {
        if (!TryParse(customId, out var step))
            return Update("This clock-in prompt is invalid. Run `/clock in` again.");
        if (step.UserId != discordUserId)
            return Ephemeral("This clock-in prompt belongs to a different Discord account.");

        var now = clock.GetUtcNow();
        var unixNow = now.ToUnixTimeSeconds();
        if (step.ExpiresAt < unixNow || step.ExpiresAt > now.Add(MaximumFuture).ToUnixTimeSeconds())
            return Update("This clock-in prompt expired. Run `/clock in` again.");

        var tree = await accounts.GroupsAsync(ct);
        var teams = ClockTeams(account, server, tree);
        if (teams.Count == 0)
            return Update("You no longer have a team available for this clock-in. Ask your team lead for access.");

        var isButton = componentType == 2;
        var isSelect = componentType == 3;
        var expiry = now.Add(Lifetime).ToUnixTimeSeconds();
        switch (step.Action)
        {
            case "team" when isSelect:
            {
                var value = SelectedValue(data);
                if (value is null)
                    return TeamPrompt(account, teams, DefaultTeam(account, teams, store), tree, config, step.Page, expiry,
                        update: true, notice: "Choose one team from the menu.");
                if (value.StartsWith("team:", StringComparison.Ordinal))
                {
                    var team = value[5..];
                    if (!teams.Any(t => t.Id == team))
                        return TeamPrompt(account, teams, DefaultTeam(account, teams, store), tree, config, step.Page, expiry,
                            update: true, notice: "That team is no longer available. Choose again.");
                    return CategoryPrompt(account, team, tree, store, config, clock, update: true);
                }
                return TeamPrompt(account, teams, DefaultTeam(account, teams, store), tree, config, step.Page, expiry,
                    update: true, notice: "That team choice is invalid. Choose again.");
            }
            case "team-page" when isButton:
                return TeamPrompt(account, teams, DefaultTeam(account, teams, store), tree, config,
                    step.Page, expiry, update: true);
            case "team-later" when isButton:
            {
                return IsAvailableTeam(step.Team, teams)
                    ? CategoryPrompt(account, step.Team!, tree, store, config, clock, update: true)
                    : Update("That team is no longer available. Run `/clock in` again.");
            }
            case "category" when isSelect:
            {
                if (!IsAvailableTeam(step.Team, teams)) return Update("That team is no longer available. Run `/clock in` again.");
                var value = SelectedValue(data);
                if (value is null || !value.StartsWith("category:", StringComparison.Ordinal))
                    return CategoryPrompt(account, step.Team!, tree, store, config, clock, update: true, page: step.Page,
                        notice: "Choose one category from the menu.");
                var categories = store.Categories(HoursRules.OrgOf(step.Team!, tree));
                var category = categories.FirstOrDefault(c => CategoryValue(c) == value);
                if (category is null)
                    return CategoryPrompt(account, step.Team!, tree, store, config, clock, update: true, page: step.Page,
                        notice: "That category is no longer available. Choose again.");
                return ClockIn(account, step.Team!, category, tree, store, config);
            }
            case "category-page" when isButton:
                if (!IsAvailableTeam(step.Team, teams)) return Update("That team is no longer available. Run `/clock in` again.");
                return CategoryPrompt(account, step.Team!, tree, store, config, clock, update: true, page: step.Page);
            case "category-later" when isButton:
                if (!IsAvailableTeam(step.Team, teams)) return Update("That team is no longer available. Run `/clock in` again.");
                return ClockIn(account, step.Team!, "", tree, store, config);
            default:
                return Update("That clock-in choice is no longer available. Run `/clock in` again.");
        }
    }

    private static List<AccountGroup> ClockTeams(Account account, DiscordGuild? server, GroupTree tree)
    {
        var accessible = HoursRules.LoggableTeams(Access.For(account, tree));
        HashSet<string> inServer = server is null ? [] : tree.WithDescendants([server.Team]);
        var candidates = accessible.Where(g => inServer.Contains(g.Id)).ToList();
        return candidates.Count > 0 ? candidates : accessible;
    }

    private static string? DefaultTeam(Account account, IReadOnlyList<AccountGroup> teams, HoursStore store)
    {
        var last = store.LastUsed(account.Email)?.Team;
        return last is not null && teams.Any(t => t.Id == last) ? last : teams.FirstOrDefault()?.Id;
    }

    private static IResult TeamPrompt(Account account, IReadOnlyList<AccountGroup> teams, string? defaultTeam,
        GroupTree tree, DiscordOptions config, int page, long expiresAt, bool update, string? notice = null)
    {
        if (defaultTeam is null || teams.Count < 2) return Ephemeral("You're not on a team you can log hours for yet. Ask your team lead to add you.");
        var pages = PageCount(teams.Count);
        if (page < 0 || page >= pages) return update ? Update("That team page is no longer available. Run `/clock in` again.") : Ephemeral("That team page is no longer available.");
        var options = teams.Skip(page * PageSize).Take(PageSize)
            .Select(t => (object)new { label = Clip(tree.Path(t.Id), 100), value = "team:" + t.Id }).ToArray();
        var components = new object[]
        {
            SelectRow(TeamId("team", account.DiscordUserId!, page, expiresAt), "Choose a team for this clock-in", options),
            ButtonRow(PageButtons("team-page", account.DiscordUserId!, null, page, pages, "team-later", account.DiscordUserId!, defaultTeam, expiresAt)),
        };
        var prefix = notice is null ? "" : notice + " ";
        var content = $"{prefix}Choose a team for this clock-in. **Add later** uses {tree.Path(defaultTeam)} for now; you can change it on the Hours page.";
        return Message(content, components, update);
    }

    private static IResult CategoryPrompt(Account account, string team, GroupTree tree, HoursStore store,
        DiscordOptions config, TimeProvider clock, bool update, int page = 0, string? notice = null)
    {
        var org = HoursRules.OrgOf(team, tree);
        var categories = store.Categories(org);
        var pages = PageCount(categories.Count);
        if (page < 0 || page >= pages) return update ? Update("That category page is no longer available. Run `/clock in` again.") : Ephemeral("No categories are available.");
        var expiry = clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds();
        var options = categories.Skip(page * PageSize).Take(PageSize)
            .Select(c => (object)new { label = Clip(c, 100), value = CategoryValue(c) }).ToArray();
        var components = new object[]
        {
            SelectRow(CategoryId("category", account.DiscordUserId!, team, page, expiry), "Choose a category", options),
            ButtonRow(PageButtons("category-page", account.DiscordUserId!, team, page, pages,
                "category-later", account.DiscordUserId!, team, expiry)),
        };
        var url = HoursUrl(config);
        var later = url is null
            ? "Add later leaves the category blank so you can fill it in on the Hours page."
            : $"Add later leaves the category blank; you can change the team or category here: {url}";
        var prefix = notice is null ? "" : notice + " ";
        return Message($"{prefix}What category are you working on for **{tree.Path(team)}**? {later}", components, update);
    }

    private static List<object> PageButtons(string pageAction, string userId, string? team,
        int page, int pages, string laterAction, string laterUserId, string? laterTeam, long expiresAt)
    {
        var buttons = new List<object>();
        if (pages > 1)
        {
            buttons.Add(new { type = 2, style = 2, label = "Previous", custom_id = PageId(pageAction, userId, team, Math.Max(0, page - 1), expiresAt), disabled = page == 0 });
            buttons.Add(new { type = 2, style = 2, label = "Next", custom_id = PageId(pageAction, userId, team, Math.Min(pages - 1, page + 1), expiresAt), disabled = page >= pages - 1 });
        }
        buttons.Add(new { type = 2, style = 2, label = "Add later", custom_id = LaterId(laterAction, laterUserId, laterTeam, expiresAt) });
        return buttons;
    }

    private static IResult ClockIn(Account account, string team, string category, GroupTree tree, HoursStore store, DiscordOptions config)
    {
        var entry = store.ClockIn(account.Email, team, category);
        if (entry is null) return Update("You're already clocked in.");
        var url = HoursUrl(config);
        var later = url is null ? "" : $" Edit this entry later: {url}";
        return Update($"Clocked in · {tree.Path(team)} · {HoursText.Cat(category)}.{later}");
    }

    private static bool IsAvailableTeam(string? team, IReadOnlyList<AccountGroup> teams) =>
        team is not null && teams.Any(t => t.Id == team);

    private static int PageCount(int count) => Math.Max(1, (count + PageSize - 1) / PageSize);

    private static string? SelectedValue(JsonElement data)
    {
        if (!data.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() != 1) return null;
        var value = values[0];
        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static bool TryParse(string customId, out Step step)
    {
        step = new Step("", "", null, 0, 0);
        var parts = customId.Split(':');
        if (parts.Length < 5 || parts[0] != "avi" || parts[1] != "clock-flow" || !DiscordOptions.IsSnowflake(parts[3])) return false;
        var action = parts[2];
        string? team = null;
        var page = 0;
        string expiryText;
        switch (action)
        {
            case "team" or "team-page" when parts.Length == 6:
                if (!int.TryParse(parts[4], NumberStyles.None, CultureInfo.InvariantCulture, out page)) return false;
                expiryText = parts[5];
                break;
            case "team-later" when parts.Length == 6:
                team = parts[4];
                if (AccountRules.CheckGroupId(team) is not null) return false;
                expiryText = parts[5];
                break;
            case "category" or "category-page" when parts.Length == 7:
                team = parts[4];
                if (AccountRules.CheckGroupId(team) is not null || !int.TryParse(parts[5], NumberStyles.None, CultureInfo.InvariantCulture, out page)) return false;
                expiryText = parts[6];
                break;
            case "category-later" when parts.Length == 6:
                team = parts[4];
                if (AccountRules.CheckGroupId(team) is not null) return false;
                expiryText = parts[5];
                break;
            default:
                return false;
        }
        if (!long.TryParse(expiryText, NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)) return false;
        step = new Step(action, parts[3], team, page, expiresAt);
        return true;
    }

    private static string TeamId(string action, string userId, int page, long expiresAt) =>
        $"{Prefix}{action}:{userId}:{page.ToString(CultureInfo.InvariantCulture)}:{expiresAt.ToString(CultureInfo.InvariantCulture)}";

    private static string CategoryId(string action, string userId, string team, int page, long expiresAt) =>
        $"{Prefix}{action}:{userId}:{team}:{page.ToString(CultureInfo.InvariantCulture)}:{expiresAt.ToString(CultureInfo.InvariantCulture)}";

    private static string PageId(string action, string userId, string? team, int page, long expiresAt) =>
        team is null ? TeamId(action, userId, page, expiresAt) : CategoryId(action, userId, team, page, expiresAt);

    private static string LaterId(string action, string userId, string? team, long expiresAt) =>
        team is null
            ? $"{Prefix}{action}:{userId}:{expiresAt.ToString(CultureInfo.InvariantCulture)}"
            : $"{Prefix}{action}:{userId}:{team}:{expiresAt.ToString(CultureInfo.InvariantCulture)}";

    private static object SelectRow(string customId, string placeholder, object[] options) => new
    {
        type = 1,
        components = new object[]
        {
            new { type = 3, custom_id = customId, placeholder = Clip(placeholder, 100), min_values = 1, max_values = 1, options },
        },
    };

    private static object ButtonRow(List<object> buttons) => new { type = 1, components = buttons.ToArray() };

    private static IResult Message(string content, object[] components, bool update)
    {
        content = Clip(content, 1900);
        // UPDATE_MESSAGE edits the original ephemeral prompt, so its existing private visibility is retained.
        return update
            ? Results.Json(new { type = 7, data = new { content, components, allowed_mentions = new { parse = Array.Empty<string>() } } })
            : Results.Json(new { type = 4, data = new { content, components, flags = 64, allowed_mentions = new { parse = Array.Empty<string>() } } });
    }

    private static IResult Ephemeral(string content) => Message(content, [], update: false);
    private static IResult Update(string content) => Message(content, [], update: true);
    private static string? HoursUrl(DiscordOptions config) =>
        config.PublicBaseUrl is { Length: > 0 } baseUrl ? $"{baseUrl.TrimEnd('/')}/admin/hours" : null;
    private static string CategoryValue(string category) =>
        "category:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(category))).ToLowerInvariant();
    private static string Clip(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
