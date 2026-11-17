// Static check for things our Content-Security-Policy (script-src 'self', no 'unsafe-inline', no 'unsafe-eval') blocks.
// These only fail when they *run* (e.g. pressing Enter in a form with onsubmit="…"), so a page can look fine in testing
// and still be broken. Run: node tests/e2e/csp-lint.mjs   (CI runs it before the browser tests)
import fs from "node:fs";
import path from "node:path";

const root = path.resolve(path.dirname(new URL(import.meta.url).pathname), "../../src/AviSite.Web");
const skip = /[\\/](bin|obj|node_modules|legacy-static|lib)[\\/]/; // legacy-static is served before our CSP; lib/ is vendored
const rules = [
  // Lowercase only: Blazor component parameters like OnValidSubmit= are fine.
  { ext: /\.(razor|cshtml|html)$/, re: /<[^>]*\son[a-z]+\s*=\s*["'{@]/g, why: "inline event handler (onclick=, onsubmit=…): use addEventListener in a /js file" },
  { ext: /\.razor$/, re: /@on[a-z]+\s*=/g, why: "Blazor @on… handler: pages are static SSR, so it never runs. Use a /js file" },
  { ext: /\.(razor|cshtml|html)$/, re: /<script(?![^>]*\ssrc=)[^>]*>\s*\S/gi, why: "inline <script> block: move it to wwwroot/js and load it with src" },
  { ext: /\.(razor|cshtml|html|js)$/, re: /javascript:/gi, why: "javascript: URL" },
  { ext: /\.js$/, re: /\son[a-z]+=\\?["'`]/gi, why: "inline handler written into HTML from JS: use addEventListener" },
  { ext: /\.js$/, re: /\b(eval|new Function)\s*\(/g, why: "eval / new Function (blocked without 'unsafe-eval')" },
  { ext: /\.js$/, re: /set(Timeout|Interval)\(\s*["'`]/g, why: "string passed to setTimeout/setInterval" },
];

const problems = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (skip.test(p + path.sep)) continue;
    if (e.isDirectory()) { walk(p); continue; }
    const text = fs.readFileSync(p, "utf8");
    for (const r of rules) {
      if (!r.ext.test(e.name)) continue;
      for (const m of text.matchAll(r.re)) {
        const line = text.slice(0, m.index).split("\n").length;
        problems.push(`${path.relative(root, p)}:${line}  ${r.why}\n    ${m[0].slice(0, 90)}`);
      }
    }
  }
})(root);

if (problems.length) {
  console.error(`CSP lint: ${problems.length} problem(s)\n` + problems.join("\n"));
  process.exit(1);
}
console.log("CSP lint: clean");
