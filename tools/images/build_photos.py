"""
Level-of-detail photo pipeline (run when adding/replacing photos).

For each source photo it writes, into src/AviSite.Web/wwwroot/img/photos/:
  <key>-480.webp, -960.webp, -1600.webp, -2400.webp   (only widths <= source)
and records into src/AviSite.Web/Store/photos.json:
  width/height (of the largest variant), focal point, alt, and a ~24px blurred
  placeholder (LQIP) as a data: URI that is painted instantly while the real
  image streams in (the "level of detail" swap).

Usage:  python3 tools/images/build_photos.py tools/images/photos.manifest.json
Manifest entries: {"key", "src" (absolute path), "alt", "crop": [l,t,r,b] fractions (optional), "focus": "50% 40%" (optional)}
"""
import json, sys, os, base64, io
from PIL import Image, ImageOps, ImageFilter
Image.MAX_IMAGE_PIXELS = None
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "src/AviSite.Web/wwwroot/img/photos")
DB = os.path.join(ROOT, "src/AviSite.Web/Store/photos.json")
WIDTHS = [480, 960, 1600, 2400]

def main(manifest):
    os.makedirs(OUT, exist_ok=True)
    db = json.load(open(DB)) if os.path.exists(DB) else {}
    for e in json.load(open(manifest)):
        im = ImageOps.exif_transpose(Image.open(e["src"])).convert("RGB")
        if c := e.get("crop"):
            w, h = im.size
            im = im.crop((int(c[0]*w), int(c[1]*h), int(c[2]*w), int(c[3]*h)))
        made = []
        for w in WIDTHS:
            if w > im.width and made: break
            v = im.copy(); v.thumbnail((w, 10**6), Image.LANCZOS)
            v.save(f"{OUT}/{e['key']}-{w}.webp", "WEBP", quality=78, method=6)
            made.append(w); last = v.size
        t = im.copy(); t.thumbnail((24, 24)); t = t.filter(ImageFilter.GaussianBlur(0.6))
        buf = io.BytesIO(); t.save(buf, "WEBP", quality=40)
        db[e["key"]] = {
            "alt": e["alt"], "widths": made, "w": last[0], "h": last[1],
            "focus": e.get("focus", "50% 50%"),
            "lqip": "data:image/webp;base64," + base64.b64encode(buf.getvalue()).decode(),
        }
        print(e["key"], made, last)
    json.dump(db, open(DB, "w"), indent=1)

if __name__ == "__main__":
    main(sys.argv[1])
