# Hours reports and branding

## Reports (`/admin/hours/report`)

Leads, officers and the owner. Pick a team and a range; two versions:

- **Internal** (default): totals, hours over time, categories, teams, each person's hours and the person × category table. CSV download too.
- **For sponsors** (`view=sponsor`): the same totals and charts with no names or per-person numbers. Use it for anything that leaves the team (sponsor updates, grant reports).

**Download PDF** (`/admin/hours/report.pdf?team=…&range=…&view=…`) builds a real US Letter PDF on the server from the same data as the page (`HoursReportData`), so the page and PDF always agree. The writer (`src/AviSite.Web/Pdf/`) has no dependencies: the brand's fonts embedded (TrueType, with copyable text), vector charts, PNG/JPEG logos. Page numbers and the brand footer on every page. The browser's Print button still works for a quick paper copy.

## Branding (`/admin/branding`)

What a report carries: logo (PNG or JPEG, ≤ 1 MB, ≤ 4000 px; checked by content), name, tagline, main color, chart color, heading font, text font, footer.

Fonts come from a list of 45, grouped in the pickers (AVI & SMS, sans serif, serif, slab, display, condensed, tech, monospace, handwritten); display-only faces are offered for headings only. The preview follows the pickers before you save (`wwwroot/js/branding.js`). The list lives in `tools/fonts/fonts.json`; to add a font, add it there and run `tools/fonts/make_pdf_fonts.py`, which rebuilds the web files, the PDF fonts, `brand-fonts.css` and `Store/BrandFonts.Catalog.g.cs`.

- Every **organization** (top-level group: AVI Robotics, CCC Engineering Renaissance, …) has its own branding. Unset fields fall back to the group name and AVI's ink/blurple.
- An organization's **leads** (and the owner) can switch on **own branding** for any team under them (e.g. SMS under AVI, Crush Depth under ER). That team's leads can then edit it.
- Every other team uses the nearest branded group above it (Electrical under SMS → SMS's).
- Colors are checked against white: main color ≥ 4.5:1 (it's text), chart color ≥ 3:1. In dark mode the page uses the same hues lifted until they read on the dark background (near-black brands use the dark theme's text color); light mode, print and the PDF use the colors as chosen. Logos sit on a white chip, as they do in the PDF.
- Logos are served at `/branding/{group}/logo?v=…` to signed-in users only, cached until the next upload.

Tables: `group_branding` (SiteDb migration 6).
