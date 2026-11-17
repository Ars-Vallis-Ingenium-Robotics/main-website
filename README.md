# avirobotics.org

Public site + shirt store for **AVI Robotics** (Fresno State) and **Sequoia Marine Solutions**.
ASP.NET Core 10, Blazor static SSR, PayPal Orders v2. Same stack as SecWeb (the member
portal), separate app, separate database.

- Plan & architecture: [`docs/PLAN.md`](docs/PLAN.md)
- Design system: [`DESIGN.md`](DESIGN.md) (AI agents: `.claude/skills/avi-brand`)
- What needs a human: [`docs/CONTENT-TODO.md`](docs/CONTENT-TODO.md)

## Run locally

```bash
dotnet run --project src/AviSite.Web        # http://localhost:5002, Development
```

Development uses a **fake PayPal** (click "Simulate PayPal payment"). Admin dashboard:
`/admin`, dev password `avi-dev-password` (Development only; never published).

## Editing the words

All copy lives in Markdown under [`content/`](content/README.md): one file per page, plus the roster,
sponsors, timelines and member profiles. Edit with any editor and refresh: running locally, changes show
without a restart. Product prices and sizes stay in `src/AviSite.Web/Store/catalog.seed.json`, and form
labels and errors stay in the Razor pages.

## SMS microsite

Served at `/sms` and, by host, at the root of `sms.avirobotics.org` (`SmsSite:Host`). Test the host
locally at `http://sms.localhost:5002`. nginx needs a `server_name sms.avirobotics.org` block proxying
to the same app.

## Photos

```bash
pip install pillow
python3 tools/images/build_photos.py tools/images/photos.manifest.json
```
Add an entry (key, absolute source path, alt text, optional crop/focus) to the manifest, rerun,
then use `<Photo Key="..." Sizes="..." />`. Only the generated WebP files are committed.

Headshots: `python3 tools/images/build_people.py` crops casual headshots from group photos
(`tools/images/people.manifest.json`: slug + source + face box). `PersonCard` picks up
`wwwroot/img/people/<slug>-160.webp` automatically and falls back to initials.

Sponsor logos: `python3 tools/images/build_sponsors.py "<final sponsorship shirt back.png>"`
cuts each logo out of the Worlds shirt artwork. Tiers live in `ClubFacts.Sponsors2026`.

Donations: `/donate` reuses the order + PayPal pipeline with a single "donation" line
(PayPal item category `DONATION`) and shows a receipt with the 501(c)(3) acknowledgment.

## Tests

```bash
cd tests/e2e && npm install --no-save playwright@1 && npx playwright install chromium
BASE_URL=http://localhost:5002 node smoke.mjs
```

Covers every route, 404s, security headers, CSRF rejection, the full buy flow,
admin pickup + CSV, phone layout, and the mobile menu.

## Configuration

| Key | Where | Notes |
|---|---|---|
| `PayPal:Mode` | env | `Fake` (dev only), `Sandbox`, `Live`, `Off` (no online payments; donate page shows offline giving). Outside Development, Sandbox needs `PayPal:AllowSandboxOutsideDevelopment` |
| `PayPal:ClientId` / `ClientSecret` / `WebhookId` | env / user-secrets | From developer.paypal.com → Apps & Credentials |
| `Admin:Owners:<n>` | env, e.g. `Admin__Owners__0=you@avirobotics.org` | Google accounts that are always owners (manage people at `/admin/people`). Only config makes owners. Dev: `owner@example.com` (dev picker on `/admin/login`) |
| `Admin:Google:ClientId` / `ClientSecret` | env | "Sign in with Google". Google Cloud → APIs & Services → Credentials → OAuth client ID (Web). Redirect URIs: `https://<host>/admin/google/callback` for each host (and `http://localhost:5002/...` for local) |
| `Admin:PasswordHash` | env | Legacy shared officer password (kept as a backup while people move to accounts). `dotnet run --project src/AviSite.Web -- hash-password` |
| `Admin:Teams:<team>` | env, e.g. `Admin__Teams__sms=pbkdf2-sha256$…` | Legacy shared team password (same hash tool). Opens only that team's QR codes, named `<team>-…`. Prefer accounts. Dev: `sms-dev-password` |
| `Site:*` | appsettings / env | Discord, Instagram, email, portal URL |
| `Store:OrdersOpen` | appsettings / env | Products visible, checkout closed |
| `Store:ComingSoon` | appsettings / env | Between drops: /shop shows "next drop" (content/pages/shop.md › coming-soon); product/cart/checkout redirect to /shop |
| `Site:NoIndex` | appsettings | Testing only: `X-Robots-Tag: noindex` |
| `DynamicQr:GoogleScriptUrl` / `SendToGoogleSheet` | appsettings | Scan rows for the old Google Sheet. Only production sends (`appsettings.Production.json`) |

