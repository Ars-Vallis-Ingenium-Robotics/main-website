namespace AviSite.Web.Store;

/// <summary>
/// One font a brand can pick. <see cref="Css"/> is the family name in wwwroot/css/brand-fonts.css; the Pdf files are
/// embedded resources from Pdf/Fonts. Made by tools/fonts/make_pdf_fonts.py (keep the two lists in step).
/// </summary>
public sealed record BrandFont(string Id, string Label, string Css, string Kind, int HeadingWeight, string HeadingPdf, string? RegularPdf, string? BoldPdf, string? Stretch = null)
{
    public bool ForHeadings => true;
    public bool ForBody => RegularPdf is not null;
}

public static class BrandFonts
{
    public const string DefaultHeading = "porcine";
    public const string DefaultBody = "archivo";

    public static readonly BrandFont[] All =
    [
        new("porcine", "Porcine Heavy (AVI)", "Porcine", "display", 900, "porcine-heading.ttf", null, null),
        new("archivo", "Archivo", "Archivo", "sans", 800, "archivo-heading.ttf", "archivo-regular.ttf", "archivo-bold.ttf"),
        new("archivo-expanded", "Archivo Expanded (SMS)", "Archivo", "sans", 800, "archivo-expanded-heading.ttf", null, null, "125%"),
        new("inter", "Inter", "Brand Inter", "sans", 800, "inter-heading.ttf", "inter-regular.ttf", "inter-bold.ttf"),
        new("source-sans-3", "Source Sans 3", "Brand Source Sans 3", "sans", 800, "source-sans-3-heading.ttf", "source-sans-3-regular.ttf", "source-sans-3-bold.ttf"),
        new("ibm-plex-sans", "IBM Plex Sans", "Brand IBM Plex Sans", "sans", 700, "ibm-plex-sans-bold.ttf", "ibm-plex-sans-regular.ttf", "ibm-plex-sans-bold.ttf"),
        new("montserrat", "Montserrat", "Brand Montserrat", "sans", 800, "montserrat-heading.ttf", "montserrat-regular.ttf", "montserrat-bold.ttf"),
        new("space-grotesk", "Space Grotesk", "Brand Space Grotesk", "sans", 700, "space-grotesk-bold.ttf", "space-grotesk-regular.ttf", "space-grotesk-bold.ttf"),
        new("oswald", "Oswald", "Brand Oswald", "condensed", 700, "oswald-bold.ttf", "oswald-regular.ttf", "oswald-bold.ttf"),
        new("bebas-neue", "Bebas Neue", "Brand Bebas Neue", "display", 400, "bebas-neue-heading.ttf", null, null),
        new("roboto-slab", "Roboto Slab", "Brand Roboto Slab", "serif", 800, "roboto-slab-heading.ttf", "roboto-slab-regular.ttf", "roboto-slab-bold.ttf"),
        new("lora", "Lora", "Brand Lora", "serif", 700, "lora-bold.ttf", "lora-regular.ttf", "lora-bold.ttf"),
    ];

    public static BrandFont? Find(string? id) => All.FirstOrDefault(f => f.Id == id);
    public static BrandFont Heading(string? id) => Find(id) is { ForHeadings: true } f ? f : Find(DefaultHeading)!;
    public static BrandFont Body(string? id) => Find(id) is { ForBody: true } f ? f : Find(DefaultBody)!;

    /// <summary>CSS font-family stack for a family name (falls back to the site's sans).</summary>
    public static string Stack(BrandFont f) => $"\"{f.Css}\", \"Archivo\", ui-sans-serif, system-ui, sans-serif";
}
