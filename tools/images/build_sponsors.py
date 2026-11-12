"""Cut individual sponsor logos out of the 2026 shirt-back artwork.

Source: Media & Marketing/.../Sponsor Highlights/final sponsorship shirt back.png
(white logos on transparent, 3300x4181). Boxes are in a 1105px-wide preview's
coordinates; each crop is trimmed to its alpha bounds and saved as WebP with alpha.
    python3 tools/images/build_sponsors.py "<path to png>"
"""
import sys, os
from PIL import Image

BOXES = {
    "precision": (285, 230, 805, 380), "fresno-state": (325, 385, 780, 580), "mesa": (355, 585, 750, 780),
    "ieee": (225, 785, 505, 870), "schmidt-ocean": (510, 785, 880, 870), "mcmillan": (225, 885, 495, 978),
    "lyles": (505, 885, 648, 1000), "mate": (655, 885, 885, 1000), "morning-star": (262, 1003, 562, 1091),
    "longroad": (568, 1008, 842, 1087), "harris": (325, 1093, 467, 1187), "sigler": (470, 1108, 636, 1187),
    "aqua-sports": (638, 1093, 782, 1187), "ac-electrical": (312, 1186, 417, 1287), "del-monte": (418, 1193, 692, 1283),
    "roll-em-up": (690, 1183, 792, 1287), "national-air": (368, 1288, 732, 1321), "glencass": (412, 1325, 698, 1358),
    "fowler": (416, 1359, 684, 1398),
}
src = Image.open(sys.argv[1]).convert("RGBA")
k = src.width / 1105
out = os.path.join(os.path.dirname(__file__), "..", "..", "src", "AviSite.Web", "wwwroot", "img", "sponsors")
os.makedirs(out, exist_ok=True)
for name, (x0, y0, x1, y1) in BOXES.items():
    c = src.crop((int(x0 * k), int(y0 * k), int(x1 * k), int(y1 * k)))
    a = c.split()[3].point(lambda v: 255 if v > 24 else 0)
    c = c.crop(a.getbbox())
    if c.height > 360: c = c.resize((int(c.width * 360 / c.height), 360), Image.LANCZOS)
    if c.width > 900: c = c.resize((900, int(c.height * 900 / c.width)), Image.LANCZOS)
    c.save(os.path.join(out, f"{name}.webp"), quality=90, method=6)
    print(name, c.size)
