using System.Text.Json;
using AviSite.Web.Options;
using Microsoft.Extensions.Options;

namespace AviSite.Web.Store;

public interface ICatalog
{
    IReadOnlyList<Product> All { get; }
    IEnumerable<Product> Active { get; }
    Product? Find(string slug);
    bool IsOrderable(Product product);
}

/// <summary>
/// Draft catalog: reads <c>{DataDirectory}/catalog.json</c>, falling back to the
/// seed file shipped with the app. Loaded once at startup; restart to pick up edits.
/// Replace with an EF Core-backed catalog + admin editor later.
/// </summary>
public sealed class JsonCatalog : ICatalog
{
    private readonly TimeProvider _clock;

    public JsonCatalog(IOptions<StoreOptions> options, IWebHostEnvironment env, TimeProvider clock, ILogger<JsonCatalog> log)
    {
        _clock = clock;
        var dataDir = Path.Combine(env.ContentRootPath, options.Value.DataDirectory);
        var custom = Path.Combine(dataDir, "catalog.json");
        var seed = Path.Combine(env.ContentRootPath, "Store", "catalog.seed.json");
        var path = File.Exists(custom) ? custom : seed;

        var json = File.ReadAllText(path);
        var products = JsonSerializer.Deserialize<List<Product>>(json, JsonDefaults.Options) ?? [];
        Validate(products);
        All = products;
        log.LogInformation("Loaded {Count} products from {Path}", products.Count, path);
    }

    public IReadOnlyList<Product> All { get; }
    public IEnumerable<Product> Active => All.Where(p => p.Active);

    public Product? Find(string slug) =>
        All.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public bool IsOrderable(Product product)
    {
        if (!product.Active) return false;
        if (product.OrdersClose is not { } close) return true;
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().ToPacific().DateTime);
        return today <= close;
    }

    private static void Validate(List<Product> products)
    {
        var dupes = products.GroupBy(p => p.Slug, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) throw new InvalidOperationException($"Duplicate product slugs: {string.Join(", ", dupes)}");
        string[] reserved = ["cart", "checkout", "pay", "orders"];
        foreach (var p in products)
        {
            if (reserved.Contains(p.Slug, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"Product slug '{p.Slug}' is reserved by a shop route.");
            if (p.PriceCents <= 0) throw new InvalidOperationException($"Product '{p.Slug}' must have a positive price.");
            if (p.Sizes.Count == 0) throw new InvalidOperationException($"Product '{p.Slug}' needs at least one size.");
            if (p.Sizes.Any(s => s.UpchargeCents < 0)) throw new InvalidOperationException($"Product '{p.Slug}' has a negative upcharge.");
        }
    }
}

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };
}
