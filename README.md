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
| `PayPal:Mode` | env | `Fake` (dev only), `Sandbox`, `Live`. Production refuses anything but `Live` |
| `PayPal:ClientId` / `ClientSecret` / `WebhookId` | env / user-secrets | From developer.paypal.com → Apps & Credentials |
| `Admin:PasswordHash` | env | `dotnet run --project src/AviSite.Web -- hash-password` |
| `Site:*` | appsettings / env | Discord, Instagram, email, portal URL |
| `Store:OrdersOpen` | appsettings / env | Close the shop between runs |

PayPal webhook URL to register: `https://avirobotics.org/api/paypal/webhook`
(events: `PAYMENT.CAPTURE.COMPLETED`, `.DENIED`, `.REFUNDED`, `.REVERSED`).

## Deploy (Linode, alongside SecWeb)

`deploy/nginx/avirobotics.org.conf` and `deploy/systemd/avisite.service` have the
server setup steps in their comments. `.github/workflows/deploy.yml` publishes over
SSH; it's **manual** (`workflow_dispatch`) for now.

## Before cutover from GitHub Pages

1. ✅ Done on `site-v2`: `showcase/`, `countdown/`, `vintagedays/`, `dynamicqr/`, `Files/`,
   `Logos/` live in `src/AviSite.Web/legacy-static/` and are served unchanged, so printed
   QR codes keep working. The old construction page is in `archive/`.
2. Point DNS for avirobotics.org at the Linode, run certbot, then remove `CNAME` / Pages.

## Known draft limitations

- Storage is JSON files in `App_Data/`. Next step: EF Core + PostgreSQL (`avisite` DB).
  `ICatalog` and `IOrderStore` are the only things that change.
- No order emails yet (SecWeb already has SMTP code we can share).
- Products are edited in `App_Data/catalog.json` (copy `Store/catalog.seed.json`) + restart.
