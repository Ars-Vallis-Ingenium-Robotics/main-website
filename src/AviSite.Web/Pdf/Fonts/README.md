# PDF report fonts

Static TrueType copies of the fonts a brand can pick on `/admin/branding`, compiled into the app for the PDF writer
(the web report uses the WOFF2 files in `wwwroot/fonts`). Generated from `tools/fonts/fonts.json` by `tools/fonts/make_pdf_fonts.py`; don't edit by hand.

- `porcine-heading.ttf`: Porcine Heavy (AVI's display face). See `wwwroot/fonts/FONTS.md` for its license status.
- `archivo-*.ttf`, `jetbrains-mono-*.ttf`: SIL Open Font License 1.1 (instances of the site's variable fonts).
- Everything else: from Fontsource, SIL Open Font License 1.1; each family's license is next to it (`*-LICENSE.txt`).
