// Fresh Development DB E2E for Project Plans; fixture-only module registration.
import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const source = path.join(repo, "src/AviSite.Web");
const obj = path.join(source, "obj");
fs.mkdirSync(obj, { recursive: true });
const fixture = fs.mkdtempSync(path.join(obj, "projects-e2e-"));
const project = path.join(fixture, "src/AviSite.Web");
const data = path.join(fixture, "data");
const groups = [
  { id: "avi", name: "AVI Robotics" },
  { id: "sms", name: "Sequoia Marine Solutions", parent: "avi" },
  { id: "sms-sibling", name: "SMS Sibling", parent: "avi" },
  { id: "partner", name: "Partner Robotics" },
];
const accounts = [
  ["owner", "avi", "Lead"], ["team-manager", "sms", "Lead"], ["sibling-manager", "sms-sibling", "Lead"],
  ["member", "sms", "Member"], ["other-tenant", "partner", "Lead"],
].map(([name, group, role]) => ({ name, email: `${name}@example.test`, stamp: name, role: "Member", teams: [{ group, role }] }));
let child, base, logs = "", count = 0;
const pass = text => { console.log(`ok ${++count} - ${text}`); };
const pause = ms => new Promise(r => setTimeout(r, ms));
function stage() {
  const ignored = new Set(["bin", "obj", "App_Data", "Program.cs"]);
  fs.mkdirSync(project, { recursive: true });
  for (const name of fs.readdirSync(source)) {
    if (ignored.has(name) || name.startsWith(".env")) continue;
    fs.cpSync(path.join(source, name), path.join(project, name), { recursive: true, filter: p => !path.basename(p).startsWith(".env") });
  }
  fs.cpSync(path.join(repo, "content"), path.join(fixture, "content"), { recursive: true });
  let code = fs.readFileSync(path.join(source, "Program.cs"), "utf8");
  function register(anchor, line) { if (!code.includes(line)) { assert.equal(code.split(anchor).length, 2); code = code.replace(anchor, `${line}\n${anchor}`); } }
  register("var app = builder.Build();", "builder.Services.AddProjectPlanModule();");
  register("app.MapRazorComponents<App>();", "app.MapProjectPlanEndpoints();");
  fs.writeFileSync(path.join(project, "ProjectsIntegration.cs"), code);
  fs.mkdirSync(data);
  fs.writeFileSync(path.join(data, "accounts.json"), JSON.stringify({ groups, accounts }));
  const built = spawnSync("dotnet", ["build", "-c", "Release", "--nologo"], { cwd: project, stdio: "inherit" });
  if (built.error) throw built.error;
  assert.equal(built.status, 0, "fixture Release build");
}
async function start() {
  const s = net.createServer();
  await new Promise((resolve, reject) => s.once("error", reject).listen(0, "127.0.0.1", resolve));
  base = `http://127.0.0.1:${s.address().port}`;
  await new Promise(r => s.close(r));
  const { PATH, HOME, TMPDIR, DOTNET_ROOT } = process.env;
  child = spawn("dotnet", ["bin/Release/net10.0/AviSite.Web.dll", "--urls", base], { cwd: project,
    env: { PATH, HOME, ...(TMPDIR ? { TMPDIR } : {}), ...(DOTNET_ROOT ? { DOTNET_ROOT } : {}), DOTNET_ENVIRONMENT: "Development", Store__DataDirectory: data, PayPal__Mode: "Off", SmsSite__AviBaseUrl: base }, stdio: ["ignore", "pipe", "pipe"] });
  child.stdout.on("data", b => { logs = (logs + b).slice(-20000); }); child.stderr.on("data", b => { logs = (logs + b).slice(-20000); });
  for (let i = 0; i < 300 && child.exitCode === null; i++) { try { if ((await fetch(base + "/healthz", { signal: AbortSignal.timeout(1000) })).ok) return; } catch {} await pause(100); }
  throw new Error("Development fixture failed to start");
}
async function stop() { if (!child || child.exitCode !== null) return; const done = new Promise(r => child.once("exit", r)); child.kill("SIGTERM"); const timer = setTimeout(() => child.kill("SIGKILL"), 4000); await done; clearTimeout(timer); }
function client() {
  const jar = new Map();
  const request = async (url, init = {}) => { const response = await fetch(base + url, { ...init, redirect: "manual", signal: AbortSignal.timeout(10000), headers: { ...init.headers, cookie: [...jar].map(([k,v]) => `${k}=${v}`).join("; ") } });
    for (const c of response.headers.getSetCookie()) { const pair = c.split(";", 1)[0], i = pair.indexOf("="); jar.set(pair.slice(0, i), pair.slice(i + 1)); } return response; };
  return { get: url => request(url), post: (url, body) => request(url, { method: "POST", body: new URLSearchParams(body) }), jar };
}
async function expect(promise, status) { const r = await promise, text = await r.text(); assert.equal(r.status, status, `${r.url} status ${r.status}: ${text.slice(0, 500)}`); return { text, location: r.headers.get("location"), headers: r.headers }; }
function form(html, action) { const m = [...html.matchAll(/<form\b[^>]*>[\s\S]*?<\/form>/g)].find(x => x[0].includes(`action="${action}"`)); assert.ok(m, `form ${action}`); return m[0]; }
function value(html, name) { const m = html.match(new RegExp(`<input[^>]*name="${name}"[^>]*value="([^"]*)"`)) || html.match(new RegExp(`<input[^>]*value="([^"]*)"[^>]*name="${name}"`)); assert.ok(m, `field ${name}`); return m[1]; }
function csrf(html) { return value(html, "__RequestVerificationToken"); }
async function login(name) { const c = client(), html = (await expect(c.get("/admin/login"), 200)).text; await expect(c.post("/admin/dev-login", { email: `${name}@example.test`, __RequestVerificationToken: csrf(form(html, "/admin/dev-login")) }), 302); assert.ok(c.jar.has("avi_admin")); return c; }
function tokens(html) { return { __RequestVerificationToken: csrf(html) }; }
function projectPayload(html, title, description = "Plan detail") { return { ...tokens(form(html, "/admin/projects/save")), teamId: "sms", title, description }; }
function taskPayload(html, action, task = {}) { const f = form(html, action); return { ...tokens(f), title: "Milestone", ownerEmail: "member@example.test", status: "In progress", startDate: "2026-01-01", dueDate: "2026-01-10", progress: "35", description: "Task detail", ...task }; }
try {
  stage(); await start();
  const anonymous = client(); await expect(anonymous.get("/admin/projects"), 404); await expect(anonymous.post("/admin/projects/save", { teamId: "sms", title: "No", description: "", __RequestVerificationToken: "invalid" }), 400); pass("anonymous access denied");
  const [manager, owner, sibling, member, other] = await Promise.all(["team-manager", "owner", "sibling-manager", "member", "other-tenant"].map(login));
  let html = (await expect(manager.get("/admin/projects"), 200)).text;
  assert.match(html, /No active projects/);
  const create = await expect(manager.post("/admin/projects/save", projectPayload(html, "Delivery Plan")), 302);
  assert.match(create.location, /result=saved/); const id = new URL(create.location, base).searchParams.get("id"); assert.match(id, /^[a-f0-9]{32}$/);
  html = (await expect(manager.get("/admin/projects"), 200)).text; assert.match(html, /Delivery Plan/);
  const edit = form(html, "/admin/projects/save");
  const editPayload = { ...tokens(edit), id, teamId: "sms", title: "Delivery Plan Revised", description: "Updated" };
  await expect(manager.post("/admin/projects/save", editPayload), 302);
  html = (await expect(manager.get("/admin/projects"), 200)).text; assert.match(html, /Delivery Plan Revised/);
  const taskAction = `/admin/projects/${id}/task`;
  let payload = taskPayload(html, taskAction, { title: "Design", startDate: "2026-01-01", dueDate: "2026-01-10", progress: "20" });
  await expect(manager.post(taskAction, payload), 302);
  html = (await expect(manager.get("/admin/projects"), 200)).text; assert.match(html, /Design/);
  payload = taskPayload(html, taskAction, { title: "Build", startDate: "2026-01-05", dueDate: "2026-01-20", progress: "65" });
  // Select the created task ID from its edit form; dependency points to it.
  const editTask = [...html.matchAll(/<form\b[^>]*>[\s\S]*?<\/form>/g)].map(m => m[0]).find(f => f.includes(`action="${taskAction}"`) && f.includes('name="taskId"')); assert.ok(editTask);
  const firstId = value(editTask, "taskId"); payload.dependency = firstId;
  await expect(manager.post(taskAction, payload), 302);
  html = (await expect(manager.get("/admin/projects"), 200)).text; assert.match(html, /Build/); assert.match(html, /Depends on: Design/);
  for (const invalid of [{ startDate: "2026-02-30" }, { startDate: "2026-02-10", dueDate: "2026-02-09" }]) {
    await expect(manager.post(taskAction, taskPayload(html, taskAction, { title: "Invalid", ...invalid })), 302);
    html = (await expect(manager.get("/admin/projects"), 200)).text; assert.doesNotMatch(html, /<strong>Invalid<\/strong>/);
  }
  await expect(manager.post(taskAction, taskPayload(html, taskAction, { title: "Self", taskId: firstId, dependency: firstId })), 302);
  assert.match((await expect(manager.get("/admin/projects"), 200)).text, /Build/);
  // Attempt cycle: Design depends on Build, while Build already depends on Design.
  const designEdit = [...html.matchAll(/<form\b[^>]*>[\s\S]*?<\/form>/g)].map(m=>m[0]).find(f=>f.includes(`action="${taskAction}"`)&&f.includes(`value="${firstId}"`)); assert.ok(designEdit);
  const secondId = [...html.matchAll(/name="taskId" value="([a-f0-9]{32})"/g)].map(m=>m[1]).find(x=>x!==firstId); assert.ok(secondId);
  await expect(manager.post(taskAction, taskPayload(html, taskAction, { taskId: firstId, title: "Design", dependency: secondId })), 302);
  html = (await expect(manager.get("/admin/projects"), 200)).text; assert.match(html, /Depends on: Design/);
  const layout = html.match(/class="gantt-bar [^"]+">20%<\/span>/); assert.ok(layout, "Gantt bar exposes proportional position/length classes");
  assert.match(html, /Recent project history/); assert.match(html, /TaskSaved/); assert.match(html, /Created/);
  assert.match(html, /name="filter"/);
  const filtered = (await expect(manager.get("/admin/projects?filter=next"), 200)).text; assert.match(filtered, /Gantt timeline/);
  pass("manager creates/edits plan and tasks; validates dates, rejects self/cycles; renders Gantt, filter, dependencies and audit history");
  const guestRead = await expect(member.get("/admin/projects"), 200); assert.match(guestRead.text, /Delivery Plan Revised/);
  for (const c of [member, sibling, other]) {
    const ownPage = await expect(c.get("/admin/projects"), 200);
    await expect(c.post("/admin/projects/save", { id, teamId: "sms", title: "Intrusion", description: "", __RequestVerificationToken: csrf(form(ownPage.text, "/admin/projects/save")) }), 404);
  }
  const siblingPage = await expect(sibling.get("/admin/projects"), 200); assert.doesNotMatch(siblingPage.text, /Delivery Plan/);
  const otherPage = await expect(other.get("/admin/projects"), 200); assert.doesNotMatch(otherPage.text, /Delivery Plan/);
  assert.match((await expect(owner.get("/admin/projects"), 200)).text, /Delivery Plan Revised/);
  await expect(member.post(taskAction, { ...taskPayload(guestRead.text, "/admin/projects/save", { progress: "0" }), __RequestVerificationToken: csrf(form(guestRead.text, "/admin/projects/save")) }), 404);
  pass("member writes denied; sibling team and other tenant isolated; parent owner can see child project");
  const archiveForm = form(html, `/admin/projects/${id}/archive`);
  await expect(manager.post(`/admin/projects/${id}/archive`, tokens(archiveForm)), 302);
  assert.doesNotMatch((await expect(manager.get("/admin/projects"), 200)).text, /Delivery Plan Revised/);
  pass("manager archives project");
  console.log(`Projects E2E: ${count} scenarios passed on a fresh Development database.`);
} catch (error) { if (logs) console.error(logs); throw error; }
finally { await stop(); fs.rmSync(fixture, { recursive: true, force: true }); }
