// End-to-end smoke test against a running Development instance (fake PayPal).
//   dotnet run --project src/AviSite.Web   (ASPNETCORE_ENVIRONMENT=Development)
//   BASE_URL=http://localhost:5002 node tests/e2e/smoke.mjs
// Requires: npm i -D playwright (or a global Playwright install).
import { chromium, devices } from "playwright";
import assert from "node:assert/strict";

const BASE = process.env.BASE_URL ?? "http://127.0.0.1:5080";
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD ?? "avi-dev-password";
const results = [];
async function test(name, fn) {
  try { await fn(); results.push(["PASS", name]); }
  catch (e) { results.push(["FAIL", name, e.message.split("\n")[0]]); }
}

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
const page = await ctx.newPage();
const consoleErrors = [];
// 404s are expected: the not-found tests request missing pages on purpose.
// YouTube thumbnails (i.ytimg.com) are blocked in the offline test sandbox; ignore only those.
page.on("console", (m) => { if (m.type() === "error" && !/status of 404/.test(m.text()) && !/ytimg\.com|youtube/.test(m.location()?.url || "")) consoleErrors.push(m.text()); });

const routes = ["/", "/about", "/projects", "/history", "/leadership", "/teams", "/sponsors", "/join", "/shop", "/shop/sms-team-tee", "/shop/cart", "/shop/checkout", "/admin/login", "/sms", "/sms/rosie", "/sms/adam", "/sms/2026", "/sms/company", "/donate"];

await test("every public route returns 200 with a single h1", async () => {
  for (const r of routes) {
    const res = await page.goto(BASE + r);
    assert.equal(res.status(), 200, r);
    assert.equal(await page.locator("h1").count(), 1, `${r} h1 count`);
  }
});

await test("unknown URLs and /not-found return 404", async () => {
  assert.equal((await page.goto(BASE + "/definitely-not-here")).status(), 404);
  assert.equal((await page.goto(BASE + "/not-found")).status(), 404);
  assert.equal((await page.goto(BASE + "/shop/no-such-shirt")).status(), 404);
});

await test("legacy URLs from the old site still work (QR codes)", async () => {
  for (const r of ["/showcase/", "/countdown/", "/vintagedays/", "/dynamicqr/one/", "/dynamicqr/two/", "/dynamicqr/three/", "/dynamicqr/four/", "/Files/aviSponsorship.pdf", "/Logos/aviDogDefault.png"]) {
    const res = await page.request.get(BASE + r, { maxRedirects: 0 });
    assert.equal(res.status(), 200, r);
  }
  const res = await page.request.get(BASE + "/showcase", { maxRedirects: 0 });
  assert.ok([301, 302].includes(res.status()), "/showcase should redirect to /showcase/");
});

await test("SMS site works on its own host and links back to AVI", async () => {
  // Chromium resolves *.localhost to loopback, so sms.localhost:<port> exercises host-based routing.
  const SMS = BASE.replace(/\/\/[^:/]+/, "//sms.localhost");
  const sms = await browser.newContext();
  const p = await sms.newPage();
  const res = await p.goto(SMS + "/rosie");
  assert.equal(res.status(), 200);
  assert.match(await p.locator("h1").innerText(), /ROSIE/);
  assert.match(await p.locator(".sms-back").first().getAttribute("href"), /^https?:\/\/[^/]+\/$/, "absolute back link to AVI");
  const old = await page.request.get(BASE + "/sms/adam", { maxRedirects: 0, headers: { Host: "sms.localhost" } });
  assert.equal(old.status(), 301);
  await sms.close();
  // ...and at /sms on the main site, with a relative link back
  await page.goto(BASE + "/sms");
  assert.ok(await page.locator('.sms-back[href="/"]').count() > 0);
  assert.equal((await page.request.get(BASE + "/teams/sms", { maxRedirects: 0 })).status(), 302);
});

await test("nonprofit status and EIN appear in the footer", async () => {
  await page.goto(BASE + "/");
  const footer = await page.locator("footer").innerText();
  assert.match(footer, /501\(c\)\(3\)/);
  assert.match(footer, /39-4775163/);
});

