# Editing the site's words

Every word on avirobotics.org (and the SMS microsite) lives in this folder as Markdown.
Open a file, change the text, save. Running locally (`dotnet run`), just refresh the page:
no rebuild, no restart. On the server, edits ship with the next deploy.

```
content/
  site.md              nav, footer, shared {tokens} and named links
  roster.md            AVI founders, advisors, mentors, officers, leads
  sponsors.md          sponsor logos and their tiers
  pages/*.md           one file per AVI page (home, about, projects, …)
  sms/*.md             the SMS microsite pages (company.md also holds leaders and crews)
  timelines/*.md       the History and SMS 2026 timelines
  people/*.md          optional member profiles for the name pop-ups (see people/_template.md)
```

## The rules (that's all of them)

```markdown
---
title: Shown in the browser tab
description: Shown by search engines
---

## section-key
The Razor page asks for this section by its key. Don't rename keys; change the text under them.

## another-key
photo: worlds-award-stage
Lines right under a heading that look like `word: value` are settings (photo, date, role…).
Then the text, as normal Markdown.

### A record inside a section
Some sections hold a list of records (timeline moments, crews, leaders). Each is a ### heading
with its own settings and text. Add one by copying another; reorder by moving the block.
```

- Paragraphs: leave a blank line between them.
- Lists: start lines with `- `. In lists of people or stats, `|` separates the columns.
- **bold**, *italic*, `[link text](where)`.
- Where a link can go:
  - `https://…` opens in a new tab
  - `/join`, `/about`, `#section` are pages and spots on this site
  - `sms:/2026` and `avi:/join` link across the AVI site and the SMS microsite (they pick the
    right address, whether SMS is at avirobotics.org/sms or sms.avirobotics.org)
  - a name from `site.md` → `## links` (e.g. `mate`, `crush-depth`) or a configured one:
    `sponsor-packet`, `discord`, `youtube`, `linktree`, `instagram`, `contact`
- Tokens in braces are filled in for you: `{ein}` (EIN and number, never split), `{status}`
  (501(c)(3)), `{legal-name}`, `{effective-date}`, `{meeting-time}`, `{meeting-room}`, plus
  anything you add to `site.md` → `## tokens`.
- `<!-- notes like this -->` on their own lines are for editors and never show on the site.
- Photo names (`photo: …`, collage lists) are keys from `tools/images/photos.manifest.json`.

If you break something, the page still loads. Locally you'll see a red-ish `⟦missing …⟧`
marker where a section is missing; on the live site that spot is just empty.

## Member profiles

Anyone named on the site gets a pop-up when their name is clicked: their photo and roles.
To add contact info (LinkedIn, email, portfolio…), copy `people/_template.md` to
`people/<first-last>.md` and fill in only what the member asked to share. Blank = hidden.
