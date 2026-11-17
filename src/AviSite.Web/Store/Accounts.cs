using System.Text.Json;
using System.Text.RegularExpressions;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>
/// Site-wide access. Owners and officers see everything in /admin. Everyone else gets what their team memberships give them.
/// (Lead is only used inside a <see cref="Membership"/>; old files that stored it here are migrated.)
/// </summary>
public enum AccountRole { Member, Lead, Officer, Owner }

/// <summary>Someone's place on one team. Leads manage that team and every team under it.</summary>
public sealed class Membership
{
    public string Group { get; set; } = "";
    /// <summary><see cref="AccountRole.Lead"/> or <see cref="AccountRole.Member"/>.</summary>
    public AccountRole Role { get; set; } = AccountRole.Member;
}

/// <summary>A person who can sign in to /admin with Google. The email is the key; removing the account ends access on their next click.</summary>
public sealed class Account
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Owner/Officer for site-wide access; Member means "only what my teams give me".</summary>
    public AccountRole Role { get; set; }
    public List<Membership> Teams { get; set; } = [];
    public string? AddedBy { get; set; }
    public DateTimeOffset AddedUtc { get; set; }
    public DateTimeOffset? LastSignInUtc { get; set; }
    /// <summary>Changes whenever access changes, so signed-in sessions pick it up (or end).</summary>
    public string Stamp { get; set; } = "";
    /// <summary>True for owners listed in config (Admin:Owners). They can't be removed from the website.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool FromConfig { get; set; }

    /// <summary>Old single-group field (before teams). Read once and migrated into <see cref="Teams"/>.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("group")] public string? LegacyGroup { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public bool SiteWide => Role is AccountRole.Owner or AccountRole.Officer;
}

/// <summary>
/// An organization or a team inside one. Top-level groups are organizations (AVI Robotics, a partner club);
/// teams nest under them as deep as needed (CCC Engineering Renaissance → Crush Depth → Electrical).
/// The id starts the group's QR code names ("crush-depth-flyer"), so it's unique across the whole tree.
/// </summary>
public sealed class AccountGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Parent { get; set; }
    public DateTimeOffset AddedUtc { get; set; }
}

/// <summary>The group tree, with the helpers every tool needs (who's under whom).</summary>
public sealed class GroupTree
{
    public IReadOnlyList<AccountGroup> All { get; }
    private readonly Dictionary<string, AccountGroup> _byId;

    public GroupTree(IEnumerable<AccountGroup> groups)
    {
        All = groups.ToList();
        _byId = All.ToDictionary(g => g.Id);
    }

    public AccountGroup? Find(string? id) => id is not null && _byId.TryGetValue(id, out var g) ? g : null;
    public string Name(string? id) => Find(id)?.Name ?? id ?? "";
    public IEnumerable<AccountGroup> Children(string? parent) => All.Where(g => g.Parent == parent).OrderBy(g => g.Name);

    /// <summary>The group and everything below it.</summary>
    public HashSet<string> WithDescendants(IEnumerable<string> ids)
    {
        var set = new HashSet<string>();
        var stack = new Stack<string>(ids.Where(_byId.ContainsKey));
        while (stack.TryPop(out var id))
            if (set.Add(id)) foreach (var c in All.Where(g => g.Parent == id)) stack.Push(c.Id);
        return set;
    }

    /// <summary>"CCC Engineering Renaissance › Crush Depth"</summary>
    public string Path(string id)
    {
        var names = new List<string>();
        for (var g = Find(id); g is not null && names.Count < 10; g = Find(g.Parent)) names.Insert(0, g.Name);
        return string.Join(" › ", names);
    }

    public int Depth(string id)
    {
        var d = 0;
        for (var g = Find(id); g?.Parent is not null && d < 10; g = Find(g.Parent)) d++;
        return d;
    }

    /// <summary>Depth-first, parents before children, for indented lists and selects.</summary>
    public IEnumerable<AccountGroup> Ordered(Func<AccountGroup, bool>? include = null)
    {
        IEnumerable<AccountGroup> Walk(string? parent)
        {
            foreach (var g in Children(parent))
            {
                if (include is null || include(g)) yield return g;
                foreach (var c in Walk(g.Id)) yield return c;
            }
        }
        return Walk(null);
    }
}

