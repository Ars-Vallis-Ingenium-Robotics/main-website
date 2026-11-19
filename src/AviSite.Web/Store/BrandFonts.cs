namespace AviSite.Web.Store;

/// <summary>
/// One font a brand can pick. <see cref="Css"/> is its CSS family (site.css or brand-fonts.css); the Pdf files are
/// embedded resources from Pdf/Fonts. The list itself (<see cref="BrandFonts.All"/>) is generated from tools/fonts/fonts.json.
/// </summary>
public sealed record BrandFont(string Id, string Label, string Css, string Group, int HeadingWeight, string HeadingPdf,
    string? RegularPdf, string? BoldPdf, string? Stretch = null, int TextWeight = 400)
{
    public bool ForHeadings => true;
    public bool ForBody => RegularPdf is not null && BoldPdf is not null;
}

public static partial class BrandFonts
{
    public const string DefaultHeading = "porcine";
    public const string DefaultBody = "archivo";

    public static BrandFont? Find(string? id) => All.FirstOrDefault(f => f.Id == id);
    public static BrandFont Heading(string? id) => Find(id) is { ForHeadings: true } f ? f : Find(DefaultHeading)!;
    public static BrandFont Body(string? id) => Find(id) is { ForBody: true } f ? f : Find(DefaultBody)!;

    /// <summary>Fonts for one picker, grouped for &lt;optgroup&gt;s.</summary>
    public static IEnumerable<(string Group, List<BrandFont> Fonts)> Grouped(bool body) =>
        Groups.Select(g => (g, All.Where(f => f.Group == g && (!body || f.ForBody)).ToList())).Where(x => x.Item2.Count > 0);

    /// <summary>CSS font-family stack (falls back to the site's sans).</summary>
    public static string Stack(BrandFont f) => $"\"{f.Css}\", \"Archivo\", ui-sans-serif, system-ui, sans-serif";
}
