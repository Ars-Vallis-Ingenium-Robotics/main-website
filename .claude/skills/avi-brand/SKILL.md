---
name: avi-brand
description: AVI Robotics / Sequoia Marine Solutions brand and UI rules for avirobotics.org. Use before designing, styling or writing copy for any page, component or graphic in this repo.
---

# AVI brand skill

1. Read `/DESIGN.md` in full. It is the source of truth for tokens, type, layout and don'ts.
2. Only use colors through CSS custom properties from the `tokens` layer of
   `src/AviSite.Web/wwwroot/css/site.css`. Never hard-code a hex value in a component.
3. Reuse components in `src/AviSite.Web/Components/Shared` before writing new markup.
4. Copy voice: plain, confident, a little dry. Short sentences. Students talking, not a
   marketing department. Never invent facts (numbers, awards, sponsors, dates). Write
   `TODO(confirm): …` instead and add it to `docs/CONTENT-TODO.md`.
5. SMS pages use the `sea` accent; everything else uses `signal`.
6. After any UI change: run the app, screenshot at 375px and 1440px, look at both,
   run `node tests/e2e/smoke.mjs`.

## Brand assets (Google shared drive: Media & Marketing/Branding)

- `Logo/Dogs/`: AVI dog mark (normal, black, white, SMALL). Primary club mark.
- `Logo/SMS/`: Sequoia Marine Solutions logo (+ "w text" versions). `phase out/` = don't use.
- `Logo/Workspace/`: AVI letter banner, dog head banner, White AVI.
- `BRAND SWATCH.aco`: official palette (Photoshop swatch). Convert to hex → tokens.
- `Porcine Font.zip`: display font. Check license before web use.
- `Branding Resources` (Google Doc): read for usage rules.

TODO: once pulled in, replace the placeholder palette in DESIGN.md + site.css and
list the exact hex values here.
