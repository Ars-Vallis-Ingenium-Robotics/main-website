// Run from the worktree: node tests/e2e/forms.mjs (Node 22+, .NET 10).
// Optional Chromium checks: FORMS_BROWSER=1 with Playwright installed next to this file,
// or set FORMS_PLAYWRIGHT to its module path. FORMS_CHROMIUM overrides the browser executable.
// This fixture compiles the real site with the two documented registration lines in a disposable
// copy under obj. It never writes Program.cs, touches another checkout, or reuses a database.
import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import fs from "node:fs";
import { get as httpGet } from "node:http";
import { createRequire } from "node:module";
import net from "node:net";
import path from "node:path";
import { DatabaseSync } from "node:sqlite";
import { fileURLToPath } from "node:url";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const source = path.join(repo, "src/AviSite.Web");
const obj = path.join(source, "obj");
fs.mkdirSync(obj, { recursive: true });
const fixture = fs.mkdtempSync(path.join(obj, "forms-e2e-"));
const project = path.join(fixture, "src/AviSite.Web");
const data = path.join(fixture, "data");
const beforeGroupReuse = {
  groups: [
    { id: "avi", name: "AVI Robotics", addedUtc: "2023-01-01T00:00:00Z" },
    { id: "sms", name: "Sequoia Marine Solutions", parent: "avi", addedUtc: "2023-02-01T00:00:00Z" },
    { id: "avi-sibling", name: "AVI Sibling", parent: "avi" },
    { id: "partner", name: "Partner Robotics", addedUtc: "2023-03-01T00:00:00Z" },
  ],
  accounts: [
    ["team", "sms", "Lead"], ["org", "avi", "Lead"], ["sibling", "avi-sibling", "Lead"],
    ["partner", "partner", "Lead"], ["member", "sms", "Member"],
  ].map(([name, group, role]) => ({ name, email: `${name}@example.test`, stamp: name, role: "Member", teams: [{ group, role }] })),
};
let child;
let logs = "";
let base;
let testCount = 0;
const pass = message => { testCount++; console.log(`ok ${testCount} - ${message}`); };
const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));

function stageApp() {
  const ignored = new Set(["bin", "obj", "App_Data", "Program.cs"]);
  fs.mkdirSync(project, { recursive: true });
  for (const name of fs.readdirSync(source)) {
    if (ignored.has(name) || name.startsWith(".env")) continue;
    fs.cpSync(path.join(source, name), path.join(project, name), { recursive: true,
      filter: file => !path.basename(file).startsWith(".env") });
  }
  fs.cpSync(path.join(repo, "content"), path.join(fixture, "content"), { recursive: true });
  const register = (code, anchor, line) => {
    if (code.includes(line)) return code;
    assert.equal(code.split(anchor).length, 2, `registration anchor: ${anchor}`);
    return code.replace(anchor, `${line}\n${anchor}`);
  };
  let code = fs.readFileSync(path.join(source, "Program.cs"), "utf8");
  code = register(code, "var app = builder.Build();", "services.AddFormsModule();");
  code = register(code, "app.MapRazorComponents<App>();", "app.MapFormEndpoints();");
  fs.writeFileSync(path.join(project, "FormsIntegration.cs"), code);
  fs.mkdirSync(data);
  seed(beforeGroupReuse);
  const build = spawnSync("dotnet", ["build", "-c", "Release", "--nologo"], { cwd: project, stdio: "inherit" });
  if (build.error) throw build.error;
  assert.equal(build.status, 0, "integration Release build");
}

function seed(accounts) { fs.writeFileSync(path.join(data, "accounts.json"), JSON.stringify(accounts)); }

