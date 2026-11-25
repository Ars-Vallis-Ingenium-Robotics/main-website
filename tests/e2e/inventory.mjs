import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { DatabaseSync } from "node:sqlite";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const base = process.env.TMPDIR || path.join(os.homedir(), ".hermes", "cache", "scratch");
fs.mkdirSync(base, { recursive: true });
const scratch = fs.mkdtempSync(path.join(base, "avi-inventory-"));
const fixture = path.join(scratch, "fixture");
const source = path.join(fixture, "src", "AviSite.Web");
const data = path.join(scratch, "data");
fs.mkdirSync(path.dirname(source), { recursive: true });
fs.cpSync(path.join(repo, "src", "AviSite.Web"), source, { recursive: true, filter: p => !/[\\/](bin|obj)[\\/]/.test(p) });
fs.mkdirSync(data, { recursive: true });
const programPath = path.join(source, "Program.cs");
let program = fs.readFileSync(programPath, "utf8");
assert(!program.includes("AddInventoryModule()") && !program.includes("MapInventoryEndpoints()"), "fixture must begin with no inventory registration");
program = program.replace("services.AddSingleton<PurchasingStore>();", "services.AddSingleton<PurchasingStore>();\nservices.AddInventoryModule();");
program = program.replace("app.MapPurchasingEndpoints();", "app.MapPurchasingEndpoints();\napp.MapInventoryEndpoints();");
fs.writeFileSync(programPath, program);
fs.writeFileSync(path.join(data, "accounts.json"), JSON.stringify({
  accounts: [
    { email: "lead@example.test", name: "Lead", role: "Member", teams: [{ group: "shop", role: "Lead" }] },
    { email: "member@example.test", name: "Member", role: "Member", teams: [{ group: "shop", role: "Member" }] },
    { email: "other@example.test", name: "Other", role: "Member", teams: [{ group: "other", role: "Lead" }] },
  ],
  groups: [{ id: "avi", name: "AVI Robotics" }, { id: "shop", name: "Shop", parent: "avi" }, { id: "other", name: "Other Org" }],
}));
const port = 21000 + Math.floor(Math.random() * 15000);
const url = `http://127.0.0.1:${port}`;
let logs = "";
let proc;
let success = false;