/// <summary>
/// What one signed-in person may do, worked out from their account and the group tree. Reuse this in every admin tool.
/// </summary>
public sealed class Access
{
    public Account? Account { get; init; }
    public bool SiteWide { get; init; }
    public bool IsOwner { get; init; }
    /// <summary>Groups they lead, plus everything below those: they manage these teams' people, sub-teams and content.</summary>
    public HashSet<string> Manages { get; init; } = [];
    /// <summary>Every group they're on (any role) plus everything they manage.</summary>
    public HashSet<string> BelongsTo { get; init; } = [];
    public GroupTree Tree { get; init; } = new([]);

    public bool CanManageGroup(string? id) => SiteWide || (id is not null && Manages.Contains(id));
    /// <summary>Adding or removing teams under <paramref name="parent"/> (null = new organizations): the owner anywhere,
    /// and leads below the teams they lead. Officers don't get this from being officers: an AVI officer can't
    /// reshape a partner's Crush Depth.</summary>
    public bool CanEditTeamsUnder(string? parent) => IsOwner || (parent is not null && Manages.Contains(parent));
    public bool LeadsAnything => SiteWide || Manages.Count > 0;

    public static Access For(Account? a, GroupTree tree)
    {
        if (a is null) return new Access { Tree = tree };
        var leads = tree.WithDescendants(a.Teams.Where(t => t.Role == AccountRole.Lead).Select(t => t.Group));
        var on = a.Teams.Select(t => t.Group).Where(id => tree.Find(id) is not null).ToHashSet();
        on.UnionWith(leads);
        return new Access { Account = a, SiteWide = a.SiteWide, IsOwner = a.Role == AccountRole.Owner, Manages = leads, BelongsTo = on, Tree = tree };
    }

    /// <summary>Old shared team passwords: act like a lead of that team.</summary>
    public static Access ForTeamPassword(string team, GroupTree tree)
    {
        var set = tree.WithDescendants([team]);
        if (set.Count == 0) set.Add(team);
        return new() { Manages = set, BelongsTo = [.. set], Tree = tree };
    }

    /// <summary>The shared officer password (no account).</summary>
    public static Access ForOfficerPassword(GroupTree tree) => new() { SiteWide = true, Tree = tree };

    /// <summary>Owners: anyone except config owners. Officers: anyone without site-wide access.
    /// Leads: people on the teams they manage (only those memberships).</summary>
    public bool CanEditPerson(Account target) =>
        IsOwner ? !target.FromConfig
        : SiteWide ? !target.SiteWide
        : !target.SiteWide && target.Teams.Any(t => Manages.Contains(t.Group)) && !Outranks(target);

    /// <summary>
    /// True if the target leads something this person doesn't: e.g. the lead of an organization outranks a sub-team lead,
    /// so the top lead of each organization acts as its owner and can't be changed from below. Co-leads of the same team are peers.
    /// </summary>
    public bool Outranks(Account target) => !SiteWide && !Access.For(target, Tree).Manages.IsSubsetOf(Manages);
}

public static partial class AccountRules
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,18}[a-z0-9])?$")]
    private static partial Regex GroupPattern();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    public static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();

    public static string? CheckEmail(string? email) =>
        EmailPattern().IsMatch(Normalize(email)) ? null : "Use the Google email they'll sign in with.";

    public static string? CheckGroupId(string? id) =>
        string.IsNullOrWhiteSpace(id) ? "Give the group a short id." :
        !GroupPattern().IsMatch(id) ? "Lowercase letters, numbers and dashes, up to 20 (it starts their QR code names)." : null;

    public static string Label(AccountRole r) => r switch
    {
        AccountRole.Owner => "Owner",
        AccountRole.Officer => "Officer",
        AccountRole.Lead => "Lead",
        _ => "Member",
    };
}

