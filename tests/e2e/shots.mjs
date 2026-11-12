import { chromium } from "playwright";
const b = await chromium.launch();
const out = "/tmp/claude-0/shots/";
for (const [w, tag] of [[1440, "d"], [390, "m"]]) {
  const p = await b.newPage({ viewport: { width: w, height: 900 }, reducedMotion: "reduce" });
  for (const r of process.argv.slice(2)) {
    await p.goto("http://127.0.0.1:5080" + r, { waitUntil: "networkidle" }).catch(() => {});
    await p.evaluate(async () => { for (let y = 0; y < document.body.scrollHeight; y += 600) { window.scrollTo(0, y); await new Promise(r => setTimeout(r, 60)); } window.scrollTo(0, 0); });
    await p.waitForTimeout(400);
    await p.screenshot({ path: out + tag + r.replace(/\//g, "_") + ".jpg", fullPage: true, quality: 55, type: "jpeg" });
  }
}
await b.close();
