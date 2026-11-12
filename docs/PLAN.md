# avirobotics.org rebuild: plan (draft 1)

Status: first draft, built 2026-11-11 while Robert was away. Everything marked
**TODO(confirm)** needs a human decision.

## Goals

1. Replace the under-construction page with a real public site for AVI Robotics
   (Fresno State) and its MATE ROV team, Sequoia Marine Solutions (SMS).
2. Sell team shirts through PayPal, with an order list the team can actually run
   pickup from.
3. Keep every URL that is already printed somewhere working (QR codes!).
4. Same stack as SecWeb (ajplant's portal), but a separate app and a separate
   database. Portal stays on its own subdomain.

## Stack

| Concern | Choice | Why |
|---|---|---|
| Framework | ASP.NET Core 10, Blazor Web App, **static SSR only** | Same as SecWeb; server-rendered HTML, no WebSocket circuit for a public site |
| Mutations | Minimal-API form endpoints (POST → redirect) + one Blazor `EditForm` for checkout | Plain HTML forms, works without JS |
| Payments | PayPal Orders v2 REST via `HttpClient` (no SDK) + PayPal JS buttons on the pay page | Server computes every price; client never sends amounts |
| Storage (draft) | JSON files under `App_Data/` behind `ICatalog` / `IOrderStore` | NuGet was unreachable from the build sandbox |
| Storage (next) | EF Core + PostgreSQL, **own database** (`avisite`), same server as SecWeb's | Swap the two interfaces; nothing else changes |
| Admin | Cookie auth, single admin password (PBKDF2 hash in config), rate-limited | Small and separate from SecWeb on purpose. Later: SSO with SecWeb |
| Hosting | Linode, nginx → Kestrel on `127.0.0.1:5080`, systemd service | Mirrors how SecWeb is deployed |
| Deploy | GitHub Actions: CI on every push; deploy is **manual** (`workflow_dispatch`) until we trust it | Nothing ships by accident |

## Sitemap

```
/                       Home
/about                  About AVI
/teams                  Teams index
/teams/sms              Sequoia Marine Solutions (MATE ROV)
/sponsors               Sponsorship + packet PDF
/join                   How to join (Discord, meetings)
/shop                   Shop index
/shop/{slug}            Product (size + qty)
/shop/cart              Cart
/shop/checkout          Buyer details → creates pending order
/shop/pay/{orderId}     PayPal buttons
/shop/orders/{orderId}  Confirmation / receipt (unguessable id)
/admin                  Orders dashboard (login required)
/api/paypal/*           create / capture / webhook
```

Legacy (served unchanged from `legacy-static/`):
`/showcase/`, `/countdown/`, `/vintagedays/`, `/dynamicqr/one|two|three|four/`,
`/Files/*`, `/Logos/*`. **TODO:** copy those folders from the current
`main-website` repo into `src/AviSite.Web/legacy-static/` before cutover.

## Store flow

1. Product page → POST `/cart/add` (slug, size, qty) → cart lives in a
   data-protected cookie.
2. Cart → Checkout form (name, email, phone optional, pickup note).
3. Checkout creates a **Pending** order with a snapshot of lines and prices
   computed on the server, then redirects to the pay page.
4. Pay page loads PayPal buttons. `createOrder` → our server creates the PayPal
   order from the stored snapshot. `onApprove` → our server captures, checks
   status and amount, marks **Paid**, clears the cart.
5. Webhook (`PAYMENT.CAPTURE.COMPLETED / DENIED / REFUNDED`) is verified with
   PayPal's verify-signature API and reconciles anything the browser missed.
6. Admin sees orders, per-size totals per product (what to order from the
   printer), marks orders picked up, exports CSV.

Development uses a **Fake** gateway (`PayPal:Mode=Fake`), refused at startup in
Production.

## Open questions for Robert (TODO(confirm))

- Brand kit: colors from `BRAND SWATCH.aco`, Porcine font license and where it is
  used, which logo is primary. (Couldn't reach the shared drive while drafting.)
- Shirt details: designs, prices, sizes, size upcharges, preorder deadline,
  pickup vs shipping.
- PayPal: business account owner, sandbox app credentials, refund policy.
- Domain plan: `avirobotics.org` for this site; portal at `portal.avirobotics.org`?
- Crush Depth: is it a team page on this site?
- Copy: mission statement, meeting times, contact email, Discord invite link,
  sponsor list and tiers.
- Photos: competition, pool tests, team photo (with consent).