async function start() {
  const socket = net.createServer();
  await new Promise((resolve, reject) => socket.once("error", reject).listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${socket.address().port}`;
  await new Promise(resolve => socket.close(resolve));
  // Allowlist the environment; no credentials or external integration settings are inherited.
  const { PATH, HOME, TMPDIR, DOTNET_ROOT } = process.env;
  child = spawn("dotnet", ["bin/Release/net10.0/AviSite.Web.dll", "--urls", base], {
    cwd: project,
    env: { PATH, HOME, ...(TMPDIR ? { TMPDIR } : {}), ...(DOTNET_ROOT ? { DOTNET_ROOT } : {}),
      DOTNET_ENVIRONMENT: "Development", Store__DataDirectory: data, PayPal__Mode: "Off", SmsSite__AviBaseUrl: base },
    stdio: ["ignore", "pipe", "pipe"],
  });
  child.stdout.on("data", b => { logs = (logs + b).slice(-24_000); });
  child.stderr.on("data", b => { logs = (logs + b).slice(-24_000); });
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline && child.exitCode === null) {
    try { if ((await fetch(base + "/healthz", { signal: AbortSignal.timeout(1000) })).ok) return; } catch { /* starting */ }
    await sleep(100);
  }
  throw new Error("Development fixture did not start");
}

async function stop() {
  if (!child || child.exitCode !== null || child.signalCode !== null) return;
  const exited = new Promise(resolve => child.once("exit", resolve));
  const timer = setTimeout(() => child.kill("SIGKILL"), 4_000);
  child.kill("SIGTERM");
  await exited;
  clearTimeout(timer);
}

function client() {
  const jar = new Map();
  const request = async (url, init) => {
    const response = await fetch(base + url, { ...init, redirect: "manual", signal: AbortSignal.timeout(10_000),
      headers: { ...init?.headers, cookie: [...jar].map(([k, v]) => `${k}=${v}`).join("; ") } });
    for (const cookie of response.headers.getSetCookie()) {
      const pair = cookie.split(";", 1)[0];
      const index = pair.indexOf("=");
      jar.set(pair.slice(0, index), pair.slice(index + 1));
    }
    return response;
  };
  return { get: url => request(url), post: (url, values) => request(url, { method: "POST", body: new URLSearchParams(values) }),
    request, jar };
}

async function expect(request, status) {
  const response = await request;
  const text = await response.text();
  assert.equal(response.status, status, `${response.url.replace(base, "").replace(/[a-f0-9]{64}/g, "[share-token]")} returned ${response.status}: ${text.slice(0, 800)}`);
  return { text, headers: response.headers, location: response.headers.get("location") };
}
function privateHeaders(response) {
  assert.match(response.headers.get("cache-control") ?? "", /\bno-store\b/);
  assert.equal(response.headers.get("referrer-policy"), "no-referrer");
  assert.match(response.headers.get("x-robots-tag") ?? "", /\bnoindex\b/);
}
function section(html, action) {
  const result = [...html.matchAll(/<form\b[^>]*>[\s\S]*?<\/form>/g)].find(match => match[0].includes(`action="${action}"`));
  assert.ok(result, "expected form action in SSR HTML");
  return result[0];
}
function token(html) { return field(html, "__RequestVerificationToken"); }
function field(html, name) {
  const input = [...html.matchAll(/<input\b[^>]*>/g)].find(match => match[0].includes(`name="${name}"`));
  assert.ok(input, `expected field ${name} in SSR HTML`);
  const value = input[0].match(/\bvalue="([^"]*)"/);
  assert.ok(value, `expected value for ${name}`);
  return value[1];
}
async function login(identity) {
  const c = client();
  const loginHtml = (await expect(c.get("/admin/login"), 200)).text;
  await expect(c.post("/admin/dev-login", { email: `${identity}@example.test`,
    __RequestVerificationToken: token(section(loginHtml, "/admin/dev-login")) }), 302);
  assert.ok(c.jar.get("avi_admin"));
  return c;
}
async function draft(c, team, title) {
  const html = (await expect(c.get("/admin/forms"), 200)).text;
  const response = await expect(c.post("/admin/forms/create", { team, title, description: "Fixture description",
    __RequestVerificationToken: token(section(html, "/admin/forms/create")) }), 303);
  assert.match(response.location, /^\/admin\/forms\/[a-f0-9]{32}$/);
  return response.location;
}

const bank = [
  { label: "=Display name", type: "ShortText", required: true },
  { label: "Notes", type: "LongText" },
  { label: "Shift", type: "SingleChoice", options: "Morning\nAfternoon", required: true },
  { label: "Topics", type: "MultipleChoice", options: "Build\nCode\nOutreach", required: true },
  { label: "Day", type: "Date" },
  { label: "Hours", type: "Number" },
];
function builder(html, url, title, questions) {
  const form = section(html, url + "/save");
  const keys = [...form.matchAll(/name="(q-[a-f0-9]{32})-label"/g)].map(m => m[1]);
  assert.equal(keys.length, questions.length);
  const params = new URLSearchParams({ title, description: "Fixture description", revision: field(form, "revision"),
    __RequestVerificationToken: token(form), intent: "save", "new-label": "", "new-type": "ShortText", "new-options": "" });
  questions.forEach((q, index) => putQuestion(params, keys[index], q));
  return { params, keys };
}
function putQuestion(params, prefix, { label, type, required, options }) {
  params.set(`${prefix}-label`, label);
  params.set(`${prefix}-type`, type);
  params.set(`${prefix}-options`, options ?? "");
  if (required) params.set(`${prefix}-required`, "true");
}
function share(html) {
  const match = html.match(/href="(\/forms\/[a-f0-9]{64})"/);
  assert.ok(match, "opaque public link");
  return match[1];
}

async function browserCheck(publicUrl, adminUrl, manager, keys) {
  const modulePath = process.env.FORMS_PLAYWRIGHT;
  const { chromium } = modulePath ? createRequire(import.meta.url)(path.resolve(modulePath)) : await import("playwright");
  const browser = await chromium.launch({ executablePath: process.env.FORMS_CHROMIUM });
  try {
    const publicPage = await browser.newPage();
    const adminPage = await browser.newPage();
    await adminPage.context().addCookies([{ name: "avi_admin", value: manager.jar.get("avi_admin"), url: base }]);
    const csp = [];
    for (const [page, url] of [[publicPage, publicUrl], [adminPage, adminUrl]]) {
      page.on("console", message => {
        if (message.type() === "error" && /Content Security Policy|violates the following/i.test(message.text())) csp.push(message.text());
      });
      assert.equal((await page.goto(base + url)).status(), 200);
      for (const width of [375, 1440]) {
        await page.setViewportSize({ width, height: 900 });
        const layout = await page.locator(".avi-forms").evaluate(el => ({
          width: document.documentElement.clientWidth, scroll: document.documentElement.scrollWidth,
          minimumHeight: Math.min(...[...el.querySelectorAll("form input:not([type=hidden]):not([type=checkbox]), form button, form select, form textarea")]
            .map(e => e.getBoundingClientRect().height).filter(height => height > 0)),
        }));
        assert.equal(layout.scroll, layout.width, `horizontal overflow at ${width}px`);
        assert.ok(layout.minimumHeight >= 44, `control shorter than 44px at ${width}px`);
      }
    }
    await publicPage.setViewportSize({ width: 375, height: 900 });
    const first = publicPage.locator(`[name="${keys[0]}"]`);
    await publicPage.keyboard.press("Tab");
    await first.focus();
    assert.equal(await first.evaluate(el => getComputedStyle(el).outlineStyle), "solid");
    await first.fill("From browser");
    const boundaryText = ("x\n".repeat(999)) + "ok";
    await publicPage.locator(`[name="${keys[1]}"]`).fill(boundaryText);
    await publicPage.locator(`[name="${keys[2]}"]`).selectOption("Morning");
    await publicPage.getByRole("checkbox", { name: "Build" }).check();
    await publicPage.getByRole("button", { name: "Submit response" }).click();
    await publicPage.getByRole("heading", { name: "Response received" }).waitFor({ timeout: 10_000 });
    assert.deepEqual(csp, []);
  } finally { await browser.close(); }
}

try {
  stageApp();
  await start();
  const guest = client();
  await expect(guest.get("/admin/forms"), 404);
  const [team, org, sibling, partner, member] = await Promise.all(["team", "org", "sibling", "partner", "member"].map(login));
  await expect(member.get("/admin/forms"), 404);
  const title = "Team signup <fixture>";
  const id = await draft(team, "sms", title);
  const firstDraft = await expect(team.get(id), 200);
  privateHeaders(firstDraft);
  assert.match(firstDraft.text, /Team signup &lt;fixture&gt;/);
  assert.doesNotMatch(firstDraft.text, /Open public response page/);
  await expect(guest.get("/forms/1"), 404);
  await expect(guest.get(`/forms/${id.split("/").at(-1)}`), 404);
  const invalid = builder(firstDraft.text, id, title, []).params;
  invalid.set("intent", "publish");
  assert.match((await expect(team.post(id + "/save", invalid), 400)).text, /at least one question/);
  putQuestion(invalid, "new", { label: "Bogus", type: "File" });
  await expect(team.post(id + "/save", invalid), 400);
  putQuestion(invalid, "new", { label: "Bogus", type: "SingleChoice", options: "Red\nred" });
  await expect(team.post(id + "/save", invalid), 400);
  invalid.set("q-00000000000000000000000000000000-label", "Unknown field");
  assert.match((await expect(team.post(id + "/save", invalid), 400)).text, /unknown question field/);
  let lastSave;
  for (const [i, question] of bank.entries()) {
    const html = (await expect(team.get(id), 200)).text;
    const { params } = builder(html, id, title, bank.slice(0, i));
    putQuestion(params, "new", question);
    if (i === bank.length - 1) params.set("intent", "publish");
    await expect(team.post(id + "/save", params), 303);
    lastSave = params;
  }
  await expect(team.post(id + "/save", lastSave), 409);
  const manager = await expect(team.get(id), 200);
  const publicUrl = share(manager.text);
  const { keys } = builder(manager.text, id, title, bank);
  assert.equal(new Set(keys).size, bank.length);
  pass("builder creates, validates, edits and publishes all six question types; stale edits fail");

  const partnerId = await draft(partner, "partner", "Partner private signup");
  const partnerBuild = builder((await expect(partner.get(partnerId), 200)).text, partnerId, "Partner private signup", []).params;
  putQuestion(partnerBuild, "new", { label: "Optional", type: "ShortText" });
  partnerBuild.set("intent", "publish");
  await expect(partner.post(partnerId + "/save", partnerBuild), 303);
  const partnerUrl = share((await expect(partner.get(partnerId), 200)).text);
  assert.notEqual(publicUrl, partnerUrl);
  await expect(guest.get(publicUrl.slice(0, -1) + (publicUrl.endsWith("0") ? "1" : "0")), 404);
  await expect(guest.get("/forms/" + publicUrl.split("/").at(-1).toUpperCase()), 404);
  assert.doesNotMatch((await expect(team.get("/admin/forms"), 200)).text, new RegExp(`${publicUrl}|Partner private signup`));
  pass("drafts have no link; published URLs are unique 256-bit tokens and reject guessed or sequential URLs");

  const denied = [guest, member, sibling, partner];
  for (const c of denied) {
    await expect(c.get(id), 404);
    await expect(c.get(id + "/responses.csv"), 404);
  }
  const partnerIndex = await expect(partner.get("/admin/forms"), 200);
  assert.doesNotMatch(partnerIndex.text, /Team signup/);
  const createForm = section(partnerIndex.text, "/admin/forms/create");
  const illegal = { team: "sms", title: "Cross-tenant", description: "", __RequestVerificationToken: token(createForm) };
  await expect(partner.post("/admin/forms/create", illegal), 404);
  await expect(partner.post(id + "/archive", { ...illegal, revision: field(section(manager.text, id + "/archive"), "revision") }), 404);
  await expect(partner.post(id + "/save", legalEdit()), 404);
  const ownerView = await expect(org.get(id), 200);
  assert.equal(share(ownerView.text), publicUrl);
  const smsHtml = await new Promise((resolve, reject) => {
    httpGet(base + id, { headers: { host: "sms.localhost", cookie: `avi_admin=${team.jar.get("avi_admin")}` },
      signal: AbortSignal.timeout(10_000) }, async response => {
      try {
        assert.equal(response.statusCode, 200);
        const chunks = [];
        for await (const chunk of response) chunks.push(chunk);
        resolve(Buffer.concat(chunks).toString("utf8"));
      } catch (error) { reject(error); }
    }).on("error", reject);
  });
  assert.ok(smsHtml.includes(`href="${base + publicUrl}"`), "SMS admin shares the canonical AVI URL");
  await expect(org.get(partnerId), 404);
  pass("tenant and sibling isolation for listing, reads, edits, archive and export; parent lead can manage child");

  function legalEdit() { return builder(manager.text, id, title, bank).params; }
  const render = await expect(guest.get(publicUrl), 200);
  privateHeaders(render);
  assert.match(render.headers.get("content-security-policy") ?? "", /form-action 'self'/);
  assert.match(render.text, /\/css\/forms\.css/);
  assert.match(render.text, /doesn't record your IP address|doesn&#x27;t record your IP address/);
  const css = await expect(guest.get("/css/forms.css"), 200);
  assert.match(css.text, /\.avi-forms__card/);
  const action = publicUrl + "/submit";
  const publicForm = section(render.text, action);
  assert.doesNotMatch(publicForm, /type="file"|multipart/);
  assert.match(publicForm, new RegExp(`name="${keys[0]}"[^>]*required`));
  const own = new URLSearchParams({ __RequestVerificationToken: token(publicForm), revision: field(publicForm, "revision") });
  const valid = new URLSearchParams(own);
  const name = '=HYPERLINK("https://example.test","test")';
  const notes = 'First line, "quoted"\r\nSecond line <script>alert(1)</script>';
  for (const [index, value] of [[0, name], [1, notes], [2, "Morning"], [3, "Build"], [3, "Code"], [4, "2024-02-29"], [5, "-12.5"]]) valid.append(keys[index], value);
  let posts = 0;
  const post = (params, code, c = guest) => { posts++; return expect(c.post(action, params), code); };
  const mutate = fn => { const params = new URLSearchParams(valid); fn(params); return params; };
  await post(mutate(p => p.delete("__RequestVerificationToken")), 400);
  await post(valid, 400, client());
  await expect(team.post(id + "/save", new URLSearchParams({ title })), 400);
  await expect(team.post(id + "/archive", new URLSearchParams({ revision: own.get("revision") })), 400);
  posts++;
  await expect(guest.request(action, { method: "POST", body: new FormData() }), 415);
  posts++;
  await expect(guest.request(action, { method: "POST", body: new URLSearchParams({ extra: "x".repeat(420 * 1024) }) }), 413);
  pass("SSR renders controls and privacy notice under CSP; CSRF, multipart and oversized bodies are rejected");

  assert.match((await post(mutate(p => p.delete(keys[0])), 400)).text, /Question 1 is required/);
  assert.match((await post(mutate(p => p.delete(keys[3])), 400)).text, /Question 4 is required/);
  await post(mutate(p => p.set(keys[0], " ".repeat(3))), 400);
  await post(mutate(p => p.set(keys[0], "x".repeat(241))), 400);
  await post(mutate(p => p.set(keys[1], "x".repeat(2001))), 400);
  await post(mutate(p => p.append(keys[0], "Duplicate")), 400);
  await post(mutate(p => p.set(keys[2], "Blatantly not an option")), 400);
  await post(mutate(p => p.set(keys[3], "Bad choice")), 400);
  await post(mutate(p => p.append(keys[3], "Build")), 400);
  await post(mutate(p => p.set(keys[4], "2025-02-29")), 400);
  await post(mutate(p => p.set(keys[5], "1e5000")), 400);
  await post(mutate(p => p.set("q-00000000000000000000000000000000", "unknown")), 400);
  const accepted = await post(valid, 303);
  const receipt = new URL(accepted.location, base);
  assert.equal(receipt.pathname, publicUrl);
  assert.match(receipt.searchParams.get("receipt") ?? "", /^[a-zA-Z0-9_-]+$/);
  privateHeaders(accepted);
  assert.match((await expect(guest.get(accepted.location), 200)).text, /Thank you\. Your response was received/);
  assert.doesNotMatch((await expect(guest.get(publicUrl + "?receipt=fake"), 200)).text, /Your response was received/);
  assert.doesNotMatch((await expect(guest.get(partnerUrl + receipt.search), 200)).text, /Your response was received/);
  pass("server validates required answers, field IDs, duplicates, option membership, lengths, dates and finite numbers");

  const view = await expect(org.get(id), 200);
  assert.match(view.text, /First line, &quot;quoted&quot;/);
  assert.match(view.text, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
  assert.doesNotMatch(view.text, /<script>alert\(1\)<\/script>/);
  const csv = await expect(org.get(id + "/responses.csv"), 200);
  privateHeaders(csv);
  assert.match(csv.headers.get("content-disposition") ?? "", /^attachment; filename=avi-form-[a-f0-9]{32}\.csv$/);
  assert.match(csv.text, /submission_id,submitted_utc,question_id,question,answer\r\n/);
  assert.match(csv.text, /,"'=Display name","'=HYPERLINK\(""https:\/\/example\.test"",""test""\)"\r\n/);
  assert.match(csv.text, /,"Notes","First line, ""quoted""\nSecond line <script>alert\(1\)<\/script>"\r\n/);
  assert.match(csv.text, /,"Hours","'-12\.5"/);
  assert.match(csv.text, /,"Topics","Build"\r\n.*,"Topics","Code"\r\n/);
  for (const c of denied) await expect(c.get(id + "/responses.csv"), 404);
  assert.doesNotMatch((await expect(partner.get(partnerId + "/responses.csv"), 200)).text, /Display name|First line|HYPERLINK/);
  pass("manager sees encoded historical responses and exports formula-safe, quoted CSV without cross-tenant data");

  if (process.env.FORMS_BROWSER === "1" || process.env.FORMS_PLAYWRIGHT) {
    await browserCheck(publicUrl, id, team, keys);
    posts++;
    pass("Chromium renders builder and response at 375px and 1440px; native form submits a 2,000-character multiline answer");
  }

  const update = legalEdit();
  update.set(keys[0] + "-label", "Updated name");
  update.set(keys[1] + "-remove", "true");
  await expect(team.post(id + "/save", update), 303);
  const updated = await expect(team.get(id), 200);
  assert.equal(share(updated.text), publicUrl);
  assert.match(updated.text, /=Display name/);
  const latest = (await expect(guest.get(publicUrl), 200)).text;
  assert.match(latest, /Updated name/);
  assert.doesNotMatch(latest, new RegExp(`name="${keys[1]}"`));
  await post(valid, 409);
  const updatedForm = section(latest, action);
  const latestAnswers = mutate(p => {
    p.set("revision", field(updatedForm, "revision"));
    p.set("__RequestVerificationToken", token(updatedForm));
    p.delete(keys[1]);
  });
  while (posts < 20) await post(latestAnswers, 303);
  const limited = await post(latestAnswers, 429);
  privateHeaders(limited);
  const otherPublic = section((await expect(guest.get(partnerUrl), 200)).text, partnerUrl + "/submit");
  await expect(guest.post(partnerUrl + "/submit", { __RequestVerificationToken: token(otherPublic), revision: field(otherPublic, "revision") }), 429);
  pass("published edits preserve links and historical answers; stale responses fail and the per-IP cap returns 429 across links");

  const archive = section(updated.text, id + "/archive");
  await expect(team.post(id + "/archive", { __RequestVerificationToken: token(archive), revision: field(archive, "revision") }), 303);
  await expect(guest.get(publicUrl), 404);
  assert.match((await expect(guest.get(accepted.location), 200)).text, /Your response was received/);
  assert.match((await expect(team.get(id), 200)).text, /Archived/);
  assert.match((await expect(team.get(id + "/responses.csv"), 200)).text, /Display name/);
  const db = new DatabaseSync(path.join(data, "site.db"), { readOnly: true });
  try {
    const saved = db.prepare("SELECT share_hash, share_protected FROM avi_forms WHERE id = ?").get(partnerId.split("/").at(-1));
    const raw = partnerUrl.split("/").at(-1);
    assert.equal(saved.share_hash.toLowerCase(), createHash("sha256").update(raw).digest("hex"));
    assert.notEqual(saved.share_protected, raw);
    assert.equal(db.prepare("SELECT share_hash FROM avi_forms WHERE status=2").get().share_hash, null);
    assert.deepEqual(db.prepare("PRAGMA table_info(avi_form_submissions)").all().map(c => c.name), ["id", "form_id", "submitted_utc", "answers_json"]);
    const row = db.prepare("SELECT submitted_utc, answers_json FROM avi_form_submissions ORDER BY id LIMIT 1").get();
    assert.ok(Number.isFinite(Date.parse(row.submitted_utc)));
    assert.equal(JSON.parse(row.answers_json).length, 6);
  } finally { db.close(); }
  await stop();
  await start();
  assert.equal(share((await expect(partner.get(partnerId), 200)).text), partnerUrl);
  const optional = section((await expect(guest.get(partnerUrl), 200)).text, partnerUrl + "/submit");
  await expect(guest.post(partnerUrl + "/submit", { __RequestVerificationToken: token(optional), revision: field(optional, "revision") }), 303);
  assert.match((await expect(partner.get(partnerId + "/responses.csv"), 200)).text, /\r\n1,"[^"]+",,,\r\n$/);
  await expect(guest.get(publicUrl), 404);
  pass("archive revokes sharing, retains responses; startup migration and protected share links survive restart");

  await stop();
  const replacement = structuredClone(beforeGroupReuse);
  replacement.groups.find(g => g.id === "partner").addedUtc = "2025-03-01T00:00:00Z";
  seed(replacement);
  await start();
  await expect(partner.get(partnerId), 404);
  await expect(partner.get(partnerId + "/responses.csv"), 404);
  await expect(guest.get(partnerUrl), 404);
  assert.doesNotMatch((await expect(partner.get("/admin/forms"), 200)).text, /Partner private signup/);
  pass("recreating a group ID does not inherit old links, forms or responses");
  console.log(`Forms e2e: ${testCount} scenarios passed on a fresh Development database.`);
} catch (error) {
  if (logs) console.error(logs.replace(/(?<=\/forms\/)[a-f0-9]{64}/g, "[share-token]"));
  throw error;
} finally {
  await stop();
  fs.rmSync(fixture, { recursive: true, force: true });
}
