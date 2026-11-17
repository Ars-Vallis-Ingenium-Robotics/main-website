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
  try {
    for (const [w, label] of [[920, "Join"], [1280, "Join the club"]]) {
      await page.setViewportSize({ width: w, height: 700 });
      await page.goto(BASE + "/about");
      const j = page.locator(".nav--wide .nav__join");
      assert.equal((await j.innerText()).trim(), label, `label at ${w}px`);
      assert.equal(await j.getAttribute("aria-label"), "Join the club");
      assert.ok((await j.boundingBox()).height < 48, `join button stays one line at ${w}px`);
      assert.equal(await page.evaluate(() => { const b = document.querySelector(".site-header__bar"); return b.scrollWidth <= b.clientWidth + 1; }), true, `header fits at ${w}px`);
    }
  } finally {
    await page.setViewportSize({ width: 1280, height: 900 });
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
  await tp.getByLabel("Password").fill(process.env.TEAM_PASSWORD ?? "sms-dev-password");
  await tp.getByRole("button", { name: "Sign in" }).click();
  await tp.waitForURL(BASE + "/admin/qr");
  assert.equal(await tp.locator('nav a[href="/admin"]').count(), 0, "no orders link for teams");
  assert.equal(await tp.locator('[data-qr-row="one"]').count(), 0, "club codes hidden");
  assert.equal((await tp.goto(BASE + "/admin/qr/one")).status(), 404, "club code edit blocked");
  await tp.goto(BASE + "/admin/qr");
  await tp.fill("#slug", "flyer-" + Date.now().toString(36)); await tp.fill("#target", "/join"); await tp.fill("#campaign", "e2e");
  await tp.getByRole("button", { name: "Add code" }).click();
  assert.match(await tp.locator("main").innerText(), /start with "sms-"/);
  const slug = "sms-e2e-" + Date.now().toString(36);
  await tp.fill("#slug", slug); await tp.getByRole("button", { name: "Add code" }).click();
  await tp.waitForURL(/saved=/);
  assert.equal(await tp.locator(`[data-qr-row="${slug}"]`).count(), 1);
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

await test("no console errors", async () => { assert.deepEqual(consoleErrors, [], consoleErrors.join(" | ")); });

await browser.close();
for (const r of results) console.log(r.join("  "));
const failed = results.filter((r) => r[0] === "FAIL").length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