async function waitReady() {
  const deadline = Date.now() + 120_000;
  while (Date.now() < deadline) {
    if (proc.exitCode !== null) throw new Error(`fixture exited (${proc.exitCode})`);
    try { if ((await fetch(`${url}/healthz`)).ok) return; } catch { /* starting */ }
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error(`fixture readiness timeout\n${logs}`);
}

async function login(email) {
  const page = await fetch(`${url}/admin/login`);
  assert.equal(page.status, 200, "login page renders for antiforgery token issuance");
  const html = await page.text();
  const token = html.match(/<input(?=[^>]*name="__RequestVerificationToken")(?=[^>]*value="([^"]+)")[^>]*>/);
  assert(token, "login form contains antiforgery token");
  const cookie = page.headers.getSetCookie().map(value => value.split(";")[0]).join("; ");
  const response = await fetch(`${url}/admin/dev-login`, { method: "POST", headers: { cookie, "content-type": "application/x-www-form-urlencoded" }, body: new URLSearchParams({ email, __RequestVerificationToken: token[1] }), redirect: "manual" });
  assert.equal(response.status, 302, `development login succeeds with CSRF; location=${response.headers.get("location")}`);
  assert.doesNotMatch(response.headers.get("location") ?? "", /error=/, "fixture account exists and can sign in");
  const cookies = new Map();
  for (const value of [...page.headers.getSetCookie(), ...response.headers.getSetCookie()]) {
    const pair = value.split(";")[0];
    cookies.set(pair.slice(0, pair.indexOf("=")), pair);
  }
  return [...cookies.values()].join("; ");
}

async function getPage(cookie, query = "") {
  return fetch(`${url}/admin/inventory${query}`, { headers: { cookie } });
}

async function post(cookie, token, route, fields, includeToken = true) {
  const body = new URLSearchParams(includeToken ? { ...fields, __RequestVerificationToken: token } : fields);
  return fetch(`${url}${route}`, { method: "POST", headers: { cookie, "content-type": "application/x-www-form-urlencoded" }, body, redirect: "manual" });
}

function fields(extra = {}) {
  return { teamId: "shop", name: "Motor", sku: "M-1", category: "Drive", location: "Bin A", unit: "each", minimum: "5", openingQuantity: "5", openingReason: "Initial count", supplier: "=1+1", notes: "", ...extra };
}

try {
  const env = { ...process.env, ASPNETCORE_ENVIRONMENT: "Development", ASPNETCORE_URLS: url, Store__DataDirectory: data, DOTNET_CLI_TELEMETRY_OPTOUT: "1", DOTNET_NOLOGO: "1" };
  for (const secret of ["PayPal__ClientSecret", "Admin__PasswordHash", "Discord__BotToken"]) delete env[secret];
  proc = spawn("dotnet", ["run", "--no-launch-profile", "--configuration", "Release", "--project", "src/AviSite.Web/AviSite.Web.csproj"], { cwd: fixture, env, stdio: ["ignore", "pipe", "pipe"] });
  proc.stdout.on("data", chunk => { logs += chunk.toString(); });
  proc.stderr.on("data", chunk => { logs += chunk.toString(); });
  await waitReady();

  const unauth = await fetch(`${url}/admin/inventory/items`);
  assert.equal(unauth.status, 404, "unauthorized endpoint is hidden");
  const lead = await login("lead@example.test");
  const page = await getPage(lead);
  assert.equal(page.status, 200, `inventory page renders after login; got ${page.status} ${page.url}`);
  const html = await page.text();
  const dbPath = path.join(data, "site.db");
  const db = new DatabaseSync(dbPath);
  const tables = db.prepare("SELECT name FROM sqlite_master WHERE type='table'").all().map(row => row.name);
  assert(tables.includes("inventory_items") && tables.includes("inventory_transactions"), "isolated fixture created inventory tables in a fresh SQLite database");
  assert.equal(db.prepare("SELECT COUNT(*) AS n FROM inventory_items").get().n, 0);
  db.close();
  const tokenMatch = html.match(/<input(?=[^>]*name="__RequestVerificationToken")(?=[^>]*value="([^"]+)")[^>]*>/);
  assert(tokenMatch, "inventory forms include antiforgery token");
  const token = tokenMatch[1];

  const csrfDenied = await post(lead, token, "/admin/inventory/items", fields(), false);
  assert.equal(csrfDenied.status, 400, "item creation rejects missing antiforgery token");
  assert.equal((await post(lead, token, "/admin/inventory/items", fields())).status, 302);
  let admin = new DatabaseSync(dbPath);
  const item = admin.prepare("SELECT id FROM inventory_items WHERE sku='M-1'").get();
  assert(item);
  let ledger = admin.prepare("SELECT kind,quantity,balance_after,actor,at_utc,reason FROM inventory_transactions WHERE item_id=? ORDER BY id").all(item.id);
  assert.equal(ledger.length, 1, "opening balance is represented by an immutable ledger receipt");
  assert.deepEqual({ kind: ledger[0].kind, quantity: ledger[0].quantity, balance: ledger[0].balance_after, actor: ledger[0].actor, reason: ledger[0].reason }, { kind: "Receive", quantity: 5, balance: 5, actor: "lead@example.test", reason: "Initial count" });
  assert.match(ledger[0].at_utc, /Z$/);
  admin.close();

  assert.equal((await getPage(lead, "?q=Motor")).status, 200, "search by item name returns inventory page");
  assert.match(await (await getPage(lead, "?q=M-1")).text(), /Motor/);
  assert.doesNotMatch(await (await getPage(lead, "?q=does-not-exist")).text(), /M-1/);
  assert.match(await (await getPage(lead, "?low=true")).text(), /M-1/, "low-stock filter includes balance at minimum");

  async function move(kind, quantity, reason = "stock test", cookie = lead, csrfToken = token) {
    return post(cookie, csrfToken, "/admin/inventory/movement", { itemId: item.id, organizationId: "avi", kind, quantity: String(quantity), reason });
  }
  assert.equal((await move("Receive", 5, "restock delivery")).status, 302);
  assert.doesNotMatch(await (await getPage(lead, "?low=true")).text(), /M-1/, "low-stock filter excludes inventory above its threshold");
  assert.equal((await move("Issue", 2, "issued to assembly")).status, 302);
  assert.equal((await move("Adjust", 1, "cycle count correction")).status, 302);
  let rejected = await move("Issue", 2, "attempted over-issue");
  assert.equal(rejected.status, 302);
  assert.match(rejected.headers.get("location") ?? "", /status=stock/, "over-issue redirects with the stock rejection result");
  const deniedPage = await getPage(lead, "?status=stock");
  assert.match(await deniedPage.text(), /would make stock negative/);
  admin = new DatabaseSync(dbPath);
  const afterRejected = admin.prepare("SELECT kind,quantity,balance_after,actor,reason FROM inventory_transactions WHERE item_id=? ORDER BY id").all(item.id);
  assert.equal(afterRejected.length, 4, "rejected over-issue appended no ledger movement");
  assert.deepEqual(afterRejected.map(row => row.kind), ["Receive", "Receive", "Issue", "Adjust"]);
  assert.equal(afterRejected.at(-1).balance_after, 1, "rejected over-issue leaves balance unchanged");
  assert.equal(admin.prepare("SELECT balance_after FROM inventory_transactions WHERE item_id=? ORDER BY id DESC LIMIT 1").get(item.id).balance_after, 1);
  assert(afterRejected.every(row => row.actor === "lead@example.test" && row.reason.length > 0));
  assert.equal(admin.prepare("SELECT COUNT(*) AS n FROM inventory_transactions WHERE item_id=? AND reason='attempted over-issue'").get(item.id).n, 0);
  admin.close();
  assert.match(await (await getPage(lead, "?low=true")).text(), /M-1/, "low-stock filter includes item below its threshold");

  const member = await login("member@example.test");
  const memberPage = await getPage(member);
  const memberHtml = await memberPage.text();
  const memberTokenMatch = memberHtml.match(/<input(?=[^>]*name="__RequestVerificationToken")(?=[^>]*value="([^"]+)")[^>]*>/);
  assert(memberTokenMatch, "member page includes its antiforgery token");
  const memberToken = memberTokenMatch[1];
  assert.equal((await post(member, memberToken, "/admin/inventory/items", fields({ sku: "NOPE" }))).status, 404, "member cannot create items");
  assert.equal((await move("Receive", 1, "member receipt", member, memberToken)).status, 302, "team member can record an auditable stock movement");
  admin = new DatabaseSync(dbPath);
  const memberEvent = admin.prepare("SELECT actor,reason,at_utc FROM inventory_transactions WHERE item_id=? ORDER BY id DESC LIMIT 1").get(item.id);
  assert.deepEqual({ actor: memberEvent.actor, reason: memberEvent.reason }, { actor: "member@example.test", reason: "member receipt" });
  assert.match(memberEvent.at_utc, /Z$/);
  admin.close();
  const other = await login("other@example.test");
  const otherPage = await getPage(other);
  assert.equal(otherPage.status, 200);
  const otherHtml = await otherPage.text();
  assert.doesNotMatch(otherHtml, /M-1|Motor/, "different organization cannot see inventory item");
  const otherTokenMatch = otherHtml.match(/<input(?=[^>]*name="__RequestVerificationToken")(?=[^>]*value="([^"]+)")[^>]*>/);
  assert(otherTokenMatch, "other organization has an antiforgery token");
  const crossOrg = await post(other, otherTokenMatch[1], "/admin/inventory/movement", { itemId: item.id, organizationId: "avi", kind: "Receive", quantity: "1", reason: "cross-org attack" });
  assert.equal(crossOrg.status, 404, "different organization cannot post movements into AVI inventory");
  const forgedOrg = await post(lead, token, "/admin/inventory/movement", { itemId: item.id, organizationId: "shop", kind: "Receive", quantity: "1", reason: "wrong tree level" });
  assert.equal(forgedOrg.status, 404, "team IDs cannot be submitted where organization IDs are required");
  const csv = await fetch(`${url}/admin/inventory/items.csv?organizationId=avi`, { headers: { cookie: lead } });
  assert.equal(csv.status, 200);
  const csvText = await csv.text();
  assert.match(csvText, /M-1/);
  assert.match(csvText, /'=1\+1/, "CSV neutralizes spreadsheet formulas");
  success = true;
  console.log("Inventory E2E passed: isolated fixture + SQLite, create/search/opening ledger, receive/issue/adjust, negative-stock rejection invariance, low-stock filtering, audit identity/reason/UTC, antiforgery, authorization, and cross-organization isolation.");
} catch (error) {
  console.error(error);
  console.error(`Inventory E2E scratch: ${scratch}`);
  if (logs) console.error(logs);
  process.exitCode = 1;
} finally {
  if (proc && proc.exitCode === null) {
    proc.kill("SIGTERM");
    await Promise.race([new Promise(resolve => proc.once("exit", resolve)), new Promise(resolve => setTimeout(resolve, 5000))]);
    if (proc.exitCode === null) proc.kill("SIGKILL");
  }
  if (success) fs.rmSync(scratch, { recursive: true, force: true });
}