public interface IAccountStore
{
    /// <summary>Everyone, config owners first.</summary>
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default);
    Task<Account?> FindAsync(string email, CancellationToken ct = default);
    /// <summary>Adds or replaces (by email). Returns false for config owners and for "Owner" (only config makes owners).</summary>
    Task<bool> SaveAsync(Account account, CancellationToken ct = default);
    Task<bool> RemoveAsync(string email, CancellationToken ct = default);
    Task MarkSignInAsync(string email, CancellationToken ct = default);
    Task<GroupTree> GroupsAsync(CancellationToken ct = default);
    /// <summary>False if the id is taken or the parent doesn't exist.</summary>
    Task<bool> AddGroupAsync(AccountGroup group, CancellationToken ct = default);
    /// <summary>False if it still has sub-teams or people (they have to be moved or taken off first).</summary>
    Task<bool> RemoveGroupAsync(string id, CancellationToken ct = default);
}

/// <summary>Same draft storage as orders: App_Data/accounts.json, atomic rewrite. Owners in config are always present.</summary>
public sealed class JsonAccountStore : IAccountStore
{
    public const string AviRoot = "avi";

    private sealed class Data
    {
        public List<Account> Accounts { get; set; } = [];
        public List<AccountGroup> Groups { get; set; } = [];
        /// <summary>Last sign-in of config owners (they aren't stored as accounts, so removing one from config ends it).</summary>
        public Dictionary<string, DateTimeOffset> OwnerSignIns { get; set; } = [];
    }

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Data _data;
    private readonly TimeProvider _clock;
    private readonly IOptionsMonitor<AdminOptions> _options;

