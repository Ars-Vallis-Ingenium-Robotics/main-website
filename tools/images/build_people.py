"""Build square casual headshots for PersonCard from people.manifest.json.

Each entry crops `box` [x0, y0, x1, y1] out of `src` (full-size camera JPG) and writes
wwwroot/img/people/<slug>-160.webp and -320.webp. Optional "rotate" levels a tilted head. Run from the repo root:
    python3 tools/images/build_people.py
"""
import json, os
from PIL import Image, ImageOps

HERE = os.path.dirname(__file__)
OUT = os.path.join(HERE, "..", "..", "src", "AviSite.Web", "wwwroot", "img", "people")
os.makedirs(OUT, exist_ok=True)
for p in json.load(open(os.path.join(HERE, "people.manifest.json"))):
    im = ImageOps.exif_transpose(Image.open(p["src"])).convert("RGB")
    x0, y0, x1, y1 = p["box"]
    side = max(x1 - x0, y1 - y0); cx, cy = (x0 + x1) // 2, (y0 + y1) // 2
    if rot := p.get("rotate"):  # degrees counter-clockwise, to level a tilted head
        big = im.crop((cx - side, cy - side, cx + side, cy + side)).rotate(rot, resample=Image.BICUBIC)
        face = big.crop((side // 2, side // 2, side // 2 * 3, side // 2 * 3))
    else:
        face = im.crop((cx - side // 2, cy - side // 2, cx + side // 2, cy + side // 2))
    for size in (160, 320):
        face.resize((size, size), Image.LANCZOS).save(os.path.join(OUT, f"{p['slug']}-{size}.webp"), quality=82, method=6)
    print(p["slug"])