PayPal webhook URL to register: `https://avirobotics.org/api/paypal/webhook`
(events: `PAYMENT.CAPTURE.COMPLETED`, `.DENIED`, `.REFUNDED`, `.REVERSED`).

## Tools

- `/tools/qr`: public QR code maker (all in the browser; `wwwroot/js/qr-tool.js` + `wwwroot/lib/qrcode-generator.js`, MIT).
  PDF export lazy-loads `wwwroot/lib/jspdf/` (jsPDF + svg2pdf, MIT). Looks are stored in `localStorage` only when the visitor saves or ticks "Remember".
- `/dynamicqr/{name}`: printed dynamic codes. Redirect + scan count (`App_Data/dynamic-qr.json`) + Google Sheet row in prod.
  Officers change targets and add codes at `/admin/qr` (same admin password as orders); team leads see only their team's codes.
  Each code's page shows scans by day, weekday, hour (Pacific) and device type. `one`–`four` are seeded from the old site.
- `/countdown`: events managed by officers at `/admin/countdowns` (`App_Data/countdowns.json`).
- Usage: tools pages count opens (server side) and the QR buttons send `navigator.sendBeacon('/api/use/qr.<button>')`.
  Only the counter name and Pacific day are stored (`App_Data/tool-usage.json`); officers see `/admin/usage`.
  New counters must be added to `ToolUsageEvents.All` (anything else is refused) and mentioned on `/privacy`.
- Link previews: `Components/Shared/SocialMeta.razor` picks `wwwroot/img/og/og-{avi,sms,tools}.jpg` (1200×630) by section.

## Admin accounts

`/admin/people` (Store/Accounts.cs, `App_Data/accounts.json`).

- **Teams** form a tree: organizations at the top (AVI Robotics, partner clubs), teams and sub-teams under them
  (AVI Robotics → Sequoia Marine Solutions; Engineering Renaissance → Crush Depth → Electrical). A team's id starts its
  QR code names and is unique across the tree. Only owners add or remove teams; a team can be removed once it has
  no people, sub-teams or QR codes.
- **People** sign in with Google and can be on several teams, each as **Member** or **Lead**. A lead manages the people
  and QR codes of their team and everything under it. **Officers** (site-wide) get the shop, countdowns, usage and
  every team. **Owners** come only from `Admin:Owners` and manage everyone.
- Add one person or a whole roster at once: one email per line, optional name after a comma.
- `Access.For(account, tree)` is the single place that answers "what can this person do"; reuse it in new admin tools.
- Every admin request re-checks the account (`AccountAuth.ValidateAsync`): removals and changes apply on the next click.

## Hours

`/hours` (→ `/admin/hours`): members clock in and out (two taps each: a button that opens a confirm, then the confirm),
add time by hand, and edit or delete their own entries. The team and category they used last are already picked;
`/hours?team=sms&category=Build` pre-picks them (print it as a QR code for a meeting). Accounts stay signed in for
30 days on a device so this stays two taps.

- **Leads** (`/admin/hours/team`): everyone on their teams, who's clocked in now, fix anyone's time, end a forgotten
  session, add time for someone, CSV. Sessions running over 24 hours show as a warning at the top of every admin page,
  with an email link and "set the end time". Clocking out of one caps it at 24 hours.
- **Report** (`/admin/hours/report`): totals, hours per week (or month), by category, team and person, a person ×
  category table; print or save as PDF. Charts are server-rendered SVG (`ColumnChart`, `HBars`), no chart library.
