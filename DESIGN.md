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

## Photos: level of detail

`tools/images/build_photos.py` turns full-size camera JPGs into 480/960/1600/2400 px WebP
variants plus a ~24 px blurred placeholder stored in `Store/photos.json`. The `<Photo>` component
paints the placeholder instantly, lets the browser choose a resolution via `srcset`, and `lod.js`
fades the sharp image in once it decodes. Always set a realistic `Sizes` value.

## Motion (scroll-driven)

All motion is CSS scroll-driven (`animation-timeline`), progressive (no effect where unsupported)
and off under `prefers-reduced-motion`. Vocabulary: `.reveal` (24 px rise on entry), `.parallax`
(photo drifts inside its frame), scroll progress bar, hero dog drift, timeline spine fill,
filmstrip scale-in, and the SMS depth gauge (≥ 88rem wide only). Don't add new effects
without a reason tied to the content.

## Sequoia Marine Solutions microsite

`/sms/*` (and the root of `sms.avirobotics.org`) uses `SmsLayout` and the `.sms-site` theme:
deep water `#06262b`, SMS Tiffany `#2cc0b6` (on dark only), teal-deep `#0f6f69` (text on light),
cream `#eee1cc`, SMS white `#f7f9f9`. Display type is Archivo at 112–125% width, not Porcine.
Every SMS page links back to AVI (nav pill + footer column) via `SmsUrls`, which emits relative
links on /sms and absolute ones on the SMS host.

## Don'ts (the "AI website" tells)

- No purple/blue gradients, glassmorphism, blobs, or emoji icons.
- No blueprint/graph-paper grids behind heroes (Robert: "a lot of AI websites are using this").
  Heroes use our own photography (toned) or plain ink with the antenna-red/blurple base stripe.
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

## Round 6 rules (Nov 2026)
- Home opens on a landing: only the club photo, "AVI" and a scroll cue. Scrolling sends AVI and the buttons up into the
  nav bar while the header slides in (scroll-driven CSS; static fallback without support or with reduced motion).
- Photo galleries use the soft-shape collage (`<Collage>`): rounded squares, circles, arches, pills, leaves. No hard
  polygon crops. Bands run edge to edge and fade out at the sides.
- Every photo not inside a link opens in the lightbox (`media.js`). Add `no-zoom` to opt out.
- No scroll-snap on horizontal strips (it stutters). No hover "lift" on things that aren't clickable (sponsor logos).
- Timelines: chapters open into their own sub-timeline (View Transitions morph); every moment opens a pop-up.
