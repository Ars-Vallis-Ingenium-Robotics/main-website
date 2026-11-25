// Standalone: install Playwright + Chromium, then run node tests/e2e/decision-matrices.mjs.
// Builds and exercises the optional module in a disposable loopback Development fixture.
import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";

import { access, copyFile, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import { chromium } from "playwright";

const root = fileURLToPath(new URL("../../", import.meta.url));
const project = "src/AviSite.Web";
const fixture = await mkdtemp(join(tmpdir(), "avi-matrices-"));
const app = join(fixture, project);
const data = join(fixture, "data");
const matrix = "/admin/decision-matrices";
const token = "__RequestVerificationToken";
const ownerEmail = "owner@example.com";
const leadEmail = "matrix-lead@example.com";
const parentLeadEmail = "matrix-parent-lead@example.com";
const memberEmail = "matrix-member@example.com";
const siblingEmail = "matrix-sibling@example.com";
const outsiderEmail = "matrix-outsider@example.com";
const team = "matrix-controls";
const siblingTeam = "matrix-build";
const otherTeam = "matrix-other";
let server, stopped, browser, base;
let log = "";

async function stage() {
  const tracked = execFileSync("git", ["ls-files", "-z", "--", project, "content"], { cwd: root, encoding: "utf8" });
  const moduleFiles = ["Components/Pages/Admin/DecisionMatrices.razor", "Endpoints/DecisionMatrixEndpoints.cs",
    "Store/DecisionMatrices.cs", "wwwroot/css/decision-matrices.css"].map(file => `${project}/${file}`);
  const files = new Set([...tracked.split("\0").filter(Boolean), ...moduleFiles]);
  for (const file of files) {
    const dest = join(fixture, file);
    await mkdir(dirname(dest), { recursive: true });
    await copyFile(join(root, file), dest);
  }
  const program = join(app, "Program.cs");
  let source = await readFile(program, "utf8");
  for (const [anchor, call] of [
    ["services.AddSingleton<PurchasingStore>();", "services.AddDecisionMatrixModule();"],
    ["app.MapPurchasingEndpoints();", "app.MapDecisionMatrixEndpoints();"],
  ]) {
    assert.equal(source.split(anchor).length, 2, `fixture registration anchor: ${anchor}`);
    if (!source.includes(call)) source = source.replace(anchor, `${anchor}\n${call}`);
  }
  await writeFile(program, source);
}

async function start() {
  const env = Object.fromEntries(["PATH", "HOME", "TMPDIR", "LANG", "DOTNET_ROOT", "NUGET_PACKAGES"]
    .filter(key => process.env[key] !== undefined).map(key => [key, process.env[key]]));
  execFileSync("dotnet", ["build", "-c", "Release", "--nologo"], { cwd: app, env, stdio: "inherit", timeout: 180_000 });
  server = spawn("dotnet", ["bin/Release/net10.0/AviSite.Web.dll"], {
    cwd: app,
    env: { ...env, ASPNETCORE_ENVIRONMENT: "Development", ASPNETCORE_URLS: "http://127.0.0.1:0",
      Store__DataDirectory: data, PayPal__Mode: "Fake" },
    stdio: ["ignore", "pipe", "pipe"],
  });
  stopped = new Promise(resolve => server.once("close", resolve));
  server.on("error", error => { log += error.message; });
  for (const stream of [server.stdout, server.stderr]) stream.on("data", chunk => { log += chunk; });
  const deadline = Date.now() + 30_000;
  while (Date.now() < deadline && server.exitCode === null && server.signalCode === null) {
    const listening = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/);
    if (listening) {
      base = listening[1];
      const login = await fetch(`${base}/admin/login`, { signal: AbortSignal.timeout(5_000) });
      assert.equal(login.status, 200);
      assert.match(await login.text(), /Sign in \(dev only\)/);
      await access(join(data, "site.db"));
      return;
    }
    await delay(100);
  }
  throw new Error("Development fixture did not listen on loopback");
}

