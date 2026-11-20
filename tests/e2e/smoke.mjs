// End-to-end smoke test against a running Development instance (fake PayPal).
//   dotnet run --project src/AviSite.Web   (ASPNETCORE_ENVIRONMENT=Development)
//   BASE_URL=http://localhost:5002 node tests/e2e/smoke.mjs
// Requires: npm i -D playwright (or a global Playwright install).
import { chromium, devices } from "playwright";
import assert from "node:assert/strict";

const BASE = process.env.BASE_URL ?? "http://127.0.0.1:5080";
// These tests contain write flows. Refuse remote targets and any local app not exposing Development-only sign-in.
const baseUrl = new URL(BASE);
if (!["localhost", "127.0.0.1", "::1", "[::1]"].includes(baseUrl.hostname)) {
  throw new Error("E2E smoke tests are local-only; set BASE_URL to a local Development instance.");
}
const loginCheck = await fetch(new URL("/admin/login", baseUrl), { redirect: "manual" });
const loginHtml = await loginCheck.text();
if (loginCheck.status !== 200 || !loginHtml.includes("Sign in (dev only)")) {
  throw new Error("Refusing E2E run: target is not a local Development instance with dev-only sign-in.");
}
const ADMIN_PASSWORD = process.env.ADMIN_PASSWORD ?? "avi-dev-password";
const results = [];
// ONLY=layout node smoke.mjs runs just the tests whose name contains "layout".
const ONLY = process.env.ONLY;
async function test(name, fn) {
  if (ONLY && !name.includes(ONLY) && !new RegExp(ONLY).test(name)) return;
  try { await fn(); results.push(["PASS", name]); }
  catch (e) { results.push(["FAIL", name, e.message.split("\n")[0]]); }
}

const browser = await chromium.launch();
const ctx = await browser.newContext({ viewport: { width: 1280, height: 900 } });
// Anything the Content-Security-Policy blocks (inline handlers, inline scripts, eval) becomes a console error,
// so the "no console errors" test at the end fails instead of the bug reaching production.
await ctx.addInitScript(() => document.addEventListener("securitypolicyviolation", (e) =>
  console.error(`CSP blocked ${e.violatedDirective}: ${e.blockedURI || "inline"} (${e.sourceFile || location.pathname}:${e.lineNumber})`)));
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
    // Printed dynamic QR codes are now server redirects (302); everything else is served as-is.
    assert.equal(res.status(), r.startsWith("/dynamicqr/") ? 302 : 200, r);
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
  await page.click("text=Use a shared password instead");
  await page.getByLabel("Password").fill(ADMIN_PASSWORD);
  await page.getByRole("button", { name: "Sign in with password" }).click();
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
  await page.click('main button[type="submit"]');
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
  await page.click('main button[type="submit"]');
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
  await page.goto(BASE + "/history#history-sub-2");
  assert.equal(await page.locator("#history-sub-2").isVisible(), true, "deep link opens a chapter");
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
  assert.equal(await page.locator(".vcar [data-video-play]").count(), 1, "one big video frame (poster until play)");
  assert.equal(await page.locator("[data-video-tab]").count(), 3, "three SMS videos to cycle through");
  await page.locator("[data-video-next]").click();
  assert.match(await page.locator(".vcar [data-video-play]").getAttribute("href"), /THIZyHml-es/, "arrow moves the poster to the next video");
  await page.locator(".vcar [data-video-play]").click();
  assert.match(await page.locator(".vcar iframe").getAttribute("src"), /THIZyHml-es/, "play loads that video");
  await page.locator("[data-video-next]").click();
  assert.doesNotMatch(await page.locator(".vcar iframe").getAttribute("src"), /THIZyHml-es/, "arrow still moves after play");
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

await test("phone menu is a full-height sheet showing every link, on every page", async () => {
  const ctx = await browser.newContext({ ...devices["iPhone 13"] });
  const phone = await ctx.newPage();
  for (const [r, y] of [["/", 2000], ["/about", 0], ["/sms/2026", 600]]) {
    await phone.goto(BASE + r);
    await phone.evaluate((y) => scrollTo(0, y), y);
    await phone.waitForTimeout(300);
    await phone.click(".nav--narrow summary");
    const sheet = phone.locator(".nav--narrow[open] .nav__sheet");
    const box = await sheet.boundingBox();
    const vh = await phone.evaluate(() => innerHeight);
    assert.ok(box && box.height > vh * 0.8, `${r}: sheet is only ${box?.height}px tall`);
    const links = sheet.locator("a");
    const n = await links.count();
    assert.ok(n >= 5, `${r}: only ${n} links`);
    for (let i = 0; i < n; i++) {
      await links.nth(i).scrollIntoViewIfNeeded();
      assert.ok(await links.nth(i).isVisible(), `${r}: link ${i} hidden`);
      const h = (await links.nth(i).boundingBox()).height;
      assert.ok(h >= 44, `${r}: link ${i} tap target ${h}px`);
    }
    await phone.keyboard.press("Escape");
  }
  await ctx.close();
});

await test("every zoomable photo has a caption", async () => {
  const missing = [];
  for (const r of routes) {
    await page.goto(BASE + r);
    const caps = await page.evaluate(() => [...document.querySelectorAll("figure.photo:not(.no-zoom) .photo__img")]
      .filter((i) => !i.closest("a, button, .hero"))
      .map((img) => { const f = img.closest("figure"); return [img.getAttribute("src"), (f.querySelector(".photo__caption")?.textContent || f.dataset.caption || img.alt || "").trim()]; }));
    for (const [src, cap] of caps) if (cap.length < 12) missing.push(`${r} ${src}`);
  }
  assert.deepEqual(missing, [], missing.slice(0, 5).join(" | "));
  await page.goto(BASE + "/");
  await page.locator(".what__photo .photo__img").first().click();
  assert.ok((await page.locator(".lightbox__cap").textContent()).trim().length > 12, "decorative photo opens with a caption");
  await page.keyboard.press("Escape");
});

await test("collages are horizontal scroll bands", async () => {
  for (const r of ["/", "/about", "/projects", "/sms/2026"]) {
    await page.goto(BASE + r);
    const s = page.locator(".collage-band__scroller").first();
    const [sw, cw] = await s.evaluate((el) => [el.scrollWidth, el.clientWidth]);
    assert.ok(sw > cw * 1.5, `${r}: band doesn't overflow sideways (${sw} vs ${cw})`);
    await s.evaluate((el) => { el.scrollLeft = 400; });
    assert.ok(await s.evaluate((el) => el.scrollLeft) > 0, `${r}: band doesn't scroll`);
    assert.ok(await page.locator(".collage-band .photo").count() >= 16, `${r}: fewer than 16 collage photos`);
  }
});

await test("landing shows the AVI dog and says Robotics", async () => {
  await page.goto(BASE + "/");
  assert.ok(await page.locator(".landing__mark .landing__dog").isVisible());
  assert.match(await page.locator(".landing__mark").innerText(), /AVI\s*Robotics/i);
  assert.match(await page.locator("#hero-title").getAttribute("aria-label"), /^AVI Robotics/);
});

await test("Worlds chapter has the three-team stage photo", async () => {
  for (const r of ["/history", "/sms/2026"]) {
    await page.goto(BASE + r);
    assert.ok(await page.locator('img[src*="worlds-stage-friends"]').count() >= 1, r);
  }
});

await test("all copy comes from content/*.md (no missing sections)", async () => {
  const bad = [];
  for (const r of [...routes, "/people/robert-voss"]) {
    await page.goto(BASE + r);
    const html = await page.content();
    if (html.includes("⟦missing")) bad.push(`${r}: ${html.match(/⟦missing[^⟧]*⟧/)[0]}`);
  }
  assert.deepEqual(bad, [], bad.slice(0, 5).join(" | "));
});

await test("editing a content file shows up on refresh, no restart", async () => {
  const fs = await import("node:fs");
  const file = new URL("../../content/pages/teams.md", import.meta.url);
  const original = fs.readFileSync(file, "utf8");
  try {
    fs.writeFileSync(file, original.replace("Where the robots get built.", "Where the robots get built (edited)."));
    await page.waitForTimeout(50);
    await page.goto(BASE + "/teams");
    assert.match(await page.locator("h1").textContent(), /\(edited\)/);
  } finally {
    fs.writeFileSync(file, original);
  }
  await page.goto(BASE + "/teams");
  assert.doesNotMatch(await page.locator("h1").textContent(), /\(edited\)/);
});

await test("names open a person pop-up; profiles show only what's shared", async () => {
  await page.goto(BASE + "/leadership");
  await page.locator('a[data-person="declan-doss"]').first().click();
  const sheet = page.locator(".person-sheet[open]");
  await sheet.waitFor();
  assert.match(await sheet.innerText(), /President · AVI Robotics/);
  assert.match(await sheet.innerText(), /Chief Technical Officer · Sequoia Marine Solutions/);
  await page.keyboard.press("Escape");
  assert.equal(await page.locator(".person-sheet[open]").count(), 0);
  const people = await (await page.request.get(BASE + "/people.json")).json();
  assert.ok(Object.keys(people).length > 50, "everyone named on the site is in /people.json");
  assert.ok(Object.values(people).every((p) => !p.links || p.links.every((l) => l.href)), "links are complete");
  assert.equal((await page.goto(BASE + "/people/robert-voss")).status(), 200);
  assert.equal((await page.goto(BASE + "/people/not-a-member")).status(), 404);
});

await test("every pop-up keeps its close button in view while scrolling (sticky)", async () => {
  await page.setViewportSize({ width: 390, height: 640 });
  const check = async (dlg, what) => {
    await dlg.waitFor({ state: "visible" });
    const r = await dlg.evaluate((el) => {
      el.scrollTop = el.scrollHeight;
      const c = el.querySelector(".sheet__close").getBoundingClientRect(), b = el.getBoundingClientRect();
      return { scrolled: el.scrollTop > 0, inView: c.top >= b.top - 1 && c.bottom <= b.bottom + 1 };
    });
    assert.ok(r.inView, `${what}: close button scrolled out of view`);
    return r;
  };
  try {
    await page.goto(BASE + "/history");
    const open = page.locator(".timeline__open").first();
    const sub = page.locator("#" + (await open.getAttribute("data-sub")));
    await open.click();
    await sub.locator(".subtl__card").first().click();
    await check(page.locator("dialog.moment-sheet[open]"), "moment");
    await page.locator("dialog.moment-sheet[open] .sheet__close").click();
    await page.locator("dialog.moment-sheet[open]").waitFor({ state: "detached" }).catch(() => {});
    assert.equal(await page.locator("dialog.moment-sheet[open]").count(), 0, "moment closes from its sticky button");

    await page.goto(BASE + "/sms/company");
    await page.locator("a[href^='#crew-']").first().click();
    const scrolled = await check(page.locator("dialog[open]"), "crew");
    assert.ok(scrolled.scrolled, "crew sheet is long enough on a phone to actually scroll");

    await page.goto(BASE + "/leadership");
    await page.locator("a.person-link").first().click();
    await check(page.locator("dialog.person-sheet[open]"), "person");
  } finally {
    await page.setViewportSize({ width: 1280, height: 900 });
  }
});

await test("join button never wraps: full label when roomy, \"Join\" when tight", async () => {
  const anon = await browser.newContext(); const page = await anon.newPage(); // signed out: signed in, the header shows the clock instead
  try {
    for (const [w, label] of [[1000, "Join"], [1280, "Join the club"]]) {
      await page.setViewportSize({ width: w, height: 700 });
      await page.goto(BASE + "/about");
      const j = page.locator(".nav--wide .nav__join");
      assert.equal((await j.innerText()).trim(), label, `label at ${w}px`);
      assert.equal(await j.getAttribute("aria-label"), "Join the club");
      assert.ok((await j.boundingBox()).height < 48, `join button stays one line at ${w}px`);
      assert.equal(await page.evaluate(() => { const b = document.querySelector(".site-header__bar"); return b.scrollWidth <= b.clientWidth + 1; }), true, `header fits at ${w}px`);
    }
  } finally {
    await anon.close();
  }
});

