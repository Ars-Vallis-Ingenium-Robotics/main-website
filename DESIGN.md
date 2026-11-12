# AVI Robotics: design system

Read this before touching UI. AI agents: this file is loaded by the `avi-brand` skill.

> **Status:** palette and logo are **placeholders**. The real brand kit lives in
> `Media & Marketing/Branding` (BRAND SWATCH.aco, Porcine font, AVI dog + SMS logos).
> When it's pulled in, change the `tokens` layer in `wwwroot/css/site.css` and this file.
> Nothing else should need to change.

## Idea

**An engineering club's drafting table.** Deep-water ink, drafting-paper off-white,
one hi-vis amber for anything you're meant to press. Technical-drawing details
(crop marks, title-block spec sheets) used **sparingly**. They're seasoning, not the meal.

## Tokens (`site.css` → `@layer tokens`)

| Token | Value | Use |
|---|---|---|
| `--ink` | `#0b1220` | Dark surfaces, primary text, borders that matter |
| `--paper` / `--paper-2` | `#f3f4f1` / `#e7e9e4` | Page background / alternate band |
| `--signal` | `#ffb000` | Primary actions, highlights **on dark**, the one accent |
| `--sea` / `--sea-deep` | `#19b3a4` / `#0f766e` | SMS team accent. `--sea` on dark only; `--sea-deep` on light |
| `--text-muted` | `#4b5567` | Secondary text on paper (6.8:1) |
| `--on-ink-muted` | `#a3aec3` | Secondary text on ink (8.4:1) |
| `--line-input` | ink @ 55% | Input/chip borders (≥3:1) |

**Contrast rules (checked):** amber is never text on paper (1.7:1). Sea is never text
on paper (2.4:1). The focus ring is ink on light surfaces and amber on dark ones.

## Type

- **Archivo** (variable, self-hosted, `wdth` 62–125). Display text uses
  `font-stretch: 118–125%` at weight 800–850, tight tracking. Body uses normal width.
- **JetBrains Mono** for labels, prices, codes, eyebrow text. Uppercase,
  tracking ≤ 0.08em, never body copy.
- Scale: `--step--1 … --step-5` (fluid `clamp`). One h1 per page.
- When the brand kit arrives: decide where **Porcine** fits (probably the wordmark /
  hero display only) and check its license allows web embedding.

## Layout

- Asymmetric `split` (5/7 columns): sticky label column + content column.
- Section rhythm: `--section` vertical padding; alternate paper → ink → paper.
- Square-ish corners (`--radius: 2px`). Hard offset shadow on hover for buttons, no blur shadows.
- Mobile first; tap targets ≥ 44px; no horizontal scroll at 375px (tested).

## Components

`Eyebrow`, `PageHero`, `Figure` (crop marks + caption, honest "photo needed" slot),
`SpecSheet` (title-block `<dl>`), `ProductCard`, `OrderSummary`, `Price`, buttons
(`btn--signal | ink | sea | ghost | ghost-on-ink`), `link-arrow`, `tag`, `alert`, `note`.

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
