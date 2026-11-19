# Hours reports and branding

## Reports (`/admin/hours/report`)

Leads, officers and the owner. Pick a team and a range; two versions:

- **Internal** (default): totals, hours over time, categories, teams, each person's hours and the person × category table. CSV download too.
- **For sponsors** (`view=sponsor`): the same totals and charts with no names or per-person numbers. Use it for anything that leaves the team (sponsor updates, grant reports).

**Download PDF** (`/admin/hours/report.pdf?team=…&range=…&view=…`) builds a real US Letter PDF on the server from the same data as the page (`HoursReportData`), so the page and PDF always agree. The writer (`src/AviSite.Web/Pdf/`) has no dependencies: standard Helvetica (not embedded), vector charts, PNG/JPEG logos. Page numbers and the brand footer on every page. The browser's Print button still works for a quick paper copy.

## Branding (`/admin/branding`)

What a report carries: logo (PNG or JPEG, ≤ 1 MB, ≤ 4000 px; checked by content), name, tagline, main color, chart color, footer.

- Every **organization** (top-level group: AVI Robotics, CCC Engineering Renaissance, …) has its own branding. Unset fields fall back to the group name and AVI's ink/blurple.
- An organization's **leads** (and the owner) can switch on **own branding** for any team under them (e.g. SMS under AVI, Crush Depth under ER). That team's leads can then edit it.
- Every other team uses the nearest branded group above it (Electrical under SMS → SMS's).
- Colors are checked against white: main color ≥ 4.5:1 (it's text), chart color ≥ 3:1. On screen the brand colors apply in light mode and print; dark mode keeps the site's own colors. Logos sit on a white chip, as they do in the PDF.
- Logos are served at `/branding/{group}/logo?v=…` to signed-in users only, cached until the next upload.

Tables: `group_branding` (SiteDb migration 6).