await test("leadership cards in a row match heights; emails stay on one line, uncut", async () => {
  try {
    for (const w of [1280, 1024, 768, 390]) {
      await page.setViewportSize({ width: w, height: 900 });
      await page.goto(BASE + "/leadership");
      const r = await page.evaluate(() => ({
        cut: [...document.querySelectorAll(".person__email")].filter((e) => e.scrollWidth > e.clientWidth + 1 || e.clientHeight - parseFloat(getComputedStyle(e).paddingTop) > 2 * parseFloat(getComputedStyle(e).fontSize)).map((e) => e.textContent),
        uneven: [...document.querySelectorAll("ul.people")].flatMap((u) => { const rows = {}; u.querySelectorAll(":scope > li > .person").forEach((a) => { const b = a.getBoundingClientRect(); (rows[Math.round(b.top)] ??= []).push(Math.round(b.height)); }); return Object.values(rows).filter((h) => new Set(h).size > 1); }),
      }));
      assert.deepEqual(r.cut, [], `emails cut or wrapped at ${w}px`);
      assert.deepEqual(r.uneven, [], `uneven card heights at ${w}px`);
    }
  } finally {
    await page.setViewportSize({ width: 1280, height: 900 });
  }
});

await test("tools: QR maker draws a code from ?url and offers PNG/SVG", async () => {
  await page.goto(BASE + "/tools/qr?url=" + encodeURIComponent("https://avirobotics.org/join") + "&style=sms");
  await page.locator("[data-qr-preview] svg").waitFor();
  assert.match(await page.locator("[data-qr-status]").innerText(), /squares/);
  assert.equal(await page.locator('[data-qr-theme="sms"]').getAttribute("aria-pressed"), "true");
  const [dl] = await Promise.all([page.waitForEvent("download"), page.click('[data-qr-download="svg"]')]);
  assert.match(dl.suggestedFilename(), /^qr-avirobotics-org-join\.svg$/);
  // Enter in a field must not submit the form (reload, lose everything) or trip the CSP.
  const before = page.url();
  await page.locator('[data-qr="url"]').press("Enter");
  await page.waitForTimeout(300);
  assert.equal(page.url(), before, "Enter doesn't navigate");
  assert.equal(await page.locator("[data-qr-preview] svg").count(), 1, "preview survives Enter");
  await page.click(".qr-custom > summary");
  for (const d of ["fluid", "vertical", "star"]) { await page.check(`input[name="qr-dot"][value="${d}"]`); await page.locator("[data-qr-preview] svg").waitFor(); }
  await page.click('[data-qr-tab="label"]');
  await page.check('input[name="qr-frame"][value="below"]');
  await page.fill("#qr-ftext", "SCAN ME");
  assert.match(await page.locator("[data-qr-preview] svg").innerHTML(), /SCAN ME/);
  await page.selectOption("#qr-kind", "wifi");
  assert.equal(await page.locator('[data-qr-download="png"]').isDisabled(), true, "Wi-Fi needs a network name");
  await page.fill("#qr-ssid", "AVI Lab");
  assert.equal(await page.locator('[data-qr-download="png"]').isDisabled(), false);
  // Many codes: a pasted list becomes a batch and zips
  await page.click('[data-qr-mode="bulk"]');
  await page.fill("#qr-bulktext", "Week 1, https://avirobotics.org/a\nWeek 2\thttps://avirobotics.org/b");
  await page.waitForFunction(() => document.querySelector("[data-qr-bulkcount]").textContent.startsWith("2 codes"));
  const [zip] = await Promise.all([page.waitForEvent("download"), page.click('[data-qr-zip="svg"]')]);
  assert.match(zip.suggestedFilename(), /^qr-codes-2\.zip$/);
  await page.click('[data-qr-mode="one"]');
  await page.selectOption("#qr-kind", "url");
  await page.fill('[data-qr="url"]', "");
  assert.equal(await page.locator('[data-qr-download="png"]').isDisabled(), true, "nothing to download without text");
  await page.goto(BASE + "/tools");
  assert.match(await page.locator("main").innerText(), /QR code maker[\s\S]*Countdowns/);
});

await test("dynamic QR: printed codes redirect and count; admin can repoint one", async () => {
  const one = await page.request.get(BASE + "/dynamicqr/one", { maxRedirects: 0 });
  assert.equal(one.status(), 302);
  assert.equal(one.headers()["cache-control"], "no-store");
  assert.equal((await page.request.get(BASE + "/dynamicqr/one/index.html", { maxRedirects: 0 })).status(), 302, "old index.html form still works");
  assert.equal((await page.request.get(BASE + "/dynamicqr/not-a-code", { maxRedirects: 0 })).status(), 404);
  assert.equal((await page.request.get(BASE + "/dynamicqr/DynamicQR/aviDynamicQR-one.png")).status(), 200, "old PNGs still served");
  const anon = await browser.newContext(); const ap = await anon.newPage();
  await ap.goto(BASE + "/admin/qr"); assert.match(ap.url(), /\/admin\/login/, "admin/qr needs login"); await anon.close();
  await page.goto(BASE + "/admin/qr"); // signed in by the admin test above
  const slug = "e2e-" + Date.now().toString(36);
  await page.fill("#slug", slug); await page.fill("#target", "/join"); await page.fill("#campaign", "e2e");
  await page.getByRole("button", { name: "Add code" }).click();
  await page.waitForURL(/saved=/);
  let r = await page.request.get(BASE + "/dynamicqr/" + slug, { maxRedirects: 0 });
  assert.equal(r.headers().location, "/join");
  await page.goto(BASE + "/admin/qr/" + slug);
  await page.fill("#target", "https://avirobotics.org/sponsors"); await page.getByRole("button", { name: "Save" }).click();
  await page.waitForURL(/saved=/);
  r = await page.request.get(BASE + "/dynamicqr/" + slug, { maxRedirects: 0 });
  assert.equal(r.headers().location, "https://avirobotics.org/sponsors");
  assert.match(await page.locator(`[data-qr-row="${slug}"]`).innerText(), /\b1\b/, "scan counted");
});

await test("countdowns page renders from content/countdowns.md", async () => {
  await page.goto(BASE + "/countdown");
  assert.ok(await page.locator(".countdown").count() > 0);
  assert.match(await page.locator("main").innerText(), /MATE ROV World Championship/);
});

await test("timeline moments can have several photos (gallery in the pop-up)", async () => {
  await page.goto(BASE + "/history");
  const open = page.locator(".timeline__open").last();
  const sub = page.locator("#" + (await open.getAttribute("data-sub")));
  await open.click();
  await sub.locator(".subtl__card", { hasText: "Techs on Decks" }).click();
  const dlg = page.locator("dialog.moment-sheet[open]");
  await dlg.waitFor({ state: "visible" });
  assert.equal(await dlg.locator(".moment-sheet__gallery .photo").count(), 5);
  await page.keyboard.press("Escape");
});

await test("dynamic QR stats: weekday, hour and device breakdown", async () => {
  await page.request.get(BASE + "/dynamicqr/three", { maxRedirects: 0, headers: { "user-agent": "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)" } });
  await page.goto(BASE + "/admin/qr/three");
  assert.match(await page.locator(".qr-insight").innerText(), /^Busiest: \w+days, around/);
  assert.match(await page.locator(".qr-devices").innerText(), /iPhone\/iPad/);
});

await test("team login sees only its own QR codes, under its own prefix", async () => {
  const tc = await browser.newContext(); const tp = await tc.newPage();
  await tp.goto(BASE + "/admin/login");
  await tp.click("text=Use a shared password instead");
  await tp.getByLabel("Password").fill(process.env.TEAM_PASSWORD ?? "sms-dev-password");
  await tp.getByRole("button", { name: "Sign in with password" }).click();
  await tp.waitForURL(BASE + "/admin/qr");
  assert.equal(await tp.locator('nav a[href="/admin"]').count(), 0, "no orders link for teams");
  assert.equal(await tp.locator('[data-qr-row="one"]').count(), 0, "club codes hidden");
  assert.equal((await tp.goto(BASE + "/admin/qr/one")).status(), 404, "club code edit blocked");
  await tp.goto(BASE + "/admin/qr");
  assert.equal(await tp.inputValue("#slug"), "avi-sms-", "the name starts filled in with the team's prefix");
  const name = "flyer-" + Date.now().toString(36);
  await tp.fill("#slug", name); await tp.fill("#target", "/join"); await tp.fill("#campaign", "e2e");
  await tp.getByRole("button", { name: "Add code" }).click();
  await tp.waitForURL(/saved=/);
  assert.equal(await tp.locator(`[data-qr-row="avi-sms-${name}"]`).count(), 1, "a name typed without the prefix gets it added");
  await tc.close();
});

await test("QR maker: event and location codes, PDF, saved looks (opt-in storage only)", async () => {
  const qc = await browser.newContext({ acceptDownloads: true }); const qp = await qc.newPage();
  await qp.goto(BASE + "/tools/qr");
  assert.deepEqual(await qp.evaluate(() => Object.keys(localStorage)), [], "nothing stored on a plain visit");
  await qp.selectOption("#qr-kind", "event");
  await qp.fill("#qr-evtitle", "General meeting"); await qp.fill("#qr-evstart", "2026-12-04T17:30");
  await qp.locator("[data-qr-preview] svg").waitFor();
  assert.equal(await qp.locator('[data-qr-download="png"]').isDisabled(), false);
  await qp.selectOption("#qr-kind", "geo"); await qp.fill("#qr-lat", "36.8135"); await qp.fill("#qr-lng", "-119.7454");
  assert.equal(await qp.locator('[data-qr-download="png"]').isDisabled(), false);
  const [pdf] = await Promise.all([qp.waitForEvent("download"), qp.click('[data-qr-download="pdf"]')]);
  assert.match(pdf.suggestedFilename(), /\.pdf$/);
  assert.match((await (await import("node:fs/promises")).readFile(await pdf.path())).subarray(0, 5).toString(), /%PDF-/);
  await qp.click(".qr-custom > summary");
  await qp.fill("[data-qr-savename]", "Club blue"); await qp.click("[data-qr-save]");
  assert.equal(await qp.locator("[data-qr-use]").count(), 1);
  assert.deepEqual(await qp.evaluate(() => Object.keys(localStorage)), ["avi-qr-saved"], "a saved look is the only thing kept");
  await qp.check("[data-qr-remember]");
  await qp.reload(); assert.equal(await qp.locator("[data-qr-remember]").isChecked(), true);
  await qp.uncheck("[data-qr-remember]");
  assert.equal(await qp.evaluate(() => localStorage.getItem("avi-qr-look")), null, "unticking forgets");
  await qp.click("[data-qr-del]"); assert.equal(await qp.locator("[data-qr-use]").count(), 0);
  await qc.close();
});

await test("videos load from YouTube only after pressing play; privacy page is linked", async () => {
  await page.goto(BASE + "/sms/2026");
  assert.equal(await page.locator("iframe").count(), 0, "no YouTube frame before play");
  assert.equal(await page.locator("a a").count(), 0, "no nested links");
  await page.locator("[data-video-play]").first().click();
  assert.equal(await page.locator('iframe[src^="https://www.youtube-nocookie.com/embed/"]').count(), 1);
  await page.goto(BASE + "/");
  await page.locator('footer a[href="/privacy"]').click();
  await page.waitForURL(BASE + "/privacy");
  assert.match(await page.locator("main").innerText(), /avi_cart[\s\S]*avi_admin/);
});

