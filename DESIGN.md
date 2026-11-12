# AVI Robotics: design system

Read this before touching UI. AI agents: this file is loaded by the `avi-brand` skill.

> **Status:** official brand applied (Nov 2026) from `Media & Marketing/Branding`:
> `BRAND SWATCH.aco`, the "Branding Resources" doc, the AVI dog / AVI letters / SMS logos,
> and Porcine Heavy. Brand values live only in the `tokens` layer of `wwwroot/css/site.css`.

## Idea

**The AVI robot dog's workshop.** AVI Black surfaces, cool SMS-white paper, one strong
AVI Blurple for anything you're meant to press. The dog mascot and the Porcine Heavy
display face carry the personality; technical-drawing details (crop marks, title-block
spec sheets) are used sparingly. SMS pages switch the accent to Tiffany blue + cream.

## Tokens (`site.css` → `@layer tokens`)

| Token | Value | Source | Use |
|---|---|---|---|
| `--ink` | `#101011` | AVI BLACK (swatch) | Dark surfaces, primary text |
| `--paper` / `--paper-2` | `#f7f9f9` / `#eceff1` | SMS WHITE (swatch) | Page background / alt band |
| `--accent` | `#0c63e6` | AVI BLURPLE | Buttons, links, the support band. White text on it 5.3:1 |
| `--accent-on-ink` | `#5a95f2` | derived | Blurple **text/focus on ink** (6.3:1). Raw blurple on ink is 3.6:1 |
| `--slate` | `#364964` | dog dark grey | Strong secondary on light |
| `--steel` | `#6c7283` | dog light grey | Decorative / large text only |
| `--antenna` | `#911b1b` | dog antenna red | Tiny details only |
| `--sms` | `#2cc0b6` | SMS TIFF BLUE | SMS accent **on ink only** (8.5:1; 2.1:1 on paper) |
| `--sms-deep` | `#0f6f69` | derived | SMS teal as text on paper/cream |
| `--sms-cream` | `#eee1cc` | SMS CREAM | SMS logo disc, SMS product backgrounds |

Note: the Branding Resources doc lists SMS White as `#ededed` and Blurple as `#0d63e5`;
the swatch file says `#f7f9f9` / `#0c63e6`. The site uses the swatch values.

**Contrast rules (checked):** blurple is never body text on ink (use `--accent-on-ink`).
Tiffany blue is never text on paper (use `--sms-deep`). Focus ring is blurple on light,
`--accent-on-ink` on dark, white on the blurple band.

## Type

- **Porcine Heavy** (`--font-display`, self-hosted woff2): headings, wordmark, product
  names. One weight; lowercase renders as small caps. Never body text, labels or buttons.
  Font kit says "License: none (public domain)", by Unbranded Design Studio. Confirm.
- **Archivo** (variable, self-hosted) for body and UI. Stand-in for **Acumin**, the brand's
  Adobe body face, which isn't licensed for web use without an Adobe Fonts kit.
- **JetBrains Mono** for labels, prices, codes. Uppercase, tracking ≤ 0.08em.
- Scale: `--step--1 … --step-5` (fluid `clamp`). One h1 per page.

## Layout

- Asymmetric `split` (5/7 columns): sticky label column + content column.
- Section rhythm: `--section` vertical padding; alternate paper → ink → paper.
- Square-ish corners (`--radius: 2px`). Hard offset shadow on hover for buttons, no blur shadows.
- Mobile first; tap targets ≥ 44px; no horizontal scroll at 375px (tested).

## Components

`Eyebrow`, `PageHero`, `Figure` (crop marks + caption, honest "photo needed" slot),
`SpecSheet` (title-block `<dl>`), `ProductCard`, `OrderSummary`, `Price`, buttons
(`btn--accent | ink | sms | ghost | ghost-on-ink`), `link-arrow`, `tag`, `alert`, `note`.

## Logos (`wwwroot/img/brand/`, generated from the 12800px masters)

- `avi-dog.webp` (hero), `avi-dog-sm.webp` (header), `avi-dog-white.webp`
- `avi-letters-white|black.webp`: the hand-drawn AVI letter mark
- `sms-logo.webp` (cream disc), `sms-logo-white.webp`
- `favicon.ico`, `icon-32/180/192/512.png`, `/site.webmanifest`
- Don't use anything from `Logo/SMS/phase out/`.

## Don'ts (the "AI website" tells)

- No purple/blue gradients, glassmorphism, blobs, or emoji icons.
- No centered hero + three identical feature cards.
- Don't stack every drawing device at once (grid + crop marks + FIG numbers + ticker
  + numbered eyebrows). Pick two per page.
- No invented facts: stats, awards, sponsors, years. Mark unknowns `TODO(confirm)`.
- No stock photos of "diverse team smiling at laptop". Real club photos or nothing.

## Checklist before shipping UI

1. `node tests/e2e/smoke.mjs` passes.
2. Screenshot at 375px and 1440px and look at them.
3. Keyboard: tab through, focus always visible, Escape closes the menu.
4. Run `/impeccable critique` and `npx impeccable detect` (once Impeccable is installed).
