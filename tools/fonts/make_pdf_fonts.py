"""Fonts for report branding (the choices on /admin/branding).

For each font: WOFF2 files for the web report (wwwroot/fonts/brand) and static TrueType files for the
server-side PDF writer (src/AviSite.Web/Pdf/Fonts), which can't use WOFF2 or variable fonts.
All fonts are SIL OFL 1.1 except Porcine Heavy (AVI's display face, see wwwroot/fonts/FONTS.md).
The list here must match BrandFonts.All in src/AviSite.Web/Store/BrandFonts.cs.

    pip install fonttools brotli
    mkdir /tmp/fontpk && cd /tmp/fontpk
    for p in inter montserrat oswald bebas-neue roboto-slab lora space-grotesk ibm-plex-sans source-sans-3; do
      npm pack @fontsource/$p && mkdir -p $p && tar xzf fontsource-$p-*.tgz -C $p; done
    python tools/fonts/make_pdf_fonts.py /tmp/fontpk
"""
import shutil
import sys
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer

root = Path(__file__).resolve().parents[2]
web_fonts = root / "src/AviSite.Web/wwwroot/fonts"
web_out = web_fonts / "brand"
pdf_out = root / "src/AviSite.Web/Pdf/Fonts"
web_out.mkdir(parents=True, exist_ok=True)
pdf_out.mkdir(parents=True, exist_ok=True)
packages = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("/tmp/fontpk")

def ttf(src, dest, axes=None):
    f = TTFont(src)
    if axes:
        f = instancer.instantiateVariableFont(f, axes, updateFontNames=False)
    f.flavor = None
    f.save(dest)

# Site fonts (already in wwwroot/fonts).
ttf(web_fonts / "porcine-heavy.woff2", pdf_out / "porcine-heading.ttf")
ttf(web_fonts / "archivo-var.woff2", pdf_out / "archivo-regular.ttf", {"wght": 400, "wdth": 100})
ttf(web_fonts / "archivo-var.woff2", pdf_out / "archivo-bold.ttf", {"wght": 700, "wdth": 100})
ttf(web_fonts / "archivo-var.woff2", pdf_out / "archivo-heading.ttf", {"wght": 800, "wdth": 100})
ttf(web_fonts / "archivo-var.woff2", pdf_out / "archivo-expanded-heading.ttf", {"wght": 800, "wdth": 125})

# Fontsource packages: id → (package folder, regular, bold, heading weight)
fontsource = {
    "inter": ("inter", 400, 700, 800),
    "montserrat": ("montserrat", 400, 700, 800),
    "oswald": ("oswald", 400, 700, 700),
    "bebas-neue": ("bebas-neue", None, None, 400),
    "roboto-slab": ("roboto-slab", 400, 700, 800),
    "lora": ("lora", 400, 700, 700),
    "space-grotesk": ("space-grotesk", 400, 700, 700),
    "ibm-plex-sans": ("ibm-plex-sans", 400, 700, 700),
    "source-sans-3": ("source-sans-3", 400, 700, 800),
}
for fid, (pkg, regular, bold, heading) in fontsource.items():
    files = next((packages / pkg).rglob("files"))
    def src(w):
        return next(files.glob(f"*-latin-{w}-normal.woff2"))
    for role, w in (("regular", regular), ("bold", bold), ("heading", heading)):
        if w is None or (role == "heading" and w == bold):
            continue  # a heading in the bold weight uses the bold file
        shutil.copyfile(src(w), web_out / f"{fid}-{w}.woff2")
        ttf(src(w), pdf_out / f"{fid}-{role}.ttf")
    lic = next((packages / pkg).rglob("LICENSE"), None)
    if lic:
        shutil.copyfile(lic, pdf_out / f"{fid}-LICENSE.txt")

for f in sorted(pdf_out.glob("*.ttf")):
    print(f.name, f.stat().st_size)
