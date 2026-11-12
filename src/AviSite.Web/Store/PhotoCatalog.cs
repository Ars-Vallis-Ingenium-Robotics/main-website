using System.Text.Json;

namespace AviSite.Web.Store;

public sealed record PhotoInfo(string Alt, int[] Widths, int W, int H, string Focus, string Lqip);

/// <summary>
/// Reads Store/photos.json, produced by tools/images/build_photos.py.
/// Each photo has several widths (the "levels of detail") plus a tiny blurred placeholder.
/// </summary>
public sealed class PhotoCatalog
{
    private readonly Dictionary<string, PhotoInfo> _photos;

    public PhotoCatalog(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "Store", "photos.json");
        _photos = File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<string, PhotoInfo>>(File.ReadAllText(path), JsonDefaults.Options) ?? []
            : [];
    }

    public PhotoInfo? Get(string key) => _photos.GetValueOrDefault(key);
}