- **Settings** (`/admin/hours/settings`, each organization's leads and the owner): leaderboard on/off, season start
  (default Aug 1), categories.
- Data: `hours`, `hours_categories`, `hours_settings` in `site.db` (`Store/Hours.cs`); rules in `Endpoints/HoursEndpoints.cs`.

## Privacy

`/privacy` (content/pages/privacy.md) lists every cookie, browser-storage key and third party. Only strictly necessary cookies are used, so there's no banner; anything optional must be opt-in. **If a feature starts storing or sending something new, update that page.** YouTube loads only on click (`media.js`).

## Environments

| | Testing | Production |
|---|---|---|
| Branch | `site-v2` | `main` |
| URLs | testing.voss.industries (SMS at `/sms`) | avirobotics.org, www → apex, sms.avirobotics.org |
| `ASPNETCORE_ENVIRONMENT` | `Staging` → `appsettings.Staging.json` | `Production` → `appsettings.Production.json` |
| Store | open, PayPal sandbox | **coming soon**, PayPal `Off` (set `PayPal__Mode=Live` + creds in prod.env to turn donations on) |
| Service / port | `avi-site-testing` / 5081 | `avi-site-prod` / 5080 |
| Deploys | every push to `site-v2` (CI) | every push to `main` (CI) |
| Data (accounts, QR codes, usage, orders, sign-in keys) | inside the release: **starts empty after every deploy** (on purpose) | `~/.local/share/avi-site-prod/data` (kept across deploys) |

**Ship to prod:** once testing looks right, `git push origin site-v2:main` (fast-forward).
CI tests it, runs `deploy-site-prod <sha>` on the server and checks avirobotics.org,
sms.avirobotics.org and that the shop says coming soon. **Roll back:** push the older commit to
`main`, or on the server run the prod deploy with an older SHA (releases are kept).

**What's stored where:** page copy, photos and code are in git. Everything people change on the site lives in the
data directory, outside the release folders, so deploys never touch it:

- `site.db` (SQLite): dynamic QR codes (scans, saved looks, logos) and hours. Schema steps live in `Store/SiteDb.cs`
  and apply on startup; old `dynamic-qr.json` is imported once and kept as `dynamic-qr.json.imported`.
- `accounts.json` (people, teams), `countdowns.json`, `tool-usage.json`, `orders.json`: small JSON files.
- `keys/`: sign-in and cart cookies (losing it just signs everyone out).

Locally that's `src/AviSite.Web/App_Data/` (git-ignored). Back up with `sqlite3 site.db ".backup site-backup.db"`
(safe while the site runs) plus a copy of the JSON files and `keys/`. In production the site logs a warning at startup if the data directory is inside a release.

Server files: `deploy/systemd/avi-site-{prod,testing}.service` (user units),
`deploy/nginx/*.conf`, `deploy/scripts/deploy-{staging,production}.sh`.

## One-time production setup (server + DNS)

1. Testing unit: make sure it has `ASPNETCORE_ENVIRONMENT=Staging` (reference copy in
   `deploy/systemd/avi-site-testing.service`); sandbox creds in `~/.config/avi-site/testing.env`.
2. Prod unit: copy `deploy/systemd/avi-site-prod.service` to `~/.config/systemd/user/`,
   optional `~/.config/avi-site/prod.env` (Admin hash; PayPal Live later), `systemctl --user daemon-reload && systemctl --user enable avi-site-prod`.
3. Deploy hook: copy the `deploy-site-v2` forced command to `deploy-site-prod` pointing at
   `deploy/scripts/deploy-production.sh` and root `~/.local/share/avi-site-prod`; new key pair,
   `command=` restricted in `authorized_keys`; private key → GitHub secret `PROD_DEPLOY_KEY`.
4. First prod release by hand: `deploy-site-prod <site-v2 sha>`; check `curl 127.0.0.1:5080/healthz`.
5. nginx: install the new `sms.avirobotics.org.conf` (now → 5080) and reload. SMS is on prod from here.
6. DNS: point `avirobotics.org` + `www` (A/AAAA) at the Linode, turn GitHub Pages off in repo
   settings, get the cert (command in `deploy/nginx/avirobotics.org.conf`), install that conf, reload.
7. `git push origin site-v2:main`. From now on prod only changes through `main`.

QR-code paths (`showcase/`, `countdown/`, `vintagedays/`, `dynamicqr/`, `Files/`, `Logos/`)
are served unchanged from `src/AviSite.Web/legacy-static/`.

## Known draft limitations

- Storage is JSON files in `App_Data/`. Next step: EF Core + PostgreSQL (`avisite` DB).
  `ICatalog` and `IOrderStore` are the only things that change.
- No order emails yet (SecWeb already has SMTP code we can share).
- Products are edited in `App_Data/catalog.json` (copy `Store/catalog.seed.json`) + restart.