async function signIn(email) {
  const page = await browser.newPage({ javaScriptEnabled: false });
  await page.goto(`${base}/admin/login`);
  await page.selectOption('select[name="email"]', email);
  await page.getByRole("button", { name: "Sign in (dev only)" }).click();
  assert.notEqual(new URL(page.url()).pathname, "/admin/login", `Development sign-in: ${email}`);
  return page;
}

async function seed(owner) {
  await owner.goto(`${base}/admin/people`);
  for (const [parent, name, id] of [["", "Matrix org", "matrix-org"], ["matrix-org", "Controls", team],
    ["matrix-org", "Build", siblingTeam], ["", "Separate org", "matrix-tenant"], ["matrix-tenant", "Other", otherTeam]]) {
    await owner.getByText("Add a team", { exact: true }).click();
    await owner.selectOption("#g-parent", parent);
    await owner.fill("#g-name", name);
    await owner.fill("#g-id", id);
    await owner.getByRole("button", { name: "Add team", exact: true }).click();
    assert.match(await owner.locator("main").innerText(), /Team added/);
  }
  for (const [email, group, role] of [[leadEmail, team, "Lead"], [parentLeadEmail, "matrix-org", "Lead"], [memberEmail, team, "Member"],
    [siblingEmail, siblingTeam, "Lead"], [outsiderEmail, otherTeam, "Lead"]]) {
    await owner.fill("#add-list", email);
    await owner.selectOption("#add-team", group);
    await owner.selectOption("#add-role", role);
    await owner.getByRole("button", { name: "Add", exact: true }).click();
    await owner.getByText(email, { exact: true }).first().waitFor();
  }
}

const field = (form, name) => form.locator(`[name="${name}"]`);
const card = (page, title) => page.locator(".dm-card").filter({ has: page.getByRole("heading", { name: title, exact: true }) });
const scoped = (path, teamId = team) => `${path}?teamId=${encodeURIComponent(teamId)}`;

async function formBody(form) {
  return new URLSearchParams(await form.evaluate(element => Array.from(new FormData(element))));
}

function post(page, path, fields) {
  return page.request.post(`${base}${path}`, {
    data: fields.toString(), headers: { "content-type": "application/x-www-form-urlencoded" }, maxRedirects: 0,
  });
}

function replaceFields(fields, key, values) {
  fields.delete(key);
  for (const value of values) fields.append(key, value);
}

async function expectPost(page, path, fields, status, outcome) {
  const response = await post(page, path, fields);
  assert.equal(response.status(), status, `POST ${path} (${outcome ?? status})`);
  if (outcome) assert.equal(new URL(response.headers().location, base).searchParams.get("status"), outcome);
}

async function fill(form, name, values) {
  const inputs = field(form, name);
  assert.equal(await inputs.count(), values.length, name);
  for (const [i, value] of values.entries()) await inputs.nth(i).fill(value);
}

