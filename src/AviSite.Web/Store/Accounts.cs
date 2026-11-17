using System.Text.Json;
using System.Text.RegularExpressions;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

/// <summary>What someone can do in /admin. Owners and officers see everything; leads and members only their group's QR codes.</summary>
public enum AccountRole { Member, Lead, Officer, Owner }

/// <summary>A person who can sign in to /admin with Google. The email is the key; removing the account ends access within a minute.</summary>
public sealed class Account
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountRole Role { get; set; }
    /// <summary>Group id for leads and members (their QR codes are named "&lt;group&gt;-…"). Null for officers and owners.</summary>
    public string? Group { get; set; }
    public string? AddedBy { get; set; }
    public DateTimeOffset AddedUtc { get; set; }
    public DateTimeOffset? LastSignInUtc { get; set; }
    /// <summary>Changes whenever role or group changes, so signed-in sessions pick it up (or end).</summary>
    public string Stamp { get; set; } = "";
    /// <summary>True for owners listed in config (Admin:Owners). They can't be removed from the website.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool FromConfig { get; set; }
}

/// <summary>A team or outside club whose people share QR codes: "sms" (Sequoia Marine Solutions), "ccc-er" (CCC Engineering Renaissance)…</summary>
public sealed class AccountGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>"avi" (a team inside AVI) or "partner" (another club or school).</summary>
    public string Kind { get; set; } = "avi";
    public DateTimeOffset AddedUtc { get; set; }
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
        AccountRole.Lead => "Group lead",
        _ => "Member",
    };

    /// <summary>Who may add, change or remove whom. Owners: anyone but config owners (owners only come from config).
    /// Leads: members of their own group.</summary>
    public static bool CanManage(Account actor, Account? target, AccountRole newRole, string? newGroup) => actor.Role switch
    {
        AccountRole.Owner => target is not { FromConfig: true } && newRole != AccountRole.Owner,
        AccountRole.Lead => newRole == AccountRole.Member && newGroup == actor.Group
                            && (target is null || (target.Role == AccountRole.Member && target.Group == actor.Group)),
        _ => false,
    };
}

public interface IAccountStore
{
    /// <summary>Everyone, config owners first.</summary>
    Task<IReadOnlyList<Account>> ListAsync(CancellationToken ct = default);
    Task<Account?> FindAsync(string email, CancellationToken ct = default);
    /// <summary>Adds or updates (by email). Returns false for config owners, which can't be changed here.</summary>
    Task<bool> SaveAsync(Account account, CancellationToken ct = default);
    Task<bool> RemoveAsync(string email, CancellationToken ct = default);
    Task MarkSignInAsync(string email, CancellationToken ct = default);
    Task<IReadOnlyList<AccountGroup>> GroupsAsync(CancellationToken ct = default);
    Task<bool> AddGroupAsync(AccountGroup group, CancellationToken ct = default);
}

/// <summary>Same draft storage as orders: App_Data/accounts.json, atomic rewrite. Owners in config are always present.</summary>
public sealed class JsonAccountStore : IAccountStore
{
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
        _data = File.Exists(_path)
            ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path), JsonDefaults.Options) ?? new()
            : new() { Groups = [new() { Id = "sms", Name = "Sequoia Marine Solutions", Kind = "avi", AddedUtc = clock.GetUtcNow() }] };
        if (!File.Exists(_path)) Save();
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
            // Only config makes owners: a stored "Owner" (shouldn't exist) or a stored copy of a config owner is ignored.
            return [.. owners, .. _data.Accounts.Where(a => a.Role != AccountRole.Owner && owners.All(o => o.Email != a.Email)).OrderBy(a => a.Role == AccountRole.Officer ? 0 : 1).ThenBy(a => a.Group).ThenBy(a => a.Name).Select(Clone)];
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
        if (ConfigOwners().Any(o => o.Email == account.Email) || account.Role == AccountRole.Owner) return false;
        if (account.Role is AccountRole.Officer or AccountRole.Owner) account.Group = null;
        await _lock.WaitAsync(ct);
        try
        {
            var existing = _data.Accounts.FirstOrDefault(a => a.Email == account.Email);
            if (existing is null)
            {
                account.AddedUtc = _clock.GetUtcNow();
                account.Stamp = NewStamp();
                _data.Accounts.Add(Clone(account));
            }
            else
            {
                if (existing.Role != account.Role || existing.Group != account.Group) existing.Stamp = NewStamp();
                existing.Name = account.Name;
                existing.Role = account.Role;
                existing.Group = account.Group;
            }
            Save();
            return true;
        }
        finally { _lock.Release(); }
    }

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

    public async Task<IReadOnlyList<AccountGroup>> GroupsAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _data.Groups.OrderBy(g => g.Kind).ThenBy(g => g.Name).Select(g => new AccountGroup { Id = g.Id, Name = g.Name, Kind = g.Kind, AddedUtc = g.AddedUtc }).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<bool> AddGroupAsync(AccountGroup group, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_data.Groups.Any(g => g.Id == group.Id)) return false;
            group.AddedUtc = _clock.GetUtcNow();
            _data.Groups.Add(group);
            Save();
            return true;
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
        Email = a.Email, Name = a.Name, Role = a.Role, Group = a.Group, AddedBy = a.AddedBy, AddedUtc = a.AddedUtc,
        LastSignInUtc = a.LastSignInUtc, Stamp = a.Stamp, FromConfig = a.FromConfig,
    };
}
