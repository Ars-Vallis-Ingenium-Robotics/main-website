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
| `Admin:PasswordHash` | env | `dotnet run --project src/AviSite.Web -- hash-password` |
| `Site:*` | appsettings / env | Discord, Instagram, email, portal URL |
| `Store:OrdersOpen` | appsettings / env | Products visible, checkout closed |
| `Store:ComingSoon` | appsettings / env | Between drops: /shop shows "next drop" (content/pages/shop.md › coming-soon); product/cart/checkout redirect to /shop |
| `Site:NoIndex` | appsettings | Testing only: `X-Robots-Tag: noindex` |

PayPal webhook URL to register: `https://avirobotics.org/api/paypal/webhook`
(events: `PAYMENT.CAPTURE.COMPLETED`, `.DENIED`, `.REFUNDED`, `.REVERSED`).

## Environments

| | Testing | Production |
|---|---|---|
| Branch | `site-v2` | `main` |
| URLs | testing.voss.industries (SMS at `/sms`) | avirobotics.org, www → apex, sms.avirobotics.org |
| `ASPNETCORE_ENVIRONMENT` | `Staging` → `appsettings.Staging.json` | `Production` → `appsettings.Production.json` |
| Store | open, PayPal sandbox | **coming soon**, PayPal `Off` (set `PayPal__Mode=Live` + creds in prod.env to turn donations on) |
| Service / port | `avi-site-testing` / 5081 | `avi-site-prod` / 5080 |
| Deploys | every push to `site-v2` (CI) | every push to `main` (CI) |
| Orders + keys | inside the release (throwaway) | `~/.local/share/avi-site-prod/data` (kept) |

**Ship to prod:** once testing looks right, `git push origin site-v2:main` (fast-forward).
CI tests it, runs `deploy-site-prod <sha>` on the server and checks avirobotics.org,
sms.avirobotics.org and that the shop says coming soon. **Roll back:** push the older commit to
`main`, or on the server run the prod deploy with an older SHA (releases are kept).

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