await test("tool usage: anonymous counts from a real browser, link previews ignored, officers see totals", async () => {
  const uc = await browser.newContext({ userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130 Safari/537.36" });
  const up = await uc.newPage();
  await up.goto(BASE + "/tools/qr?url=https://avirobotics.org/join");
  await up.locator("[data-qr-preview] svg").waitFor();
  const beacon = up.waitForRequest((r) => r.url().endsWith("/api/use/qr.svg") && r.method() === "POST");
  await Promise.all([up.waitForEvent("download"), up.click('[data-qr-download="svg"]')]);
  await beacon;
  assert.deepEqual(await uc.cookies(), [], "counting sets no cookies");
  await uc.close();
  await page.request.get(BASE + "/tools/qr", { headers: { "user-agent": "Discordbot/2.0" } });
  await page.goto(BASE + "/admin/usage");
  const row = (label) => page.locator("tr", { has: page.locator("th", { hasText: label }) }).locator("td").nth(3).innerText();
  assert.ok(+(await row("QR maker opened")) >= 1);
  assert.ok(+(await row("QR: SVG downloads")) >= 1);
});

await test("QR maker works by keyboard: tab rows use arrow keys, preview says what it opens", async () => {
  await page.goto(BASE + "/tools/qr?url=https://avirobotics.org/join");
  await page.locator("[data-qr-preview] svg").waitFor();
  assert.equal(await page.locator("[data-qr-preview]").getAttribute("aria-label"), "QR code for https://avirobotics.org/join");
  assert.equal(await page.locator("#qr-mode-bulk").getAttribute("tabindex"), "-1", "one Tab stop per tab row");
  await page.focus("#qr-mode-one"); await page.keyboard.press("ArrowRight");
  assert.equal(await page.evaluate(() => document.activeElement.id), "qr-mode-bulk");
  assert.equal(await page.locator("#qr-bulk").isVisible(), true, "arrow selects the tab");
  await page.keyboard.press("Home");
  assert.equal(await page.locator("#qr-one").isVisible(), true);
  await page.click(".qr-custom > summary");
  await page.focus('[data-qr-tab][aria-selected="true"]'); await page.keyboard.press("End");
  assert.equal(await page.evaluate(() => document.activeElement.dataset.qrTab), "size");
  assert.equal(await page.locator('[data-qr-panel="size"]').isVisible(), true);
});

await test("accounts: organizations with sub-teams, bulk add, leads manage everything below them, removal is immediate", async () => {
  const as = async (email) => {
    const c = await browser.newContext(); const pg = await c.newPage();
    await pg.goto(BASE + "/admin/login");
    await pg.selectOption('select[name="email"]', email);
    await pg.getByRole("button", { name: "Sign in (dev only)" }).click();
    await pg.waitForLoadState();
    return [c, pg];
  };
  const id = Date.now().toString(36).slice(-5);
  const er = "er" + id, cd = "cd" + id, el = "el" + id;
  const erLead = `erlead-${id}@example.com`, cdLead = `cdlead-${id}@example.com`, m1 = `m1-${id}@example.com`, m2 = `m2-${id}@example.com`;
  const addTeam = async (pg, parent, name, tid) => {
    await pg.click("text=Add a team");
    await pg.selectOption("#g-parent", parent); await pg.fill("#g-name", name); await pg.fill("#g-id", tid);
    await pg.getByRole("button", { name: "Add team" }).click();
    assert.match(await pg.locator("main").innerText(), /Team added/);
  };
  const addPeople = async (pg, list, team, role = "Member") => {
    await pg.fill("#add-list", list); await pg.selectOption("#add-team", team); await pg.selectOption("#add-role", role);
    // Wait for this save's page, not an alert left over from the last one.
    await Promise.all([pg.waitForResponse((r) => r.request().method() === "POST" && new URL(r.url()).pathname === "/admin/people"),
      pg.getByRole("button", { name: "Add", exact: true }).click()]);
    await pg.waitForLoadState();
  };

  const [oc, op] = await as("owner@example.com");
  assert.equal(new URL(op.url()).pathname, "/admin", "owners land on the officer dashboard");
  await op.goto(BASE + "/admin/people");
  assert.equal(await op.locator('[data-team="sms"]').count(), 1, "SMS is a team under AVI Robotics");
  await addTeam(op, "", "Engineering Renaissance " + id, er);
  await addTeam(op, er, "Crush Depth " + id, cd);

  // A mistake keeps what was typed.
  await op.fill("#add-list", `typo-${id}@example.com, Kept Name`); await op.selectOption("#add-team", "");
  await op.getByRole("button", { name: "Add", exact: true }).click();
  assert.match(await op.locator("main").innerText(), /Pick a team/);
  assert.equal(await op.inputValue("#add-list"), `typo-${id}@example.com, Kept Name`, "list kept after an error");

  // Bulk: good lines are added, bad ones stay in the box.
  await addPeople(op, `${erLead}, ER Lead`, er, "Lead");
  await addPeople(op, `${cdLead}`, cd, "Lead");
  await addPeople(op, `${m1}, Member One\nMember Two <${m2}>\nnot-an-email`, cd);
  assert.match(await op.locator("main").innerText(), /Added 2 new people to Crush Depth[\s\S]*Skipped 1/);
  assert.equal(await op.inputValue("#add-list"), "not-an-email", "only the bad line is left");
  await addPeople(op, cdLead, er); // same person, second team
  const chips = await op.locator(`[data-person="${cdLead}"] .team-chip`).allInnerTexts();
  assert.ok(chips.some((c) => c.includes(`Crush Depth ${id} · Lead`)) && chips.some((c) => c.startsWith(`Engineering Renaissance ${id}`) && !c.includes("Lead")), "one person, two teams, two roles: " + chips.join(" | "));

  // The ER lead manages everything under ER, including sub-teams they create.
  const [lc, lp] = await as(erLead);
  await lp.goto(BASE + "/admin/people");
  assert.equal(await lp.locator('[data-person="owner@example.com"]').count(), 0, "leads only see their teams' people");
  assert.ok(await lp.locator(`[data-person="${m1}"]`).count() === 1, "…including sub-teams");
  await addTeam(lp, cd, "Electrical " + id, el); // leads add sub-teams below the teams they lead…
  assert.equal(await lp.locator('#g-parent option[value=""]').count(), 0, "…but only the owner makes organizations");
  await addPeople(lp, m1, el, "Lead");

  // An officer sees every team but can't add or remove teams.
  const officer = `off-${id}@example.com`;
  await op.goto(BASE + "/admin/people");
  await op.fill("#add-list", officer); await op.selectOption("#add-team", ""); await op.check(".people-admin__officer input");
  await op.getByRole("button", { name: "Add", exact: true }).click(); await op.locator(".alert").first().waitFor();
  assert.equal(await op.locator(".people-admin__officer input").isChecked(), false, "the officer box never carries over to the next add");
  const [ofc, ofp] = await as(officer);
  await ofp.goto(BASE + "/admin/people");
  assert.equal(await ofp.locator(`[data-team="${el}"]`).count(), 0, "officers don't see inside other organizations");
  const summary = ofp.locator(`[data-org="${er}"]`);
  assert.match(await summary.innerText(), /Led by ER Lead[\s\S]*2 teams · \d+ people · \d+ QR codes?/, "only who leads it and the totals");
  assert.equal(await ofp.locator(`[data-person="${m1}"]`).count(), 0, "partner people stay private");
  assert.equal(await ofp.locator('[data-team="sms"]').count(), 1, "AVI's own teams are visible");
  assert.equal(await ofp.locator("text=Add a team").count(), 0, "officers don't add teams");
  assert.equal(await ofp.locator(".team-tree__remove").count(), 0, "officers don't remove teams");
  await ofp.goto(BASE + "/admin/qr?group=" + cd);
  assert.equal(await ofp.locator(`[data-qr-row^="${er}-"]`).count(), 0, "partner QR codes stay private");
  await ofp.goto(BASE + "/admin/people");
  const csrfForm = await ofp.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const forced = await ofp.request.post(BASE + "/admin/people/team/delete", { form: { id: el, __RequestVerificationToken: csrfForm }, maxRedirects: 0 });
  assert.match(forced.headers().location, /error=denied/, "not even by posting directly");
  await ofc.close();

  // The organization's top lead acts as its owner: a Crush Depth lead can't change them, even on Crush Depth.
  await addPeople(op, erLead, cd); // ER lead is also a member of Crush Depth
  assert.doesNotMatch(await op.locator(`[data-person="${erLead}"] th`).innerText(), /officer/i, "adding someone after promoting an officer doesn't promote them too");
  const [cc, cp] = await as(cdLead);
  await cp.goto(BASE + "/admin/people?team=" + cd);
  assert.equal(await cp.locator(`[data-person="${erLead}"] .team-chip__x`).count(), 0, "can't take the org lead off a sub-team");
  assert.equal(await cp.locator(`[data-person="${erLead}"] .people-admin__edit`).count(), 0, "can't change the org lead's role");
  const tok = await cp.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const forced2 = await cp.request.post(BASE + "/admin/people/team/remove", { form: { email: erLead, group: cd, __RequestVerificationToken: tok }, maxRedirects: 0 });
  assert.match(forced2.headers().location, /error=denied/, "not even by posting directly");

  // Members: own codes only; leads above see them.
  const [mc, mp] = await as(m2);
  assert.equal(new URL(mp.url()).pathname, "/admin/hours", "members land on hours");
  await mp.goto(BASE + "/admin/qr");
  assert.equal(await mp.inputValue("#slug"), `${er}-${cd}-`, "sub-team codes start with the whole chain: organization, then team");
  await mp.fill("#slug", `${er}-${cd}-`); await mp.fill("#target", "/join"); await mp.fill("#campaign", "e2e");
  await mp.getByRole("button", { name: "Add code" }).click();
  assert.match(await mp.locator("main").innerText(), /then a name/, "a prefix alone isn't a name");
  await mp.fill("#slug", `${er}-${cd}-flyer`); await mp.fill("#target", "/join"); await mp.fill("#campaign", "e2e");
  await mp.getByRole("button", { name: "Add code" }).click();
  await mp.waitForURL(/saved=/);
  const [m1c, m1p] = await as(m1);
  await m1p.goto(BASE + "/admin/qr");
  assert.equal(await m1p.locator(`[data-qr-row="${er}-${cd}-flyer"]`).count(), 0, "members don't see each other's codes");
  await m1p.goto(BASE + "/admin/people"); assert.equal(new URL(m1p.url()).pathname, "/admin/people", "m1 leads Electrical, so they get People");
  await mp.goto(BASE + "/admin/people"); assert.match(mp.url(), /\/admin\/login/, "plain members can't manage people");
  for (const pg of [lp, cp]) { await pg.goto(BASE + "/admin/qr"); assert.equal(await pg.locator(`[data-qr-row="${er}-${cd}-flyer"]`).count(), 1, "leads above see team codes"); }

  // Taking someone off their only team removes them, and they're out on their next click.
  await lp.goto(BASE + "/admin/people?team=" + cd);
  await lp.locator(`[data-person="${m2}"] .team-chip__x`).click();
  assert.match(await lp.locator("main").innerText(), /Removed/);
  await mp.goto(BASE + "/admin/qr"); assert.match(mp.url(), /\/admin\/login/, "removed member is out");
  // Only empty teams can be removed, and only by the owner.
  await op.goto(BASE + "/admin/people");
  assert.equal(await op.locator(`[data-team="${cd}"] .team-tree__remove`).count(), 0, "a team with people can't be removed");
  await addTeam(lp, er, "Scratch " + id, "sc" + id);
  await lp.locator(`[data-team="sc${id}"] .team-tree__remove button`).click(); // the ER lead removes their own sub-team
  assert.match(await lp.locator("main").innerText(), /Team removed/);
  await op.goto(BASE + "/admin/people");
  assert.equal(await op.locator(`[data-team="sc${id}"]`).count(), 0);
  for (const c of [oc, lc, cc, mc, m1c]) await c.close();
});

await test("accounts: the owner can also be on teams (e.g. lead of SMS); co-leads are allowed", async () => {
  const c = await browser.newContext(); const pg = await c.newPage();
  await pg.goto(BASE + "/admin/login"); await pg.selectOption('select[name="email"]', "owner@example.com"); await pg.click("text=Sign in (dev only)");
  await pg.waitForURL((u) => !u.pathname.startsWith("/admin/login")); // signed in before moving on
  await pg.goto(BASE + "/admin/people");
  const co = `co-${Date.now().toString(36)}@example.com`;
  await pg.fill("#add-list", `owner@example.com\n${co}`); await pg.selectOption("#add-team", "sms"); await pg.selectOption("#add-role", "Lead");
  await pg.getByRole("button", { name: "Add", exact: true }).click(); await pg.locator(".alert").first().waitFor();
  assert.match(await pg.locator('[data-person="owner@example.com"]').innerText(), /Sequoia Marine Solutions · Lead/i, "owner leads SMS");
  assert.match(await pg.locator('[data-person="owner@example.com"]').innerText(), /owner/i, "and is still the owner");
  assert.match(await pg.locator('[data-team="sms"]').innerText(), /led by .*owner@example\.com.*,|led by .*,.*owner@example\.com/, "two co-leads");
  await c.close();
});

await test("team prefixes follow the whole chain, swap with the team picker; People 'Everyone' shows everyone", async () => {
  const c = await browser.newContext(); const pg = await c.newPage();
  await pg.goto(BASE + "/admin/login"); await pg.selectOption('select[name="email"]', "owner@example.com"); await pg.click("text=Sign in (dev only)");
  await pg.waitForURL((u) => !u.pathname.startsWith("/admin/login")); // signed in before moving on
  await pg.goto(BASE + "/admin/people?q=&team=");
  assert.ok(await pg.locator("tbody tr[data-person]").count() > 0, "an empty team filter means everyone, not nobody");
  const sub = "sub" + Date.now().toString(36).slice(-5);
  if (await pg.locator(`[data-team="${sub}"]`).count() === 0) {
    await pg.click("text=Add a team"); await pg.selectOption("#g-parent", "sms"); await pg.fill("#g-name", "Electrical " + sub); await pg.fill("#g-id", sub);
    await pg.getByRole("button", { name: "Add team" }).click();
  }
  assert.match(await pg.locator(`[data-team="${sub}"]`).innerText(), new RegExp(`avi-sms-${sub}-`), "Electrical in SMS in AVI → avi-sms-electrical-");
  await pg.goto(BASE + "/admin/qr");
  await pg.selectOption("#owner", "sms");
  assert.equal(await pg.inputValue("#slug"), "avi-sms-");
  await pg.fill("#slug", "avi-sms-pool-day");
  await pg.selectOption("#owner", sub);
  assert.equal(await pg.inputValue("#slug"), `avi-sms-${sub}-pool-day`, "changing the team swaps the prefix and keeps the name");
  await c.close();
});

await test("Google sign-in refuses bad callbacks; unknown emails can't use the dev picker", async () => {
  assert.match((await page.request.get(BASE + "/admin/google/callback?code=x&state=y", { maxRedirects: 0 })).headers().location, /error=/);
  const r = await page.request.get(BASE + "/admin/google", { maxRedirects: 0 });
  assert.match(r.headers().location, /error=google-off/, "button is off until Admin:Google is set");
});

await test("dynamic QR: delete needs a tick, old prints go home, and the name can't be reused", async () => {
  await page.goto(BASE + "/admin/qr");
  const slug = "e2e-del-" + Date.now().toString(36);
  await page.fill("#slug", slug); await page.fill("#target", "/join"); await page.fill("#campaign", "e2e");
  await page.getByRole("button", { name: "Add code" }).click(); await page.waitForURL(/saved=/);
  await page.goto(BASE + "/admin/qr/" + slug);
  await page.click("text=Delete this code");
  await page.getByRole("button", { name: "Delete " + slug }).click();
  assert.match(await page.locator("main").innerText(), /Tick the box first/);
  await page.check(".qr-delete input[type=checkbox]");
  await page.getByRole("button", { name: "Delete " + slug }).click();
  await page.waitForURL(/deleted=/);
  assert.equal(await page.locator(`[data-qr-row="${slug}"]`).count(), 0, "gone from the list");
  const r = await page.request.get(BASE + "/dynamicqr/" + slug, { maxRedirects: 0 });
  assert.equal(r.headers().location, "/", "old prints land on the home page");
  assert.equal((await page.goto(BASE + "/admin/qr/" + slug)).status(), 404);
  await page.goto(BASE + "/admin/qr");
  await page.fill("#slug", slug); await page.fill("#target", "https://example.com"); await page.fill("#campaign", "e2e");
  await page.getByRole("button", { name: "Add code" }).click();
  assert.match(await page.locator("main").innerText(), /is taken/, "can't re-point old prints");
});

await test("dynamic QR list: search, group, status and sort filters live in the URL", async () => {
  await page.goto(BASE + "/admin/qr");
  const total = await page.locator("[data-qr-row]").count();
  await page.fill("#f-q", "discord"); await page.getByRole("button", { name: "Show" }).click();
  await page.waitForURL(/q=discord/);
  assert.equal(await page.locator("[data-qr-row]").count(), 1, "search matches the link");
  assert.match(await page.locator(".qr-filter__count").innerText(), new RegExp(`Showing 1 of ${total}`));
  await page.goto(BASE + "/admin/qr?group=club&sort=name");
  const names = await page.locator("[data-qr-row]").evaluateAll((rows) => rows.map((r) => r.dataset.qrRow));
  assert.ok(names.length > 0 && names.every((n) => !n.startsWith("sms-")), "club-wide only");
  assert.deepEqual(names, [...names].sort(), "sorted by name");
  await page.goto(BASE + "/admin/qr?q=zzz-nothing");
  assert.match(await page.locator("main").innerText(), /No codes match/);
});

await test("dynamic QR looks: save from the maker, reopen with it; strangers can't save", async () => {
  await page.goto(BASE + "/admin/qr/three");
  const href = await page.locator(".qr-lookline a").getAttribute("href");
  assert.match(href, /code=three/);
  await page.goto(BASE + href);
  const bar = page.locator("[data-qr-codebar]");
  await bar.waitFor({ state: "visible" });
  await page.click('[data-qr-theme="sunset"]');
  await page.click("[data-qr-codesave]");
  await page.waitForFunction(() => document.querySelector("[data-qr-codehint]").textContent.startsWith("Saved"));
  await page.goto(BASE + "/tools/qr?url=x&style=avi&code=three");
  await page.waitForFunction(() => document.querySelector("[data-qr-codehint]").textContent.includes("saved look"));
  assert.equal(await page.inputValue('[data-qr="fg"]'), "#b0124f", "saved look beats ?style=");
  await page.goto(BASE + "/admin/qr");
  assert.match(await page.locator('[data-qr-row="three"]').innerText(), /look saved/i);

  const anon = await browser.newContext(); const ap = await anon.newPage();
  await ap.goto(BASE + "/tools/qr?code=three");
  await ap.locator("[data-qr-codebar]").waitFor({ state: "visible" });
  assert.equal(await ap.locator("[data-qr-codesave]").isVisible(), false, "no save button when signed out");
  assert.match(await ap.locator("[data-qr-codehint]").innerText(), /Sign in/);
  await anon.close();
  const forged = await page.request.post(BASE + "/admin/qr/three/look", { data: { look: { fg: "#000000" } }, headers: { Origin: "https://evil.example" } });
  assert.equal(forged.status(), 400, "other sites can't save looks");
});

// ---------- Hours ----------
// A few hours tests need a session that started over a day ago; nobody can make one through the site, so the test
// writes it straight into the database (Node's built-in SQLite). Where the site keeps it depends on how it was started.
async function siteDb() {
  const { DatabaseSync } = await import("node:sqlite");
  const fs = await import("node:fs"); const path = await import("node:path");
  const here = path.dirname(new URL(import.meta.url).pathname);
  const candidates = [process.env.SITE_DB, path.join(here, "../../App_Data/site.db"), path.join(here, "../../src/AviSite.Web/App_Data/site.db")].filter((f) => f && fs.existsSync(f));
  candidates.sort((a, b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
  const db = new DatabaseSync(candidates[0]); db.exec("PRAGMA busy_timeout = 5000"); return db;
}
const signIn = async (email) => {
  const c = await browser.newContext({ viewport: { width: 1280, height: 900 } }); const pg = await c.newPage();
  await pg.goto(BASE + "/admin/login"); await pg.selectOption('select[name="email"]', email); await pg.click("text=Sign in (dev only)");
  await pg.waitForLoadState(); return [c, pg];
};
const addPerson = async (pg, list, team, role = "Member") => {
  await pg.goto(BASE + "/admin/people");
  await pg.fill("#add-list", list); await pg.selectOption("#add-team", team); await pg.selectOption("#add-role", role);
  await Promise.all([pg.waitForResponse((r) => r.request().method() === "POST" && new URL(r.url()).pathname === "/admin/people"), pg.getByRole("button", { name: "Add", exact: true }).click()]);
};

await test("hours: clocking in and out each take two taps; one session at a time; /hours links pre-pick team and category", async () => {
  const id = Date.now().toString(36).slice(-5), who = `clock-${id}@example.com`;
  const [oc, op] = await signIn("owner@example.com");
  await addPerson(op, who, "sms");
  const [c, pg] = await signIn(who);
  assert.equal(new URL(pg.url()).pathname, "/admin/hours", "members land on hours");
  await pg.goto(BASE + "/hours?team=sms&category=Outreach");
  assert.equal(await pg.inputValue("[data-clock-team]"), "sms");
  assert.equal(await pg.locator('[data-clock-cat][value="Outreach"]').isChecked(), true, "the poster link picked the category");
  await pg.click(".clock__confirm > summary"); // tap 1
  assert.equal(await pg.locator(".clock--on").count(), 0, "one tap doesn't clock you in");
  assert.match(await pg.locator("[data-clock-what]").innerText(), /Sequoia Marine Solutions · Outreach/);
  await pg.click("text=Yes, clock in"); // tap 2
  await pg.locator(".clock--on").waitFor();
  const tok = await pg.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const again = await pg.request.post(BASE + "/admin/hours/in", { form: { team: "sms", category: "Build", __RequestVerificationToken: tok }, maxRedirects: 0 });
  assert.match(again.headers().location, /error=already-in/, "can't start a second session");
  await pg.click(".clock__confirm > summary"); // tap 1
  assert.equal(await pg.locator(".clock--on").count(), 1, "one tap doesn't clock you out");
  await pg.fill('input[name="note"]', "e2e work"); await pg.click("text=Yes, clock out"); // tap 2
  await pg.locator(".clock__confirm > summary", { hasText: "Clock in" }).waitFor();
  assert.match(await pg.locator(".entry").first().innerText(), /Outreach[\s\S]*e2e work/);
  await c.close(); await oc.close();
});

await test("hours: members add, edit and delete their own time; bad times are refused; others' entries are off limits", async () => {
  const id = Date.now().toString(36).slice(-5), a = `ha-${id}@example.com`, b = `hb-${id}@example.com`;
  const [oc, op] = await signIn("owner@example.com");
  await addPerson(op, `${a}\n${b}`, "sms");
  const [c, pg] = await signIn(a);
  const add = async (date, start, end) => {
    await pg.goto(BASE + "/admin/hours"); await pg.click("summary:has-text('Add time by hand')");
    const f = pg.locator("details[open] form.hours-form").first();
    await f.locator('input[name="date"]').fill(date); await f.locator('input[name="start"]').fill(start); await f.locator('input[name="end"]').fill(end);
    await f.locator('select[name="category"]').selectOption("Build");
    await f.locator('button[type="submit"]').click(); await pg.waitForLoadState();
    return pg.locator(".alert").first().innerText();
  };
  assert.match(await add("2026-10-01", "17:30", "20:00"), /Added/);
  assert.match(await add("2030-01-01", "10:00", "11:00"), /future/i);
  assert.match(await add("2026-10-02", "22:00", "01:30"), /Added/, "past midnight is fine");
  const first = pg.locator(".entry", { hasText: "Thu, Oct 1" });
  assert.match(await first.innerText(), /2:30:00/);
  await first.locator(".entry__edit > summary").click();
  await first.locator('input[name="end"]').fill("21:00"); await first.locator('button', { hasText: "Save" }).click(); await pg.waitForLoadState();
  assert.match(await pg.locator(".entry", { hasText: "Thu, Oct 1" }).innerText(), /3:30:00/, "edited");
  const myId = await pg.locator(".entry", { hasText: "Thu, Oct 1" }).locator('input[name="id"]').first().inputValue();
  await pg.locator(".entry", { hasText: "Fri, Oct 2" }).locator(".entry__edit > summary").click();
  await pg.locator(".entry", { hasText: "Fri, Oct 2" }).locator(".hours-form__delete summary").click();
  await pg.locator(".entry", { hasText: "Fri, Oct 2" }).locator("text=Yes, delete this entry").click(); await pg.waitForLoadState();
  assert.equal(await pg.locator(".entry", { hasText: "Fri, Oct 2" }).count(), 0, "deleted");
  // Another member can't touch it.
  const [bc, bp] = await signIn(b);
  const tok = await bp.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const r = await bp.request.post(BASE + "/admin/hours/delete", { form: { id: myId, __RequestVerificationToken: tok }, maxRedirects: 0 });
  assert.match(r.headers().location, /error=denied/, "members can't delete each other's hours");
  for (const x of [c, bc, oc]) await x.close();
});

await test("hours: leads see their team, fix anyone's time, end forgotten sessions; >24 h sessions warn on every admin page", async () => {
  const id = Date.now().toString(36).slice(-5), lead = `hl-${id}@example.com`, m = `hm-${id}@example.com`, gone = `hg-${id}@example.com`;
  const [oc, op] = await signIn("owner@example.com");
  await addPerson(op, lead, "sms", "Lead");
  await addPerson(op, `${m}, Hours Member\n${gone}, Forgot Clockout`, "sms");
  // m clocks in normally; gone clocked in 30 hours ago and never left.
  const [mc, mp] = await signIn(m);
  await mp.goto(BASE + "/hours?team=sms&category=Build"); await mp.click(".clock__confirm > summary"); await mp.click("text=Yes, clock in");
  const db = await siteDb();
  const t = new Date(Date.now() - 30 * 3600e3).toISOString();
  db.prepare("INSERT INTO hours (email, team, category, start_utc, source, created_utc, updated_utc) VALUES (?, 'sms', 'Build', ?, 'clock', ?, ?)").run(gone, t, t, t);
  db.close();
  const [lc, lp] = await signIn(lead);
  await lp.goto(BASE + "/admin/qr");
  const banner = lp.locator(".long-sessions");
  assert.match(await banner.innerText(), /Forgot Clockout · Sequoia Marine Solutions/, "the warning is on every admin page");
  assert.match(await banner.locator('a[href^="mailto:"]').first().getAttribute("href"), new RegExp(gone.replace(/[.@-]/g, "\\$&")));
  await banner.locator("text=Set the end time").first().click();
  const fix = lp.locator("#fix");
  await fix.locator('input[name="end"]').fill("23:00"); // the day they started
  await fix.locator("text=End session").click(); await lp.waitForLoadState();
  assert.match(await lp.locator(".alert").first().innerText(), /Session ended/);
  assert.equal(await lp.locator(".long-sessions").count(), 0, "warning gone once it's fixed");
  assert.match(await lp.locator("[data-running]").first().innerText(), /Hours Member/, "who's clocked in right now");
  // The lead adds time for someone.
  await lp.click("summary:has-text('Add time for someone')");
  const f = lp.locator("details.people-admin__more[open] form.hours-form");
  await f.locator('select[name="email"]').selectOption(m);
  await f.locator('input[name="date"]').fill("2026-10-03"); await f.locator('input[name="start"]').fill("09:00"); await f.locator('input[name="end"]').fill("12:00");
  await f.locator('button[type="submit"]').click(); await lp.waitForLoadState();
  assert.match(await lp.locator(".alert").first().innerText(), /Added/);
  await mp.goto(BASE + "/admin/hours");
  assert.match(await mp.locator(".entry", { hasText: "Sat, Oct 3" }).innerText(), /added by a lead/, "the member sees who added it");
  // Members don't get the team page.
  await mp.goto(BASE + "/admin/hours/team"); assert.equal(new URL(mp.url()).pathname, "/admin/hours");
  for (const x of [oc, mc, lc]) await x.close();
});

await test("hours: report with charts and CSV; leaderboard is each organization's choice", async () => {
  const [oc, op] = await signIn("owner@example.com");
  await op.goto(BASE + "/admin/hours/report?range=all");
  assert.ok(await op.locator(".colchart__bar").count() > 0, "hours-per-week chart");
  const assertPieMatchesReport = async () => {
    const pie = op.locator("[data-hours-pie]");
    assert.equal(await pie.count(), 1, "category pie chart");
    assert.ok(await pie.locator("[data-pie-slice]").count() > 0, "category legend is present");
    const chartSvg = pie.locator('svg[role="img"]');
    const titleId = await chartSvg.getAttribute("aria-labelledby");
    const descriptionId = await chartSvg.getAttribute("aria-describedby");
    assert.ok(titleId && descriptionId, "chart has an accessible name and description");
    assert.ok(await op.locator(`#${titleId}`).count() === 1 && await op.locator(`#${descriptionId}`).count() === 1, "accessible title and description resolve");
    const percentages = await pie.locator(".hours-pie__share").allTextContents();
    assert.ok(percentages.every((value) => /^(?:<0\.1|\d{1,3}(?:\.\d)?)%$/.test(value.trim())), `share labels are concise: ${percentages.join(", ")}`);
    const legendEntries = await pie.locator("[data-pie-slice]").evaluateAll((items) => items.map((item) => ({
      label: item.getAttribute("data-pie-label"), hours: Number(item.getAttribute("data-pie-hours")),
    })));
    const wedgeEntries = await pie.locator("[data-pie-wedge]").evaluateAll((items) => items.map((item) => ({
      label: item.getAttribute("data-pie-label"), hours: Number(item.getAttribute("data-pie-hours")),
      tag: item.tagName.toLowerCase(), path: item.getAttribute("d"),
    })));
    assert.equal(wedgeEntries.length, legendEntries.length, "every category has a rendered slice");
    assert.deepEqual(wedgeEntries.map(({ label, hours }) => ({ label, hours })), legendEntries, "slice data matches the complete legend");
    assert.ok(wedgeEntries.every(({ hours }) => Number.isFinite(hours) && hours > 0), "rendered slices have positive finite values");
    for (const { label, tag, path } of wedgeEntries) {
      if (tag !== "path") continue; // Single-category charts render as a full circle.
      const parts = path.trim().split(" ");
      assert.equal(parts.length, 8, `slice path grammar for ${label}`);
      const start = parts[1].slice(1).split(",").map(Number);
      const end = parts[6].split(",").map(Number);
      assert.ok(start.every(Number.isFinite) && end.every(Number.isFinite), `finite SVG points for ${label}`);
      assert.ok(start[0] !== end[0] || start[1] !== end[1], `non-degenerate SVG arc for ${label}`);
    }
    const renderedTotal = wedgeEntries.reduce((sum, item) => sum + item.hours, 0);
    const chartTotal = Number(await pie.getAttribute("data-total-hours"));
    const reportTotal = Number(await op.locator(".hours__totals").getAttribute("data-report-total-hours"));
    assert.ok(Number.isFinite(chartTotal) && Number.isFinite(reportTotal) && Math.abs(chartTotal - reportTotal) < 0.01, `pie total ${chartTotal} matches filtered report total ${reportTotal}`);
    assert.ok(Math.abs(renderedTotal - reportTotal) < 0.01, `rendered slices total ${renderedTotal} matches filtered report total ${reportTotal}`);
  };
  await assertPieMatchesReport();
  assert.ok(await op.locator(".report__block .hbars li").count() > 0, "team / person bars");
  assert.match(await op.locator(".report__matrix").innerText(), /Total/i);
  await op.goto(BASE + "/admin/hours/report?range=week");
  await assertPieMatchesReport(); // The pie is recomputed from the range-filtered report data.
  const csv = await op.request.get(BASE + "/admin/hours/export.csv?range=all");
  assert.equal(csv.status(), 200);
  assert.match((await csv.text()).split("\n")[0], /^name,email,organization,team,category,date,start,end,hours,note,source$/);
  // Leaderboard off by default; the owner turns it on for AVI; members of AVI teams see it.
  await op.goto(BASE + "/admin/hours/settings");
  const avi = op.locator('form[data-org="avi"]');
  await avi.locator('input[name="leaderboard"]').check(); await avi.locator("button", { hasText: "Save" }).click(); await op.waitForLoadState();
  const id = Date.now().toString(36).slice(-5), who = `lb-${id}@example.com`;
  await addPerson(op, who, "sms");
  const [c, pg] = await signIn(who);
  assert.match(await pg.locator(".board").innerText(), /AVI Robotics leaderboard/);
  await op.goto(BASE + "/admin/hours/settings");
  await op.locator('form[data-org="avi"] input[name="leaderboard"]').uncheck(); await op.locator('form[data-org="avi"] button', { hasText: "Save" }).click(); await op.waitForLoadState();
  await pg.reload(); assert.equal(await pg.locator(".board").count(), 0, "turned off again");
  // A member can't reach settings or someone else's CSV.
  assert.equal((await pg.request.get(BASE + "/admin/hours/export.csv")).status(), 404);
  await c.close(); await oc.close();
});

await test("reports: sponsor version has no names; real PDF; branding per organization, and per team where switched on", async () => {
  const [oc, op] = await signIn("owner@example.com");
  // Internal vs sponsor.
  await op.goto(BASE + "/admin/hours/report?range=all");
  assert.ok(await op.locator("[data-report-people]").count() > 0, "internal report lists people");
  await op.locator("[data-audience-sponsor]").click(); await op.waitForLoadState();
  assert.match(op.url(), /view=sponsor/);
  assert.equal(await op.locator("[data-report-people]").count(), 0, "sponsor report has no per-person sections");
  assert.equal(await op.locator(".report__matrix").count(), 0);
  assert.match(await op.locator("h1").innerText(), /Hours summary/);
  // The filter keeps the sponsor view.
  await op.click(".qr-filter button[type=submit]"); await op.waitForLoadState();
  assert.match(op.url(), /view=sponsor/, "changing the filter stays on the sponsor version");
  // The PDF is a real PDF, for both versions.
  for (const view of ["sponsor", "internal"]) {
    const pdf = await op.request.get(BASE + `/admin/hours/report.pdf?range=all&view=${view}`);
    assert.equal(pdf.status(), 200);
    assert.equal(pdf.headers()["content-type"], "application/pdf");
    const body = await pdf.body();
    assert.equal(body.subarray(0, 5).toString(), "%PDF-", `${view} PDF starts like a PDF`);
    assert.ok(body.subarray(body.length - 8).toString().includes("%%EOF"));
  }

  // Branding: AVI gets a tagline, footer and logo; SMS gets its own; contrast is checked.
  await op.goto(BASE + "/admin/branding");
  await op.fill("#tag-avi", "Smoke tagline");
  await op.fill("#footer-avi", "Smoke footer line");
  await op.locator('#brand-avi form[action="/admin/branding/avi"] button[type=submit]').click(); await op.waitForLoadState();
  assert.match(await op.locator(".alert").first().innerText(), /saved/i);
  const png = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAgAAAAECAYAAACzzX7wAAAAIElEQVR4XmNkYGBg4El+9h9EYwNM+CRBgAmXThggqAAABC4EsGclMTEAAAAASUVORK5CYII=", "base64");
  await op.setInputFiles("#logo-avi", { name: "logo.png", mimeType: "image/png", buffer: png });
  await op.locator('#brand-avi form[action$="/logo"] button', { hasText: "Upload logo" }).click(); await op.waitForLoadState();
  assert.match(await op.locator(".alert").first().innerText(), /Logo uploaded/);
  await op.setInputFiles("#logo-avi", { name: "fake.png", mimeType: "image/png", buffer: Buffer.from("not an image at all") });
  await op.locator('#brand-avi form[action$="/logo"] button', { hasText: "Upload logo" }).click(); await op.waitForLoadState();
  assert.match(await op.locator(".alert").first().innerText(), /PNG or JPEG/, "files are checked by content");
  const own = op.locator('form[action="/admin/branding/sms/own"] input[name=enabled][value=true]');
  if (await own.count()) { await own.locator("xpath=..").locator("button").click(); await op.waitForLoadState(); }
  assert.equal(await op.locator("#brand-sms").count(), 1, "SMS has its own branding card once switched on");
  await op.locator("#accent-sms").evaluate((e) => { e.value = "#eeeeee"; });
  await op.locator('#brand-sms form[action="/admin/branding/sms"] button[type=submit]').click(); await op.waitForLoadState();
  assert.match(await op.locator(".alert").first().innerText(), /too light/, "a chart color that can't be seen on white is refused");
  await op.fill("#name-sms", "Smoke SMS");
  await op.selectOption("#hfont-sms", "archivo-expanded"); await op.selectOption("#bfont-sms", "inter");
  await op.locator("#primary-sms").evaluate((e) => { e.value = "#0f6f69"; });
  await op.locator("#accent-sms").evaluate((e) => { e.value = "#1f9a92"; });
  await op.locator('#brand-sms form[action="/admin/branding/sms"] button[type=submit]').click(); await op.waitForLoadState();
  assert.match(await op.locator(".alert").first().innerText(), /saved/i);

  // Reports pick the right branding: SMS's own for SMS, AVI's for everything else.
  await op.goto(BASE + "/admin/hours/report?team=sms&range=all");
  assert.equal(await op.locator(".report__org").innerText(), "Smoke SMS");
  const style = await op.locator(".report").getAttribute("style");
  assert.match(style, /--brand: #0f6f69/);
  assert.match(style, /--brand-dark: #[0-9a-f]{6}/, "a dark-mode version of the brand color");
  assert.match(style, /--brand-body-font: "Brand Inter"/, "the brand's text font");
  assert.match(style, /--brand-heading-stretch: 125%/, "Archivo Expanded headings");
  assert.equal(await op.locator(".report__org").evaluate((e) => getComputedStyle(e).fontStretch), "125%");
  const smsPdf = await (await op.request.get(BASE + "/admin/hours/report.pdf?team=sms&range=all")).body();
  assert.ok(smsPdf.includes(Buffer.from("/BaseFont /AVI-inter_regular")) && smsPdf.includes(Buffer.from("/FontFile2")), "the PDF embeds the brand's fonts");
  await op.goto(BASE + "/admin/hours/report?team=avi&range=all");
  assert.equal(await op.locator(".report__org").innerText(), "AVI Robotics");
  assert.match(await op.locator(".report__tagline").innerText(), /Smoke tagline/);
  assert.match(await op.locator(".report__footer").innerText(), /Smoke footer line/);
  const logo = await op.locator(".report__logo").getAttribute("src");
  assert.equal((await op.request.get(BASE + logo)).headers()["content-type"], "image/png");
  const brandedPdf = await op.request.get(BASE + "/admin/hours/report.pdf?team=avi&range=all");
  assert.ok((await brandedPdf.body()).includes(Buffer.from("/Subtype /Image")), "the logo is in the PDF");

  // Members can't change branding.
  const id = Date.now().toString(36).slice(-5), who = `br-${id}@example.com`;
  await addPerson(op, who, "sms");
  const [mc, mp] = await signIn(who);
  await mp.goto(BASE + "/admin/branding");
  assert.equal(await mp.locator(".branding-admin__card").count(), 0, "members see no branding to edit");
  // Turn SMS back to AVI's branding so other tests see the default.
  await op.goto(BASE + "/admin/branding");
  await op.locator('form[action="/admin/branding/sms/own"] button').click(); await op.waitForLoadState();
  assert.equal(await op.locator("#brand-sms").count(), 0);
  await mc.close(); await oc.close();
});

// ---------- Layout checks: catch "looks a little off" bugs (off-center icons, buttons jammed together) ----------
// Looks at the real pixels: finds the icon's ink inside a button and checks its middle is the button's middle.
const lens = await (await browser.newContext({ deviceScaleFactor: 1 })).newPage(); // about:blank, no CSP, for pixel math
async function iconOffset(pg, clip, inset = 0.22) {
  const png = await pg.screenshot({ clip, scale: "device" });
  return lens.evaluate(async ([b64, inset]) => {
    const img = new Image(); img.src = "data:image/png;base64," + b64; await img.decode();
    const c = document.createElement("canvas"); c.width = img.width; c.height = img.height;
    const x = c.getContext("2d"); x.drawImage(img, 0, 0);
    const { data, width: w, height: h } = x.getImageData(0, 0, img.width, img.height);
    const i0 = Math.round(w * inset), j0 = Math.round(h * inset), i1 = w - i0, j1 = h - j0;
    const at = (i, j) => { const k = (j * w + i) * 4; return [data[k], data[k + 1], data[k + 2]]; };
    const ref = at(i0, Math.round(h / 2)); // the button's own fill, just inside its edge
    let minX = 1e9, minY = 1e9, maxX = -1, maxY = -1;
    for (let j = j0; j < j1; j++) for (let i = i0; i < i1; i++) {
      const [r, g, b] = at(i, j);
      if (Math.abs(r - ref[0]) + Math.abs(g - ref[1]) + Math.abs(b - ref[2]) > 90) { minX = Math.min(minX, i); maxX = Math.max(maxX, i); minY = Math.min(minY, j); maxY = Math.max(maxY, j); }
    }
    if (maxX < 0) return null;
    return { dx: (minX + maxX + 1) / 2 - w / 2, dy: (minY + maxY + 1) / 2 - h / 2, w };
  }, [png.toString("base64"), inset]);
}
async function assertIconCentered(pg, target, label, tol = 1) { // tol in device pixels: the check runs at 2×, so half a CSS pixel. Text glyphs (×, +) never pass this; draw icons with CSS.
  // target: a locator, or a function returning a viewport box (after scrolling it into view)
  const clip = typeof target === "function" ? await target() : (await target.scrollIntoViewIfNeeded(), await target.boundingBox());
  const o = await iconOffset(pg, clip);
  assert.ok(o, `${label}: no icon found`);
  assert.ok(Math.abs(o.dx) <= tol && Math.abs(o.dy) <= tol, `${label}: icon is off center by ${o.dx.toFixed(1)}px, ${o.dy.toFixed(1)}px`);
}
// No two buttons may overlap or sit closer than 4px (side by side or stacked). Segmented controls (tabs) are allowed to join.
async function assertButtonsBreathe(pg, label) {
  const bad = await pg.evaluate(() => {
    const els = [...document.querySelectorAll("button, .btn, summary.btn")].filter((e) => {
      const r = e.getBoundingClientRect(), cs = getComputedStyle(e);
      return r.width > 2 && r.height > 2 && cs.visibility !== "hidden" && !e.closest("[hidden], .visually-hidden, [role=tablist], .site-header, .nav-sheet, dialog:not([open])") && !e.classList.contains("visually-hidden");
    });
    const out = [];
    for (let a = 0; a < els.length; a++) for (let b = a + 1; b < els.length; b++) {
      const A = els[a], B = els[b];
      if (A.contains(B) || B.contains(A)) continue;
      const r = A.getBoundingClientRect(), q = B.getBoundingClientRect();
      const ox = Math.min(r.right, q.right) - Math.max(r.left, q.left), oy = Math.min(r.bottom, q.bottom) - Math.max(r.top, q.top);
      const name = (e) => (e.getAttribute("aria-label") || e.textContent || e.className).trim().replace(/\s+/g, " ").slice(0, 30);
      if ((ox > 0 && oy > 0) || (ox > 0 && -oy < 4) || (oy > 0 && -ox < 4)) {
        const rect = (q) => ({ x: Math.round(q.x), y: Math.round(q.y), right: Math.round(q.right), bottom: Math.round(q.bottom) });
        const path = (e) => {
          const parts = [];
          for (let n = e, i = 0; n && i < 5; n = n.parentElement, i++) {
            const classes = typeof n.className === "string" ? n.className.trim().split(" ").filter(Boolean).join(".") : "";
            parts.push(`${n.tagName.toLowerCase()}${n.id ? `#${n.id}` : ""}${classes ? `.${classes}` : ""}`);
          }
          return parts.join(" > ");
        };
        out.push(`${name(A)} ↔ ${name(B)} (gap ${Math.max(-ox, -oy).toFixed(1)}px; ${JSON.stringify(rect(r))} vs ${JSON.stringify(rect(q))}; ${path(A)} ↔ ${path(B)})`);
      }
    }
    return out;
  });
  assert.deepEqual(bad, [], `${label}: buttons touching or overlapping: ${[...new Set(bad)].slice(0, 5).join("; ")}`);
}

await test("hours: the second tap lands where the first one was (no mouse move); the timer shows seconds", async () => {
  const id = Date.now().toString(36).slice(-5), who = `tap-${id}@example.com`;
  const [oc, op] = await signIn("owner@example.com");
  await addPerson(op, who, "sms");
  const [c, pg] = await signIn(who);
  await pg.goto(BASE + "/admin/hours");
  for (const [first, second] of [["Clock in", "Yes, clock in"], ["Clock out", "Yes, clock out"]]) {
    const a = await pg.locator(".clock__confirm > summary").boundingBox();
    await pg.mouse.click(a.x + a.width / 2, a.y + a.height / 2); // tap 1
    const b = await pg.getByRole("button", { name: second }).boundingBox();
    const inside = (x, y) => x >= b.x && x <= b.x + b.width && y >= b.y && y <= b.y + b.height;
    assert.ok(inside(a.x + a.width / 2, a.y + a.height / 2), `${second} sits under the ${first} tap (${JSON.stringify(a)} vs ${JSON.stringify(b)})`);
    assert.ok(Math.abs(a.x - b.x) <= 2 && Math.abs(a.y - b.y) <= 2, `${second} starts where ${first} was`);
    await Promise.all([pg.waitForLoadState(), pg.mouse.click(a.x + a.width / 2, a.y + a.height / 2)]); // tap 2, same spot
    if (first === "Clock in") {
      await pg.locator(".clock--on").waitFor();
      await pg.waitForTimeout(1200);
      assert.match(await pg.locator("[data-clock-since]").first().innerText(), /^\d+:\d\d:\d\d$/, "running timer counts seconds");
    }
  }
  await pg.locator(".clock__confirm > summary", { hasText: "Clock in" }).waitFor();
  await c.close(); await oc.close();
});

await test("hours: quick clock-in from the header (two taps, same spot, stay on the page); no reason gets flagged; both timers tick", async () => {
  const id = Date.now().toString(36).slice(-5), who = `quick-${id}@example.com`;
  const [oc, op] = await signIn("owner@example.com");
  await addPerson(op, who, "sms");
  const [c, pg] = await signIn(who);
  await pg.goto(BASE + "/about?x=1");
  const sum = pg.locator("header .nav--wide [data-quick-clock] > summary");
  const a = await sum.boundingBox();
  await pg.mouse.click(a.x + a.width / 2, a.y + a.height / 2);
  const b = await pg.locator("header .nav--wide .nav__clock--sure").boundingBox();
  assert.ok(a.x + a.width / 2 > b.x && a.x + a.width / 2 < b.x + b.width && a.y + a.height / 2 > b.y && a.y + a.height / 2 < b.y + b.height, "confirm sits under the first tap");
  await Promise.all([pg.waitForURL(/\/about\?x=1$/), pg.mouse.click(a.x + a.width / 2, a.y + a.height / 2)]);
  await pg.locator("header .nav--wide .nav__clock.is-on").waitFor();
  await pg.goto(BASE + "/admin/hours");
  assert.match(await pg.locator(".clock__status").innerText(), /No reason yet/);
  const big = pg.locator(".clock__elapsed"), small = pg.locator("header .nav--wide [data-clock-since]");
  const [b1, s1] = [await big.innerText(), await small.innerText()];
  await pg.waitForTimeout(2100);
  assert.notEqual(await big.innerText(), b1, "big timer ticks"); assert.notEqual(await small.innerText(), s1, "header timer ticks");
  assert.match(await big.innerText(), /^\d+:\d\d:\d\d$/);
  await Promise.all([pg.waitForURL(/done=reason/), pg.locator(".clock [data-reason] button", { hasText: "Build" }).click()]);
  assert.match(await pg.locator(".clock__status").innerText(), /Build/);
  // Clocked out without a reason: the entry is flagged until one is picked.
  const tok = await pg.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  await pg.request.post(BASE + "/admin/hours/out", { form: { __RequestVerificationToken: tok } });
  await pg.goto(BASE + "/about");
  await pg.locator("header .nav--wide [data-quick-clock] > summary").click();
  await Promise.all([pg.waitForURL(/\/about$/), pg.locator("header .nav--wide .nav__clock--sure").click()]);
  await pg.request.post(BASE + "/admin/hours/out", { form: { __RequestVerificationToken: tok } });
  await pg.goto(BASE + "/admin/hours");
  assert.equal(await pg.locator("[data-no-reason]").isVisible(), true, "flag at the top");
  assert.equal(await pg.locator(".entry--flag").count(), 1);
  await pg.locator(".entry--flag [data-reason] button", { hasText: "Outreach" }).click(); await pg.waitForLoadState();
  assert.equal(await pg.locator(".entry--flag").count(), 0); assert.equal(await pg.locator("[data-no-reason]").count(), 0);
  await c.close(); await oc.close();
});

await test("profiles: photo crop + details go public only after approval; text is escaped; hiding is instant", async () => {
  const id = Date.now().toString(36).slice(-5), who = `pf-${id}@example.com`, name = `Pat Tester${id}`, slug = `pat-tester${id}`;
  const [oc, op] = await signIn("owner@example.com");
  await addPerson(op, `${name} <${who}>`, "sms");
  const [c, pg] = await signIn(who);
  await pg.goto(BASE + "/admin/profile");
  // A 600×400 test image drawn in the page, handed to the file picker.
  const png = await pg.evaluate(async () => { const cv = document.createElement("canvas"); cv.width = 600; cv.height = 400; const g = cv.getContext("2d");
    g.fillStyle = "#1e5ac8"; g.fillRect(0, 0, 600, 400); g.fillStyle = "#f0c828"; g.beginPath(); g.arc(300, 200, 150, 0, 7); g.fill();
    return [...new Uint8Array(await (await new Promise((r) => cv.toBlob(r, "image/png"))).arrayBuffer())]; });
  await pg.setInputFiles("[data-photo-pick]", { name: "me.png", mimeType: "image/png", buffer: Buffer.from(png) });
  await pg.locator("[data-photo-canvas]").waitFor();
  await pg.fill('input[name="headline"]', "Building <b>robots</b>");
  await pg.fill('textarea[name="bio"]', "Hi <script>alert(1)</script>\n\nSecond paragraph");
  await pg.fill('input[name="linkedin"]', "linkedin.com/in/pat");
  await pg.check("[data-profile-public]");
  await Promise.all([pg.waitForURL(/done=sent/), pg.click("form[data-profile-form] button[type=submit]")]);
  assert.equal(await pg.locator("[data-profile-status]").getAttribute("data-profile-status"), "pending");
  const draft = await pg.locator("[data-photo-current]").getAttribute("src");
  assert.match(draft, /^\/people\/photo\/[0-9a-f]{24}\/320$/);
  const anon = await browser.newContext(); const ap = await anon.newPage();
  assert.equal((await ap.request.get(BASE + "/people/" + slug)).status(), 404, "not public before approval");
  assert.equal((await ap.request.get(BASE + draft)).status(), 404, "draft photo is private");
  // A plain member can't approve; the owner can.
  const tok = await pg.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const self = await pg.request.post(BASE + "/admin/profile/review", { form: { email: who, action: "approve", __RequestVerificationToken: tok }, maxRedirects: 0 });
  assert.match(self.headers().location, /error=denied/, "no approving yourself");
  await op.goto(BASE + "/admin/hours");
  assert.equal(await op.locator("[data-review-banner]").isVisible(), true, "reviewers get a banner");
  await op.goto(BASE + "/admin/profile/review");
  await op.locator(`[data-review="${who}"] button`, { hasText: "Approve" }).click(); await op.waitForLoadState();
  await ap.goto(BASE + "/people/" + slug);
  assert.equal(await ap.locator(".person-page img.avatar").getAttribute("src"), draft, "approved photo on the public page");
  const photo = await ap.request.get(BASE + draft);
  assert.equal(photo.status(), 200); assert.equal(photo.headers()["content-type"], "image/webp", "cropped to WebP in the browser");
  assert.match(await ap.locator(".lede").innerText(), /<b>robots<\/b>/, "headline shown as typed, not as HTML");
  assert.equal(await ap.locator(".profile__bio script").count(), 0);
  assert.match(await ap.locator(".profile__bio").innerText(), /<script>/);
  const people = await (await ap.request.get(BASE + "/people.json")).json();
  assert.match(people[slug].links[0].href, /^https:\/\/linkedin\.com\/in\/pat/);
  // Bad link refused; untick public → gone at once.
  await pg.goto(BASE + "/admin/profile");
  await pg.fill('input[name="github"]', "javascript:alert(1)");
  await Promise.all([pg.waitForURL(/error=link/), pg.click("form[data-profile-form] button[type=submit]")]);
  await pg.goto(BASE + "/admin/profile");
  await pg.uncheck("[data-profile-public]");
  await Promise.all([pg.waitForURL(/done=saved/), pg.click("form[data-profile-form] button[type=submit]")]);
  assert.equal((await ap.request.get(BASE + "/people/" + slug)).status(), 404, "hidden right away");
  await anon.close(); await c.close(); await oc.close();
});

await test("team trees fold, and stay folded after a reload (People, Team hours)", async () => {
  const [c, pg] = await signIn("owner@example.com");
  await pg.goto(BASE + "/admin/people");
  const sms = pg.locator('details[data-fold="people-teams:sms"]');
  assert.equal(await sms.evaluate((d) => d.open), true, "open by default");
  await sms.locator("> summary").click({ position: { x: 8, y: 8 } });
  assert.equal(await sms.evaluate((d) => localStorage.getItem("avi-fold:" + d.dataset.fold)), "0", "collapse saved immediately");
  await pg.reload();
  assert.equal(await sms.evaluate((d) => d.open), false, "remembered");
  await pg.click("text=Expand all");
  assert.equal(await sms.evaluate((d) => d.open), true);
  await pg.goto(BASE + "/admin/hours/team?range=all");
  assert.ok(await pg.locator('details[data-fold^="hours-teams:"]').count() > 0, "team hours fold by team");
  await c.close();
});

await test("header: Sign in when signed out; Clock in + account menu when signed in (sign-in works on public pages)", async () => {
  const anon = await browser.newContext({ viewport: { width: 1280, height: 900 } }); const ap = await anon.newPage();
  await ap.goto(BASE + "/");
  assert.match(await ap.locator(".landing__actions > *").first().innerText(), /Join/, "signed out, home's first button is Join");
  await ap.goto(BASE + "/about");
  assert.equal(await ap.locator("header .nav--wide .nav__signin").isVisible(), true, "Sign in link in the header");
  assert.equal(await ap.locator("header .account-menu").count(), 0);
  await anon.close();
  const [c, pg] = await signIn("owner@example.com");
  await pg.goto(BASE + "/about");
  assert.equal(await pg.locator("header .nav--wide .nav__signin").count(), 0, "no Sign in once signed in");
  assert.equal(await pg.locator("header .nav--wide [data-quick-clock] > summary").isVisible(), true, "clock button in the header");
  await pg.goto(BASE + "/");
  assert.match(await pg.locator(".landing__actions > *").first().innerText(), /Clock|Clocked in/, "home's first button is the clock when signed in");
  await pg.goto(BASE + "/about");
  await pg.locator("header .nav--wide .account-menu > summary").click();
  for (const label of ["My hours", "Discord link", "Discord bot", "QR codes", "People"]) assert.equal(await pg.locator("header .nav--wide .account-menu__panel a", { hasText: label }).isVisible(), true, label);
  await pg.goto(BASE + "/admin/discord");
  const discordTitle = await pg.locator("h1").innerText();
  assert.equal(discordTitle.toLowerCase(), "discord link", `account page loads without needing Discord credentials (got ${discordTitle}; ${pg.url()})`);
  // Owner's bot page: status, pause/start round trip, add a server.
  await pg.goto(BASE + "/admin/discord/bot");
  assert.equal((await pg.locator("h1").innerText()).toLowerCase(), "discord bot", "owner can open the bot page");
  assert.equal(await pg.locator("[data-bot-state]").getAttribute("data-bot-state"), "running");
  await pg.locator("[data-bot-toggle]").click();
  await pg.waitForURL(/done=paused/);
  assert.equal(await pg.locator("[data-bot-state]").getAttribute("data-bot-state"), "paused", "pause sticks");
  await pg.locator("[data-bot-toggle]").click();
  await pg.waitForURL(/done=resumed/);
  assert.equal(await pg.locator("[data-bot-state]").getAttribute("data-bot-state"), "running");
  await pg.fill("#add-guild", "923456789012345678");
  await pg.fill("#add-name", "Smoke test server");
  await pg.locator("form[action='/admin/discord/bot/servers'] button[type=submit]").last().click();
  await pg.waitForURL(/done=server/);
  assert.equal(await pg.locator("#server-923456789012345678 h3").innerText(), "Smoke test server");
  await pg.locator("#server-923456789012345678 summary").click();
  await pg.locator("#server-923456789012345678 form[action$='/remove'] input[name=confirm]").check();
  await pg.locator("#server-923456789012345678 form[action$='/remove'] button").click();
  await pg.waitForURL(/done=removed/);
  assert.equal(await pg.locator("#server-923456789012345678").count(), 0, "removed servers disappear");
  // Re-add the same guild to prove the site configuration can be restored after removal.
  await pg.locator("#add-guild").fill("923456789012345678");
  await pg.locator("#add-name").fill("Smoke test server restored");
  await pg.locator('form[action="/admin/discord/bot/servers"]').last().locator("button[type=submit]").click();
  await pg.waitForURL(/done=server/);
  assert.equal(await pg.locator("#server-923456789012345678 h3").innerText(), "Smoke test server restored", "removed server can be re-added");
  await pg.goto(BASE + "/about");
  await pg.locator("header .nav--wide .account-menu > summary").click();
  await pg.locator("header .nav--wide .account-menu__panel a", { hasText: "QR codes" }).click();
  assert.equal(new URL(pg.url()).pathname, "/admin/qr");
  await c.close();
});

await test("dark mode: the toggle switches, remembers, and follows the system until pressed", async () => {
  const c = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: "dark" }); const pg = await c.newPage();
  await pg.goto(BASE + "/about");
  const bg = () => pg.evaluate(() => getComputedStyle(document.body).backgroundColor);
  const darkBg = await bg();
  assert.equal(await pg.locator("[data-theme-toggle]").getAttribute("aria-pressed"), "true", "system dark shows as on");
  await pg.click("[data-theme-toggle]");
  assert.equal(await pg.evaluate(() => document.documentElement.dataset.theme), "light");
  assert.notEqual(await bg(), darkBg, "page went light");
  await pg.reload();
  assert.equal(await pg.evaluate(() => document.documentElement.dataset.theme), "light", "choice survives a reload");
  await pg.click("[data-theme-toggle]");
  assert.equal(await pg.locator("[data-theme-toggle]").getAttribute("aria-pressed"), "true");
  assert.equal(await bg(), darkBg);
  await c.close();
});

await test("hours week: drag, tap and + add blocks; one save makes them all; overlaps are refused whole", async () => {
  const id = Date.now().toString(36).slice(-5), who = `wk-${id}@example.com`;
  const [oc, op] = await signIn("owner@example.com");
  await addPerson(op, who, "sms");
  const [c, pg] = await signIn(who);
  await pg.goto(BASE + "/admin/hours");
  await pg.click("text=Catch up on a week");
  await pg.click("text=← Previous"); // a week that's all in the past
  const days = await pg.locator("[data-week-add]").evaluateAll((bs) => bs.map((b) => b.dataset.weekAdd));
  assert.equal(days.length, 7);
  const col = pg.locator(`[data-week-col="${days[1]}"]`);
  await col.evaluate((el) => window.scrollTo(0, el.getBoundingClientRect().top + scrollY - 140));
  const bx = await col.boundingBox(), hour = bx.height / 18; // 6am–midnight
  await pg.mouse.move(bx.x + 30, bx.y + 3 * hour); await pg.mouse.down(); // 9:00
  await pg.mouse.move(bx.x + 30, bx.y + 5.5 * hour, { steps: 6 }); await pg.mouse.up(); // → 11:30
  assert.equal(await pg.inputValue("[data-week-rows] li >> nth=0 >> [data-row-start]"), "09:00");
  assert.equal(await pg.inputValue("[data-week-rows] li >> nth=0 >> [data-row-end]"), "11:30");
  await pg.click(`[data-week-add="${days[3]}"]`);
  assert.equal(await pg.locator("[data-week-rows] li").count(), 2);
  assert.match(await pg.locator("[data-week-save]").innerText(), /Save 2 blocks \(4:30\)/);
  // Drawing over an existing new block flags it and blocks the save.
  await pg.click(`[data-week-add="${days[1]}"]`);
  const extra = pg.locator(`[data-week-rows] li:has([data-row-date][value="${days[1]}"]) >> nth=1`);
  await extra.locator("[data-row-start]").fill("10:00"); await extra.locator("[data-row-start]").dispatchEvent("change");
  await pg.locator("[data-week-rows] li.is-clash").first().waitFor();
  await pg.click("[data-week-save]");
  assert.equal(await pg.locator("[data-week-form] .alert").isVisible(), true, "overlap warning");
  await pg.locator("[data-week-rows] li.is-clash >> nth=-1 >> [data-row-remove]").click();
  await Promise.all([pg.waitForURL(/done=added/), pg.click("[data-week-save]")]);
  assert.match(await pg.locator(".alert").first().innerText(), /Saved 2 blocks/);
  assert.equal(await pg.locator(`[data-week-col="${days[1]}"] [data-week-entry]`).count(), 1, "the block is on the calendar now");
  // The server refuses overlap with saved time, and saves nothing from that batch.
  const tok = await pg.locator('input[name="__RequestVerificationToken"]').first().inputValue();
  const body = new URLSearchParams([["__RequestVerificationToken", tok], ["week", days[0]],
    ["date", days[2]], ["start", "08:00"], ["end", "09:00"], ["team", "sms"], ["category", "Build"],
    ["date", days[1]], ["start", "10:00"], ["end", "12:00"], ["team", "sms"], ["category", "Build"]]);
  const r = await pg.request.post(BASE + "/admin/hours/save-week", { data: body.toString(), headers: { "Content-Type": "application/x-www-form-urlencoded" }, maxRedirects: 0 });
  assert.match(r.headers().location, /error=overlap/);
  await pg.reload();
  assert.equal(await pg.locator(`[data-week-col="${days[2]}"] [data-week-entry]`).count(), 0, "nothing from the refused batch was saved");
  // Blocks link to their edit form.
  await pg.locator(`[data-week-col="${days[1]}"] [data-week-entry]`).click();
  assert.equal(await pg.locator(".entry__edit[open]").count(), 1, "opened straight to Edit");
  await c.close(); await oc.close();
});

await test("layout: icons sit in the middle of their buttons", async () => {
  const c = await browser.newContext({ viewport: { width: 1280, height: 900 }, deviceScaleFactor: 2 }); const pg = await c.newPage();
  // Customize toggle (+ drawn in a circle)
  await pg.goto(BASE + "/tools/qr");
  const summary = pg.locator(".qr-custom > summary");
  await assertIconCentered(pg, async () => { await summary.scrollIntoViewIfNeeded(); return summary.evaluate((s) => {
    const r = s.getBoundingClientRect(), st = getComputedStyle(s), size = parseFloat(getComputedStyle(s, "::before").width);
    return { x: r.left + parseFloat(st.paddingLeft), y: r.top + (r.height - size) / 2, width: size, height: size };
  }); }, "Customize icon");
  // Team chip × on People
  await pg.goto(BASE + "/admin/login"); await pg.selectOption('select[name="email"]', "owner@example.com"); await pg.click("text=Sign in (dev only)");
  await pg.waitForURL((u) => !u.pathname.startsWith("/admin/login")); // signed in before moving on
  await pg.goto(BASE + "/admin/people");
  const x = pg.locator(".team-chip__x").first();
  if (await x.count()) await assertIconCentered(pg, x, "Team chip ×");
  // Pop-up close button
  await pg.goto(BASE + "/history");
  const open = pg.locator(".timeline__open").last();
  const sub = pg.locator("#" + (await open.getAttribute("data-sub")));
  await open.click();
  await sub.locator(".subtl__card").first().click();
  const close = pg.locator("dialog[open] .sheet__close");
  await close.waitFor();
  await assertIconCentered(pg, close, "Pop-up close ×");
  // Photo lightbox close
  await pg.goto(BASE + "/about");
  const img = pg.locator(".collage .photo__img").first();
  await img.scrollIntoViewIfNeeded(); await img.click();
  const lbClose = pg.locator("dialog.lightbox[open] .lightbox__close");
  await lbClose.waitFor();
  await assertIconCentered(pg, lbClose, "Lightbox close ×");
  await c.close();
});

await test("layout: buttons never touch or overlap (admin, QR maker, sign-in, home)", async () => {
  const c = await browser.newContext({ viewport: { width: 1280, height: 900 } }); const pg = await c.newPage();
  const openAll = () => pg.evaluate(() => document.querySelectorAll("details").forEach((d) => (d.open = true)));
  await pg.goto(BASE + "/admin/login"); await openAll(); await assertButtonsBreathe(pg, "/admin/login");
  await pg.selectOption('select[name="email"]', "owner@example.com"); await pg.click("text=Sign in (dev only)");
  for (const path of ["/admin/people", "/admin/qr", "/admin/qr/three", "/admin/usage", "/admin/hours", "/admin/hours/team", "/admin/hours/report", "/admin/hours/settings", "/admin/discord", "/admin/discord/bot", "/admin/hours/week", "/admin/profile", "/admin/profile/review", "/tools/qr", "/", "/about"]) {
    for (const w of [1440, 1024, 768, 390]) {
      await pg.setViewportSize({ width: w, height: 900 });
      await pg.goto(BASE + path); await openAll();
      await assertButtonsBreathe(pg, `${path} at ${w}px`);
    }
  }
  await c.close();
});

await test("Discord bot admin: post actions submit and explain missing setup", async () => {
  const guild = (900000000000000000n + BigInt(Date.now())).toString();
  const [c, pg] = await signIn("owner@example.com");
  try {
    await pg.goto(BASE + "/admin/discord/bot");
    const installHelp = pg.locator("[data-discord-install-help]");
    assert.match(await installHelp.innerText(), /does not install the bot in Discord/i, "site allow-list is distinguished from the Discord install");
    const invite = pg.locator("[data-discord-bot-invite]");
    if (await invite.count()) {
      const url = new URL(await invite.getAttribute("href"));
      assert.equal(url.origin, "https://discord.com", "invite opens Discord, not a site route");
      assert.equal(url.pathname, "/oauth2/authorize");
      const scopes = (url.searchParams.get("scope") || "").split(/\s+/);
      assert.ok(scopes.includes("bot") && scopes.includes("applications.commands"), "invite requests bot and command scopes");
      assert.equal(url.searchParams.get("permissions"), "3072", "invite requests only View Channel and Send Messages");
    }
    const add = pg.locator('form[action="/admin/discord/bot/servers"]').last();
    await add.locator('input[name="guild"]').fill(guild);
    await add.locator('input[name="name"]').fill("E2E Discord");
    await add.locator('select[name="team"]').selectOption("avi");
    await Promise.all([
      pg.waitForURL(/done=server/),
      add.locator('button[type="submit"]').click(),
    ]);
    const server = pg.locator(`#server-${guild}`);
    const posts = server.locator('form[action$="/post"]');
    const help = server.locator(".discord-bot__help");
    assert.equal(await help.evaluate(el => el.scrollWidth <= el.clientWidth), true, "meeting guidance wraps within the server card");
    const saveBox = await server.locator('form[action$="/settings"] .actions button').boundingBox();
    const postBox = await posts.nth(0).locator("button").boundingBox();
    assert.ok(postBox.y - (saveBox.y + saveBox.height) >= 12, "post actions have a visible gap after Save");
    assert.equal(await posts.count(), 2, "panel and summary post controls are present");
    assert.equal(await posts.nth(0).locator("button").isDisabled(), false, "panel action is not silently disabled");
    assert.equal(await posts.nth(1).locator("button").isDisabled(), false, "summary action is not silently disabled");
    await Promise.all([
      pg.waitForURL(/error=(?:no-token|paused|panel-channel-missing)/),
      posts.nth(0).locator("button").click(),
    ]);
    assert.match(await pg.locator(".alert").first().innerText(), /Needs the bot token|Set and save a clock panel channel|bot is paused/i);

    const owner = server.locator("details.discord-bot__owner");
    const ownerActions = owner.locator(".discord-bot__owner-actions");
    assert.equal(await ownerActions.isVisible(), false, "collapsed owner controls stay hidden");
    await owner.locator("summary").click();
    assert.equal(await ownerActions.isVisible(), true, "owner controls appear when expanded");
    const buttonGap = (a, b) => {
      const x = Math.max(0, Math.max(a.x, b.x) - Math.min(a.x + a.width, b.x + b.width));
      const y = Math.max(0, Math.max(a.y, b.y) - Math.min(a.y + a.height, b.y + b.height));
      return Math.max(x, y);
    };
    for (const width of [1440, 1024, 768, 390]) {
      await pg.setViewportSize({ width, height: 900 });
      const saveName = await owner.locator('form[action="/admin/discord/bot/servers"] .actions button').boundingBox();
      const registerForm = owner.locator('form[action="/admin/discord/bot/commands"]');
      if (await registerForm.count()) {
        const register = await registerForm.locator("button").boundingBox();
        assert.ok(buttonGap(saveName, register) >= 12, `Save name and Register commands have breathing room at ${width}px`);
      } else {
        const actionGap = await owner.locator(".discord-bot__owner-actions").evaluate((el) => parseFloat(getComputedStyle(el).rowGap));
        assert.ok(actionGap >= 12, `owner action groups have a visible gap at ${width}px`);
      }
      await assertButtonsBreathe(pg, `Discord bot admin owner controls at ${width}px`);
    }
    const remove = owner.locator('form[action$="/remove"]');
    await remove.locator('input[name="confirm"]').check();
    await Promise.all([pg.waitForURL(/done=removed/), remove.locator('button[type="submit"]').click()]);
  } finally { await c.close(); }
});

await test("no console errors", async () => { assert.deepEqual(consoleErrors, [], consoleErrors.join(" | ")); });

await browser.close();
for (const r of results) console.log(r.join("  "));
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