async function check() {
  const anonymous = await browser.newPage();
  assert.equal((await anonymous.goto(`${base}${matrix}`)).status(), 404);
  assert.ok([400, 404].includes((await post(anonymous, `${matrix}/save`, new URLSearchParams())).status()), "anonymous create denied");

  const owner = await signIn(ownerEmail);
  await seed(owner);
  const lead = await signIn(leadEmail);
  const create = lead.locator(`form[action="${matrix}/save"]`);
  for (const [alternatives, criteria] of [[2, 1], [20, 20]]) {
    assert.equal((await lead.goto(`${base}${matrix}?alternatives=${alternatives}&criteria=${criteria}`)).status(), 200);
    assert.equal(await field(create, "alternative").count(), alternatives);
    assert.equal(await field(create, "criterion").count(), criteria);
    assert.equal(await field(create, "score").count(), alternatives * criteria);
    assert.equal(await field(create, "rationale").count(), alternatives * criteria);
  }
  assert.equal((await lead.goto(`${base}${matrix}`)).status(), 200);
  const dimensions = lead.locator(`form[method="get"][action="${matrix}"]`);
  assert.deepEqual(await field(dimensions, "alternatives").locator("option").evaluateAll(options => options.map(o => Number(o.value))),
    Array.from({ length: 19 }, (_, i) => i + 2));
  assert.deepEqual(await field(dimensions, "criteria").locator("option").evaluateAll(options => options.map(o => Number(o.value))),
    Array.from({ length: 20 }, (_, i) => i + 1));
  await field(dimensions, "alternatives").selectOption("3");
  await field(dimensions, "criteria").selectOption("3");
  await dimensions.getByRole("button", { name: "Configure matrix" }).click();
  assert.deepEqual(await field(create, "teamId").locator("option").evaluateAll(options => options.map(o => o.value)), [team]);
  await fill(create, "title", ["Robot design"]);
  await fill(create, "alternative", ["Alpha", "Beta", "Gamma"]);
  await fill(create, "alternativeNotes", ["Prototype", "Incumbent", "TBD"]);
  await fill(create, "criterion", ["Cost", "Reliability", "Speed"]);
  await fill(create, "weight", ["2", "3", "5"]);
  await fill(create, "criterionNotes", ["Budget", "Bench test", "Cycle time"]);
  await fill(create, "score", ["100", "50", "80", "50", "100", "70", "0", "", "100"]);
  await fill(create, "rationale", ["=1+1", "Trial", "Timing", "Estimate", "Tested", "Measured", "No budget", "Pending test", "Fast"]);
  const draft = await formBody(create);
  const invalid = async (change, status = 302, outcome = "invalid") => {
    const fields = new URLSearchParams(draft);
    change(fields);
    await expectPost(lead, `${matrix}/save`, fields, status, outcome);
  };
  await invalid(fields => fields.delete(token), 400, null);
  for (const [key, badValues] of [["weight", ["0", "-1", "10001", "not a weight"]], ["score", ["-1", "101", "42.5"]]]) {
    for (const value of badValues) await invalid(fields => replaceFields(fields, key, [value, ...fields.getAll(key).slice(1)]));
  }
  await invalid(fields => replaceFields(fields, "alternative", ["Alpha", " alpha ", "Gamma"]));
  await invalid(fields => replaceFields(fields, "criterion", ["Cost", "cost", "Speed"]));
  await invalid(fields => fields.delete("rationale"));
  for (const [alternatives, criteria] of [[1, 3], [21, 3], [3, 0], [3, 21]]) {
    await invalid(fields => {
      for (const [key, size, value] of [["alternative", alternatives, "Option"], ["alternativeNotes", alternatives, ""],
        ["criterion", criteria, "Metric"], ["criterionNotes", criteria, ""], ["weight", criteria, "1"],
        ["score", alternatives * criteria, "50"], ["rationale", alternatives * criteria, ""]]) {
        replaceFields(fields, key, Array(size).fill(value));
      }
    });
  }
  await create.getByRole("button", { name: "Save matrix" }).click();
  assert.equal(new URL(lead.url()).searchParams.get("status"), "saved");
  assert.equal(await lead.locator(".dm-card").count(), 1, "only the valid submission created a matrix");
  const saved = card(lead, "Robot design");
  assert.match(await saved.innerText(), /each entered weight ÷ 10/);
  assert.deepEqual(await saved.locator("li").allTextContents(), ["Alpha — 75.0000 (rank 1)", "Beta — 75.0000 (rank 1)", "Gamma — Missing score; not ranked"]);
  const csvPath = await saved.getByRole("link", { name: "Download CSV for Robot design" }).getAttribute("href");
  const idPath = csvPath.split("/export.csv")[0];
  const editPath = `${idPath}/edit`;
  const updatePath = `${idPath}/update`;
  const archivePath = `${idPath}/archive`;
  const restorePath = `${idPath}/restore`;
  const csv = await lead.request.get(`${base}${csvPath}`);
  assert.equal(csv.status(), 200);
  assert.equal(csv.headers()["cache-control"], "no-store");
  assert.match(await csv.text(), /^Alpha,75\.0000,1,Cost,2,100,'=1\+1$/m);
  assert.match(await csv.text(), /^Gamma,,,Reliability,3,,Pending test$/m);

  await lead.goto(`${base}${matrix}?alternatives=20&criteria=20`);
  const maxForm = lead.locator(`form[action="${matrix}/save"]`);
  const maximum = await formBody(maxForm);
  maximum.set("teamId", team);
  maximum.set("title", "Maximum dimensions");
  replaceFields(maximum, "alternative", Array.from({ length: 20 }, (_, i) => `Option ${i + 1}`));
  replaceFields(maximum, "alternativeNotes", Array(20).fill(""));
  replaceFields(maximum, "criterion", Array.from({ length: 20 }, (_, i) => `Metric ${i + 1}`));
  replaceFields(maximum, "weight", Array(20).fill("1"));
  replaceFields(maximum, "criterionNotes", Array(20).fill(""));
  replaceFields(maximum, "score", Array(400).fill(""));
  replaceFields(maximum, "rationale", Array(400).fill(""));
  await expectPost(lead, `${matrix}/save`, maximum, 302, "saved");
  assert.equal((await lead.goto(`${base}${matrix}`)).status(), 200);
  assert.equal(await card(lead, "Maximum dimensions").count(), 1, "20×20 matrix saves across the default form-field limit");
  const csvResponseHeaders = await lead.request.get(`${base}${csvPath}`);
  assert.equal(csvResponseHeaders.status(), 200, "existing matrix remains available after max-size save");
  const parentLead = await signIn(parentLeadEmail);
  await parentLead.goto(`${base}${matrix}`);
  assert.equal(await card(parentLead, "Robot design").count(), 1, "parent lead can see a child-team matrix");
  assert.equal((await parentLead.goto(`${base}${scoped(editPath)}`)).status(), 200, "parent lead can edit a child-team matrix");
  await parentLead.context().close();

  const member = await signIn(memberEmail);
  await member.goto(`${base}${matrix}`);
  assert.equal(await card(member, "Robot design").count(), 1);
  assert.equal(await member.getByRole("link", { name: "Edit" }).count(), 0);
  assert.equal(await member.getByRole("button", { name: "Archive" }).count(), 0);
  assert.equal((await member.request.get(`${base}${csvPath}`)).status(), 200);
  for (const page of [anonymous, member]) assert.equal((await page.goto(`${base}${scoped(editPath)}`)).status(), 404);
  await member.goto(`${base}${matrix}`);
  const memberFields = new URLSearchParams(draft);
  memberFields.set(token, await field(member.locator('form[action="/admin/logout"]').first(), token).inputValue());
  for (const path of [`${matrix}/save`, updatePath, archivePath]) await expectPost(member, path, memberFields, 404);

  assert.equal((await owner.goto(`${base}${scoped(editPath)}`)).status(), 200, "site owner can edit across organizations");
  const wrongTeam = await formBody(owner.locator(`form[action="${updatePath}"]`));
  wrongTeam.set("teamId", siblingTeam);
  await expectPost(owner, updatePath, wrongTeam, 404);

  for (const [email, ownTeam] of [[siblingEmail, siblingTeam], [outsiderEmail, otherTeam]]) {
    const page = await signIn(email);
    await page.goto(`${base}${matrix}`);
    assert.equal(await card(page, "Robot design").count(), 0);
    const fields = new URLSearchParams(draft);
    fields.set(token, await field(page.locator(`form[action="${matrix}/save"]`), token).inputValue());
    await expectPost(page, `${matrix}/save`, fields, 404);
    for (const scope of [team, ownTeam]) {
      fields.set("teamId", scope);
      assert.equal((await page.goto(`${base}${scoped(editPath, scope)}`)).status(), 404);
      assert.equal((await page.request.get(`${base}${scoped(`${idPath}/export.csv`, scope)}`)).status(), 404);
      await expectPost(page, updatePath, fields, 404);
      await expectPost(page, archivePath, fields, 404);
    }
    await page.context().close();
  }

  await saved.getByRole("link", { name: "Edit Robot design" }).click();
  const edit = lead.locator(`form[action="${updatePath}"]`);
  const before = await formBody(edit);
  before.delete(token);
  draft.delete(token);
  for (const key of new Set(draft.keys())) assert.deepEqual(before.getAll(key), draft.getAll(key), `prefilled ${key}`);
  await field(edit, "title").fill("Revised robot design");
  await field(edit, "alternative").nth(2).fill("Delta");
  await field(edit, "criterion").nth(1).fill("Durability");
  await field(edit, "weight").nth(1).fill("5.5");
  await field(edit, "score").nth(7).fill("100");
  await field(edit, "rationale").nth(7).fill("Repeatable");
  const revision = await formBody(edit);
  const noToken = new URLSearchParams(revision);
  noToken.delete(token);
  await expectPost(lead, updatePath, noToken, 400);
  const badRevision = new URLSearchParams(revision);
  replaceFields(badRevision, "weight", ["0", "1", "1"]);
  await expectPost(lead, updatePath, badRevision, 302, "invalid");
  await edit.getByRole("button", { name: "Save changes" }).click();
  const updated = card(lead, "Revised robot design");
  assert.match(await updated.innerText(), /Edited by matrix-lead@example\.com/);
  assert.match(await updated.innerText(), /each entered weight ÷ 12\.5/);
  assert.deepEqual(await updated.locator("li").allTextContents(), ["Delta — 84.0000 (rank 1)", "Beta — 80.0000 (rank 2)", "Alpha — 70.0000 (rank 3)"]);
  assert.equal(await card(lead, "Robot design").count(), 0);
  assert.equal(await updated.getByRole("link", { name: "Download CSV for Revised robot design" }).getAttribute("href"), csvPath, "edit preserves ID and team");
  const updatedCsv = await lead.request.get(`${base}${csvPath}`);
  assert.equal(updatedCsv.status(), 200);
  assert.match(await updatedCsv.text(), /^Delta,84\.0000,1,Durability,5\.5,100,Repeatable$/m);
  await expectPost(lead, updatePath, revision, 409);

  const archive = updated.locator(`form[action="${archivePath}"]`);
  const archiveToken = await field(archive, token).inputValue();
  await expectPost(lead, archivePath, new URLSearchParams({ teamId: team }), 400);
  await archive.getByRole("button", { name: "Archive Revised robot design" }).click();
  const archived = card(lead, "Revised robot design (archived)");
  assert.match(await archived.innerText(), /Archived by matrix-lead@example\.com/);
  assert.equal(await archived.getByRole("link", { name: "Edit Revised robot design" }).count(), 0);
  assert.equal((await lead.request.get(`${base}${csvPath}`)).status(), 200, "authorized CSV export remains available after archive");
  await expectPost(lead, archivePath, new URLSearchParams({ teamId: team, [token]: archiveToken }), 404);
  assert.equal((await lead.goto(`${base}${scoped(editPath)}`)).status(), 404);
  await expectPost(lead, updatePath, revision, 404);
  await lead.goto(`${base}${matrix}`);
  await archived.locator(`form[action="${restorePath}"]`).getByRole("button", { name: "Restore Revised robot design" }).click();
  const restored = card(lead, "Revised robot design");
  assert.match(await restored.innerText(), /Restored by matrix-lead@example\.com/);
  assert.equal(await card(lead, "Revised robot design (archived)").count(), 0);
  console.log("PASS live decision matrices: configurable 2×1–20×20 creation, weighted ties/missing scores, validation, CSV safety, edit concurrency, archive/restore, antiforgery, and access isolation");
}

try {
  await stage();
  await start();
  browser = await chromium.launch();
  await check();
} catch (error) {
  if (log) console.error(log);
  throw error;
} finally {
  try {
    await browser?.close();
  } finally {
    if (server) {
      server.kill();
      const deadline = setTimeout(() => server.kill("SIGKILL"), 5_000);
      try { await stopped; } finally { clearTimeout(deadline); }
    }
    await rm(fixture, { recursive: true, force: true });
  }
}