    public JsonAccountStore(IOptions<StoreOptions> options, IOptionsMonitor<AdminOptions> admin, IWebHostEnvironment env, TimeProvider clock)
    {
        _clock = clock;
        _options = admin;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "accounts.json");
        _data = File.Exists(_path) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path), JsonDefaults.Options) ?? new() : new();
        if (Migrate(_data, clock.GetUtcNow()) || !File.Exists(_path)) Save();
    }

    /// <summary>
    /// Brings older files up to date: AVI Robotics becomes the top group with its teams (SMS) under it,
    /// and the old one-group-per-person field becomes a team membership.
    /// </summary>
    private static bool Migrate(Data d, DateTimeOffset now)
    {
        var changed = false;
        if (d.Groups.All(g => g.Id != AviRoot))
        {
            d.Groups.Insert(0, new AccountGroup { Id = AviRoot, Name = "AVI Robotics", AddedUtc = now });
            changed = true;
        }
        if (d.Groups.All(g => g.Id != "sms"))
        {
            d.Groups.Add(new AccountGroup { Id = "sms", Name = "Sequoia Marine Solutions", Parent = AviRoot, AddedUtc = now });
            changed = true;
        }
        // Before sub-teams, "sms" was a top-level "AVI team": it belongs under AVI Robotics.
        if (d.Groups.FirstOrDefault(g => g.Id == "sms") is { Parent: null } sms) { sms.Parent = AviRoot; changed = true; }
        foreach (var a in d.Accounts)
        {
            if (a.LegacyGroup is { } g)
            {
                if (a.Teams.All(t => t.Group != g)) a.Teams.Add(new Membership { Group = g, Role = a.Role == AccountRole.Lead ? AccountRole.Lead : AccountRole.Member });
                a.LegacyGroup = null;
                changed = true;
            }
            if (a.Role == AccountRole.Lead) { a.Role = AccountRole.Member; changed = true; }
        }
        return changed;
    }

    private IEnumerable<Account> ConfigOwners() => _options.CurrentValue.Owners
        .Select(AccountRules.Normalize).Where(e => e.Length > 0).Distinct()
        .Select(e => new Account { Email = e, Name = e, Role = AccountRole.Owner, FromConfig = true, Stamp = "config" });

    public async Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var owners = ConfigOwners().ToList();
            foreach (var o in owners) o.LastSignInUtc = _data.OwnerSignIns.TryGetValue(o.Email, out var t) ? t : null;
            // Only config makes owners: a stored "Owner", or a stored copy of a config owner, is ignored.
            return [.. owners, .. _data.Accounts.Where(a => a.Role != AccountRole.Owner && owners.All(o => o.Email != a.Email))
                .OrderBy(a => a.Role == AccountRole.Officer ? 0 : 1).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).Select(Clone)];
        }
        finally { _lock.Release(); }
    }

    public async Task<Account?> FindAsync(string email, CancellationToken ct = default)
    {
        email = AccountRules.Normalize(email);
        return (await ListAsync(ct)).FirstOrDefault(a => a.Email == email);
    }

    public async Task<bool> SaveAsync(Account account, CancellationToken ct = default)
    {
        account.Email = AccountRules.Normalize(account.Email);
        if (ConfigOwners().Any(o => o.Email == account.Email) || account.Role is AccountRole.Owner or AccountRole.Lead) return false;
        await _lock.WaitAsync(ct);
        try
        {
            var teams = account.Teams.Where(t => _data.Groups.Any(g => g.Id == t.Group))
                .GroupBy(t => t.Group).Select(g => new Membership { Group = g.Key, Role = g.Any(t => t.Role == AccountRole.Lead) ? AccountRole.Lead : AccountRole.Member }).ToList();
            var existing = _data.Accounts.FirstOrDefault(a => a.Email == account.Email);
            if (existing is null)
            {
                account.AddedUtc = _clock.GetUtcNow();
                account.Stamp = NewStamp();
                account.Teams = teams;
                _data.Accounts.Add(Clone(account));
            }
            else
            {
                if (existing.Role != account.Role || Key(existing.Teams) != Key(teams)) existing.Stamp = NewStamp();
                existing.Name = account.Name;
                existing.Role = account.Role;
                existing.Teams = teams;
            }
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }

    private static string Key(IEnumerable<Membership> teams) => string.Join(",", teams.OrderBy(t => t.Group).Select(t => $"{t.Group}:{t.Role}"));

    public async Task<bool> RemoveAsync(string email, CancellationToken ct = default)
    {
        email = AccountRules.Normalize(email);
        if (ConfigOwners().Any(o => o.Email == email)) return false;
        await _lock.WaitAsync(ct);
        try
        {
            var removed = _data.Accounts.RemoveAll(a => a.Email == email) > 0;
            if (removed) Save();
            return removed;
        }
        finally { _lock.Release(); }
    }

    public async Task MarkSignInAsync(string email, CancellationToken ct = default)
    {
        email = AccountRules.Normalize(email);
        await _lock.WaitAsync(ct);
        try
        {
            var now = _clock.GetUtcNow();
            if (ConfigOwners().Any(o => o.Email == email)) _data.OwnerSignIns[email] = now;
            else if (_data.Accounts.FirstOrDefault(x => x.Email == email) is { } a) a.LastSignInUtc = now;
            else return;
            Save();
        }
        finally { _lock.Release(); }
    }

    public async Task<GroupTree> GroupsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return new GroupTree(_data.Groups.Select(g => new AccountGroup { Id = g.Id, Name = g.Name, Parent = g.Parent, AddedUtc = g.AddedUtc })); }
        finally { _lock.Release(); }
    }

    public async Task<bool> AddGroupAsync(AccountGroup group, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_data.Groups.Any(g => g.Id == group.Id)) return false;
            if (group.Parent is not null && _data.Groups.All(g => g.Id != group.Parent)) return false;
            group.AddedUtc = _clock.GetUtcNow();
            _data.Groups.Add(group);
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> RemoveGroupAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (id == AviRoot || _data.Groups.Any(g => g.Parent == id) || _data.Accounts.Any(a => a.Teams.Any(t => t.Group == id))) return false;
            var removed = _data.Groups.RemoveAll(g => g.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
        finally { _lock.Release(); }
    }

    /// <summary>Name the owner gave someone, kept when their Google name differs.</summary>
    public static string DisplayName(Account a) => string.IsNullOrWhiteSpace(a.Name) ? a.Email : a.Name;

    private static string NewStamp() => Guid.NewGuid().ToString("N")[..12];

    private void Save()
    {
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, JsonDefaults.Options));
        File.Move(tmp, _path, overwrite: true);
    }

    private static Account Clone(Account a) => new()
    {
        Email = a.Email, Name = a.Name, Role = a.Role, AddedBy = a.AddedBy, AddedUtc = a.AddedUtc,
        LastSignInUtc = a.LastSignInUtc, Stamp = a.Stamp, FromConfig = a.FromConfig,
        Teams = a.Teams.Select(t => new Membership { Group = t.Group, Role = t.Role }).ToList(),
    };
}
