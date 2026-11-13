using System.Collections.Concurrent;
using AviSite.Web.Content.Md;

namespace AviSite.Web.Content;

/// <summary>
/// Loads the site's copy from the content/ folder (Markdown, see content/README.md).
/// Files are re-read when they change on disk, so in Development an edit shows up on the next
/// page refresh, no restart. Missing files or keys never crash a page: Development shows a
/// visible "⟦missing …⟧" marker, Production renders nothing and logs a warning.
/// </summary>
public sealed class SiteContent
{
    private readonly ConcurrentDictionary<string, (DateTime Stamp, MdDocument Doc)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SiteContent> _log;

    public SiteContent(IWebHostEnvironment env, IConfiguration config, ILogger<SiteContent> log)
    {
        _log = log;
        IsDevelopment = env.IsDevelopment();
        Root = ResolveRoot(env.ContentRootPath, config["Content:Path"]);
        log.LogInformation("Site copy from {Root}", Root);
    }

    public string Root { get; }
    public bool IsDevelopment { get; }

    private static string ResolveRoot(string contentRoot, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(Path.Combine(contentRoot, configured));
        // Published: content/ sits next to the app. Running from the repo: it's at the repo root.
        // Check for site.md, not just the folder: on Windows (case-insensitive) the C# "Content"
        // folder in the project would otherwise match "content".
        foreach (var candidate in new[] { Path.Combine(contentRoot, "content"), Path.Combine(contentRoot, "..", "..", "content") })
            if (File.Exists(Path.Combine(candidate, "site.md"))) return Path.GetFullPath(candidate);
        return Path.GetFullPath(Path.Combine(contentRoot, "content"));
    }

    /// <summary>A content file by path without ".md", e.g. "pages/home". Empty document when missing.</summary>
    public MdDocument Doc(string name)
    {
        var file = Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar) + ".md");
        DateTime stamp;
        try { stamp = File.GetLastWriteTimeUtc(file); }
        catch { stamp = DateTime.MinValue; }
        if (_cache.TryGetValue(name, out var hit) && hit.Stamp == stamp) return hit.Doc;

        MdDocument doc;
        if (File.Exists(file))
        {
            try { doc = MdDocument.Parse(name, File.ReadAllText(file)); }
            catch (IOException) when (hit.Doc is not null) { return hit.Doc; } // mid-save in an editor: keep the last good copy
        }
        else
        {
            _log.LogWarning("Content file missing: {File}", file);
            doc = MdDocument.Parse(name, "");
        }
        _cache[name] = (stamp, doc);
        return doc;
    }

    /// <summary>Names (without .md) of the files in a content folder, e.g. "people" → ["people/robert-voss", …].</summary>
    public IReadOnlyList<string> Files(string folder)
    {
        var dir = Path.Combine(Root, folder);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.md")
            .Select(f => $"{folder}/{Path.GetFileNameWithoutExtension(f)}")
            .Where(n => !n.EndsWith("/_template", StringComparison.OrdinalIgnoreCase) && !n.EndsWith("/README", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Missing(string what)
    {
        _log.LogWarning("Missing content: {What}", what);
    }
}
