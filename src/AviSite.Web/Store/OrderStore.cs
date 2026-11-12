using System.Security.Cryptography;
using System.Text.Json;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

public interface IOrderStore
{
    Task<Order?> GetAsync(string id, CancellationToken ct = default);
    Task<Order?> FindByPayPalAsync(string? paypalOrderId, string? captureId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> ListAsync(CancellationToken ct = default);
    Task<Order> CreateAsync(Buyer buyer, Quote quote, string currency, CancellationToken ct = default);

    /// <summary>
    /// Runs <paramref name="mutate"/> on the stored order under a lock and persists it if it returns true.
    /// Returns a copy of the order after the update (or null if not found).
    /// </summary>
    Task<Order?> UpdateAsync(string id, Func<Order, bool> mutate, CancellationToken ct = default);
}

/// <summary>
/// Draft storage: one JSON file, whole-file rewrite on every change, atomic rename.
/// Fine for a club shirt run (hundreds of orders). Replace with EF Core + PostgreSQL
/// (separate "avisite" database) before real traffic; the interface stays the same.
/// </summary>
public sealed class JsonOrderStore : IOrderStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly List<Order> _orders;
    private readonly TimeProvider _clock;

    public JsonOrderStore(IOptions<StoreOptions> options, IWebHostEnvironment env, TimeProvider clock)
    {
        _clock = clock;
        var dir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "orders.json");
        _orders = File.Exists(_path)
            ? JsonSerializer.Deserialize<List<Order>>(File.ReadAllText(_path), JsonDefaults.Options) ?? []
            : [];
    }

    public async Task<Order?> GetAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return Clone(_orders.FirstOrDefault(o => o.Id == id)); }
        finally { _lock.Release(); }
    }

    public async Task<Order?> FindByPayPalAsync(string? paypalOrderId, string? captureId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return Clone(_orders.FirstOrDefault(o =>
                (paypalOrderId is not null && o.PayPalOrderId == paypalOrderId) ||
                (captureId is not null && o.PayPalCaptureId == captureId)));
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<Order>> ListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _orders.OrderByDescending(o => o.CreatedUtc).Select(o => Clone(o)!).ToList(); }
        finally { _lock.Release(); }
    }

    public async Task<Order> CreateAsync(Buyer buyer, Quote quote, string currency, CancellationToken ct = default)
    {
        if (quote.IsEmpty) throw new InvalidOperationException("Cannot create an order from an empty quote.");

        await _lock.WaitAsync(ct);
        try
        {
            var order = new Order
            {
                Id = NewId(),
                Code = NewCode(),
                CreatedUtc = _clock.GetUtcNow(),
                Buyer = buyer,
                Lines = quote.Lines.Select(l => new OrderLine(l.Product.Slug, l.Product.Name, l.Size, l.Qty, l.UnitCents)).ToList(),
                TotalCents = quote.TotalCents,
                Currency = currency,
            };
            order.Log("Created at checkout");
            _orders.Add(order);
            try { await PersistAsync(); }
            catch { _orders.Remove(order); throw; } // keep memory == disk
            return Clone(order)!;
        }
        finally { _lock.Release(); }
    }

    public async Task<Order?> UpdateAsync(string id, Func<Order, bool> mutate, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var i = _orders.FindIndex(o => o.Id == id);
            if (i < 0) return null;
            // Mutate a copy; only swap it in once it's on disk, so a failed write can't leave memory saying "Paid".
            var draft = Clone(_orders[i])!;
            if (!mutate(draft)) return Clone(draft);
            var original = _orders[i];
            _orders[i] = draft;
            try { await PersistAsync(); }
            catch { _orders[i] = original; throw; }
            return Clone(draft);
        }
        finally { _lock.Release(); }
    }

    // Deliberately not cancellable: a client disconnecting mid-capture must not abort recording the payment.
    private async Task PersistAsync()
    {
        var tmp = _path + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(_orders, JsonDefaults.Options), CancellationToken.None);
        File.Move(tmp, _path, overwrite: true);
    }

    private static Order? Clone(Order? o) =>
        o is null ? null : JsonSerializer.Deserialize<Order>(JsonSerializer.Serialize(o, JsonDefaults.Options), JsonDefaults.Options);

    private static string NewId() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // No 0/O/1/I/L to keep codes readable when said out loud at pickup.
    private const string CodeAlphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private string NewCode()
    {
        string code;
        do { code = "AVI-" + RandomNumberGenerator.GetString(CodeAlphabet, 5); }
        while (_orders.Any(o => o.Code == code));
        return code;
    }
}