await test("photos ship multiple resolutions with a blurred placeholder", async () => {
  await page.goto(BASE + "/");
  const img = page.locator("img.photo__img").first();
  assert.match(await img.getAttribute("srcset"), /480w.*960w.*1600w/);
  assert.match(await page.locator("figure.photo").first().getAttribute("style"), /--lqip: url\('data:image\/webp;base64/);
});

await test("security headers present", async () => {
  const res = await page.goto(BASE + "/");
  const h = res.headers();
  assert.match(h["content-security-policy"] ?? "", /frame-ancestors 'none'/);
  assert.equal(h["x-content-type-options"], "nosniff");
});

await test("/admin requires login", async () => {
  await page.goto(BASE + "/admin");
  assert.match(page.url(), /\/admin\/login/);
});

await test("state-changing POSTs without antiforgery token are rejected", async () => {
  for (const path of ["/cart/add", "/admin/logout", "/api/paypal/orders/x/create"]) {
    const r = await page.request.post(BASE + path, { form: { slug: "sms-team-tee", size: "M", qty: "1" }, maxRedirects: 0 });
    assert.ok(r.status() === 400 || r.status() === 404, `${path} → ${r.status()}`);
  }
});

let orderUrl;
await test("buy flow: size required → cart → checkout validation → fake PayPal → paid", async () => {
  await page.goto(BASE + "/shop/sms-team-tee");
  await page.locator('label[for="size-XL"]').click();
  await page.locator('input[name="qty"]').fill("2");
  await page.getByRole("button", { name: "Add to cart" }).click();
  await page.waitForURL(/\/shop\/cart/);
  assert.match(await page.locator(".cart").innerText(), /SMS Team Tee/);
  assert.match(await page.locator(".cart__subtotal").innerText(), /\$50\.00/);

  // 2XL upcharge
  await page.goto(BASE + "/shop/sms-team-tee");
  await page.locator('label[for="size-2XL"]').click();
  await page.getByRole("button", { name: "Add to cart" }).click();
  await page.waitForURL(/\/shop\/cart/);
  assert.match(await page.locator(".cart__subtotal").innerText(), /\$78\.00/);

  await page.getByRole("link", { name: "Check out" }).click();
  await page.getByRole("button", { name: "Continue to payment" }).click();
  assert.match(await page.locator(".validation-wrap").innerText(), /Enter your name/);
  assert.match(await page.title(), /^Error:/);

  await page.getByLabel("Full name").fill("Test Buyer");
  await page.getByLabel("Email").fill("test@example.com");
  // Phone left empty on purpose: optional field must not error.
  await page.getByRole("button", { name: "Continue to payment" }).click();
  await page.waitForURL(/\/shop\/pay\//);
  await page.getByRole("button", { name: "Simulate PayPal payment" }).click();
  await page.waitForURL(/\/shop\/orders\//);
  assert.match(await page.locator("h1").innerText(), /Thanks, Test/i);
  orderUrl = page.url();

  await page.goto(BASE + "/shop/cart");
  assert.match(await page.locator("main").innerText(), /Nothing here yet/);
});

await test("paid order can't be paid again", async () => {
  const payUrl = orderUrl.replace("/shop/orders/", "/shop/pay/");
  await page.goto(payUrl);
  assert.match(page.url(), /\/shop\/orders\//);
});

await test("tampered cart cookie is discarded", async () => {
  await ctx.addCookies([{ name: "avi_cart", value: "garbage", url: BASE }]);
  await page.goto(BASE + "/shop/cart");
  assert.match(await page.locator("main").innerText(), /Nothing here yet/);
});

await test("admin: login, see order in printer table, mark picked up, CSV", async () => {
  await page.goto(BASE + "/admin/login");
  await page.getByLabel("Password").fill(ADMIN_PASSWORD);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL(BASE + "/admin");
  const text = await page.locator("main").innerText();
  assert.match(text, /Test Buyer/);
  assert.match(text, /\$78\.00/);
  await page.getByRole("button", { name: "Mark picked up" }).first().click();
  await page.waitForURL(/\/admin/);
  assert.match(await page.locator("main").innerText(), /Picked up ✓/);
  const csv = await page.request.get(BASE + "/admin/orders.csv");
  assert.equal(csv.status(), 200);
  assert.match(await csv.text(), /Test Buyer/);
});

await test("phone layout: no horizontal scroll, menu opens and Escape closes it", async () => {
  const phone = await browser.newContext({ ...devices["iPhone 13"] });
  const p = await phone.newPage();
  for (const r of routes) {
    await p.goto(BASE + r);
    assert.equal(await p.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth), true, r);
  }
  await p.goto(BASE + "/about"); // the home page hides the nav until you scroll past the landing photo
  await p.locator(".nav__toggle").click();
  assert.equal(await p.locator(".nav--narrow").getAttribute("open"), "");
  await p.keyboard.press("Escape");
  assert.equal(await p.locator(".nav--narrow").getAttribute("open"), null);
  await phone.close();
});

await test("donate: pick an amount → fake PayPal → receipt with IRS language", async () => {
  await page.goto(BASE + "/donate");
  await page.locator('label.chip:has-text("$100")').click();
  await page.fill("#name", "Test Donor");
  await page.fill("#email", "donor@example.com");
  await page.click('button[type="submit"]');
  await page.waitForURL(/\/shop\/pay\//);
  assert.match(await page.locator("h1").innerText(), /donation/i);
  await page.click("#fake-pay");
  await page.waitForURL(/\/shop\/orders\//);
  const text = await page.locator("main").innerText();
  assert.match(text, /Donation received/i);
  assert.match(text, /\$100\.00/);
  assert.match(text, /No goods or services/);
});

await test("donate: amount outside $5–$10,000 is refused", async () => {
  await page.goto(BASE + "/donate");
  await page.locator('label.chip:has-text("Other")').click();
  await page.fill("#custom", "2");
  await page.fill("#name", "Test Donor");
  await page.fill("#email", "donor@example.com");
  await page.click('button[type="submit"]');
  assert.match(page.url(), /\/donate/);
  assert.ok(await page.locator(".alert, .field__error").count() > 0);
});

// "EIN" and the number must sit on one line, everywhere, at phone and desktop widths.
await test("EIN label and number never wrap apart", async () => {
  const pages = ["/", "/about", "/sponsors", "/donate", "/sms"];
  for (const width of [360, 1280]) {
    await page.setViewportSize({ width, height: 900 });
    for (const r of pages) {
      await page.goto(BASE + r);
      const bad = await page.evaluate(() => {
        const out = [];
        const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
        while (walker.nextNode()) {
          const n = walker.currentNode;
          if (!/\bEIN\b/.test(n.textContent) || n.parentElement.closest("script,style,head")) continue;
          const holder = n.parentElement.closest(".ein");
          if (!holder) { out.push("EIN outside .ein: " + n.textContent.trim().slice(0, 60)); continue; }
          if (!/\d{2}-\d{7}/.test(holder.textContent)) out.push("no number next to EIN");
          const range = document.createRange(); range.selectNodeContents(holder);
          const tops = [...range.getClientRects()].filter((q) => q.width > 0).map((q) => q.top + q.height / 2);
          if (Math.max(...tops) - Math.min(...tops) > 4) out.push("EIN wraps: " + holder.textContent);
        }
        return out;
      });
      assert.deepEqual(bad, [], `${r} @${width}px: ${bad.join("; ")}`);
    }
  }
  await page.setViewportSize({ width: 1280, height: 900 });
});

// Case rule: Porcine (display) text is always capitals; buttons/nav use sentence case
// (first word capitalized, the rest lowercase unless it's a proper noun or acronym).
await test("consistent letter case", async () => {
  const proper = ["AVI", "SMS", "ROSIE", "ADAM", "MATE", "ROV", "MESA", "PayPal", "PDF", "Discord", "Instagram", "Fresno", "State", "Micromouse", "Robotics", "Sequoia", "Marine", "Solutions", "Explorer", "Worlds", "St.", "John's", "I"];
  const bad = [];
  for (const r of ["/", "/about", "/projects", "/history", "/leadership", "/teams", "/sponsors", "/join", "/shop", "/donate", "/sms", "/sms/rosie", "/sms/2026", "/sms/company"]) {
    await page.goto(BASE + r);
    const found = await page.evaluate((proper) => {
      const out = [];
      for (const el of document.querySelectorAll("body *")) {
        const cs = getComputedStyle(el);
        if (!/Porcine/i.test(cs.fontFamily.split(",")[0])) continue;
        if (![...el.childNodes].some((c) => c.nodeType === 3 && /[a-z]/i.test(c.textContent))) continue;
        if (cs.textTransform !== "uppercase") out.push(`display type not uppercase: <${el.tagName.toLowerCase()} class="${el.className}"> ${el.textContent.trim().slice(0, 40)}`);
      }
      for (const el of document.querySelectorAll(".btn, .nav a, .link-arrow, summary")) {
        if (getComputedStyle(el).textTransform === "uppercase") continue;
        const t = el.innerText.replace(/[^\p{L}\p{N}'.\s()-]/gu, " ").trim();
        if (!t) continue;
        const words = t.split(/\s+/);
        if (words[0][0] !== words[0][0].toUpperCase()) out.push(`label starts lowercase: "${t}"`);
        for (const w of words.slice(1)) {
          if (/^[A-Z]/.test(w) && !proper.includes(w) && !/^[A-Z0-9.'()-]+$/.test(w)) out.push(`Title Case word "${w}" in "${t}"`);
        }
      }
      return out;
    }, proper);
    for (const f of found) bad.push(`${r}: ${f}`);
  }
  assert.deepEqual([...new Set(bad)], [], [...new Set(bad)].slice(0, 6).join(" | "));
});

await test("timelines open into chapters, moments pop up, and the story ends unfinished", async () => {
  for (const r of ["/history", "/sms/2026"]) {
    await page.goto(BASE + r);
    assert.equal(await page.locator(".timeline > li").last().evaluate((li) => li.classList.contains("timeline__item--next")), true, `${r} ends with a to-be-continued node`);
    const open = page.locator(".timeline__open").first();
    const subId = await open.getAttribute("data-sub");
    await open.click();
    const sub = page.locator("#" + subId);
    await sub.waitFor({ state: "visible" });
    assert.equal(await page.locator(".tl > .timeline").isVisible(), false, `${r} main timeline gives way to the chapter`);
    await sub.locator(".subtl__card").first().click();
    const dlg = page.locator("dialog.moment-sheet[open]");
    await dlg.waitFor({ state: "visible" });
    await page.keyboard.press("Escape");
    await dlg.waitFor({ state: "detached" }).catch(() => {});
    assert.equal(await page.locator("dialog.moment-sheet[open]").count(), 0, `${r} pop-up closes`);
    await sub.locator("[data-sub-close]").click();
    await page.locator(".tl > .timeline").waitFor({ state: "visible" });
  }
  // without JS the chapter is still reachable through its #anchor
  await page.goto(BASE + "/history#history-sub-1");
  assert.equal(await page.locator("#history-sub-1").isVisible(), true, "deep link opens a chapter");
});

await test("photos open in a lightbox", async () => {
  await page.goto(BASE + "/about");
  const img = page.locator(".collage .photo__img").first();
  await img.scrollIntoViewIfNeeded();
  await img.click();
  const box = page.locator("dialog.lightbox[open]");
  await box.waitFor({ state: "visible" });
  assert.match(await box.locator(".lightbox__img").getAttribute("src"), /-(1600|2400)\.webp$/);
  await page.keyboard.press("Escape");
  assert.equal(await page.locator("dialog.lightbox[open]").count(), 0);
});

await test("people photos, sponsor logos and SMS videos render", async () => {
  await page.goto(BASE + "/leadership");
  assert.ok(await page.locator(".person__photo").count() >= 8, "headshots on leadership");
  await page.goto(BASE + "/sponsors");
  const logos = page.locator(".sponsor-wall img");
  assert.ok(await logos.count() >= 15, "sponsor logos");
  for (const alt of await logos.evaluateAll((imgs) => imgs.map((i) => i.alt))) assert.ok(alt.length > 2, "logo alt text");
  await page.goto(BASE + "/sms");
  assert.equal(await page.locator('iframe[src^="https://www.youtube-nocookie.com/embed/"]').count(), 1, "one big video frame");
  assert.equal(await page.locator("[data-video-tab]").count(), 3, "three SMS videos to cycle through");
  await page.locator("[data-video-next]").click();
  assert.match(await page.locator(".vcar iframe").getAttribute("src"), /THIZyHml-es/, "arrow moves to the next video");
  await page.goto(BASE + "/sms/rosie");
  assert.equal(await page.locator("model-viewer[src='/models/rosie.glb']").count(), 1, "ROSIE 3D model");
  const csp = (await page.goto(BASE + "/sms")).headers()["content-security-policy"];
  assert.match(csp, /frame-src[^;]*https:\/\/www\.youtube-nocookie\.com/);
});

await test("SMS company: tapping a person or crew opens a detail pop-up", async () => {
  await page.goto(BASE + "/sms/company");
  await page.locator('.tiles--crews a[data-dialog]').first().click();
  const d = page.locator("dialog[open]");
  assert.equal(await d.count(), 1, "crew dialog open");
  assert.match(await d.innerText(), /What they built/i);
  await page.keyboard.press("Escape");
  assert.equal(await page.locator("dialog[open]").count(), 0, "Escape closes it");
  await page.locator('.tiles--people a[data-dialog]').first().click();
  assert.equal(await page.locator("dialog[open]").count(), 1, "person dialog open");
});

await test("PDF links (sponsor packet, submission docs) open in a new tab", async () => {
  for (const r of ["/", "/sponsors", "/sms/2026"]) {
    await page.goto(BASE + r);
    const bad = await page.locator('a[href$=".pdf"]:not([target="_blank"])').count();
    assert.equal(bad, 0, `${r} has PDF links without target=_blank`);
  }
  assert.ok(await page.locator(".docs a").count() >= 8, "submission documents listed");
});

await test("no sideways scroll on any page at 360px", async () => {
  const phone = await browser.newPage({ viewport: { width: 360, height: 800 } });
  const wide = [];
  for (const r of routes) {
    await phone.goto(BASE + r);
    const sw = await phone.evaluate(() => document.documentElement.scrollWidth);
    if (sw > 360) wide.push(`${r} (${sw}px)`);
  }
  await phone.close();
  assert.deepEqual(wide, [], wide.join(", "));
});

await test("no console errors", async () => { assert.deepEqual(consoleErrors, [], consoleErrors.join(" | ")); });

await browser.close();
for (const r of results) console.log(r.join("  "));
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
