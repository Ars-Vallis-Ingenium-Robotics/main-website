from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote

root = Path(__file__).resolve().parents[1]
pages = [
    "index.html",
    "about.html",
    "history.html",
    "programs.html",
    "outreach.html",
    "tools.html",
    "sponsors.html",
    "sms.html",
]

class Parser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.refs = []

    def handle_starttag(self, tag, attrs):
        attr_map = dict(attrs)
        for key in ("href", "src"):
            if key in attr_map:
                self.refs.append((tag, key, attr_map[key]))

missing = []
checked = 0
for page in pages:
    parser = Parser()
    page_path = root / page
    parser.feed(page_path.read_text(encoding="utf-8"))
    for tag, key, ref in parser.refs:
        if ref.startswith(("http://", "https://", "mailto:", "tel:", "data:", "#")):
            continue
        clean = unquote(ref.split("#", 1)[0].split("?", 1)[0])
        if not clean:
            continue
        checked += 1
        target = (page_path.parent / clean).resolve()
        if not target.exists():
            missing.append((page, tag, key, ref, str(target)))

print(f"checked_pages={len(pages)}")
print(f"checked_local_refs={checked}")
print(f"missing_refs={len(missing)}")
for item in missing:
    print("MISSING", item)
raise SystemExit(1 if missing else 0)
