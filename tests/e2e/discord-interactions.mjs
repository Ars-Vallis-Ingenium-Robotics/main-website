import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { generateKeyPairSync, sign as cryptoSign } from "node:crypto";
import { DatabaseSync } from "node:sqlite";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const scratchBase = process.env.TMPDIR || path.join(os.homedir(), ".hermes", "cache", "scratch");
fs.mkdirSync(scratchBase, { recursive: true });
const scratch = fs.mkdtempSync(path.join(scratchBase, "discord-hours-e2e-"));
const dataDir = path.join(scratch, "data");
fs.mkdirSync(dataDir, { recursive: true });
const appId = "323456789012345678";
const selfId = "123456789012345678";
const otherId = "223456789012345678";
const { publicKey, privateKey } = generateKeyPairSync("ed25519");
const publicHex = publicKey.export({ format: "der", type: "spki" }).subarray(-32).toString("hex");
const port1 = 20_000 + Math.floor(Math.random() * 19_998);
const baseUrl = `http://127.0.0.1:${port1}`;
const baseUrl2 = `http://127.0.0.1:${port1 + 1}`;
let app;
let app2;
let logs = "";
let success = false;

function writeSeed() {
  const accounts = {
    accounts: [
      { email: "self@example.test", name: "Private Self Name", role: "Member", teams: [{ group: "avi", role: "Lead" }], stamp: "self" },
      { email: "other@example.test", name: "Private Other Name", role: "Member", teams: [{ group: "avi", role: "Member" }], stamp: "other" },
    ],
    groups: [{ id: "avi", name: "AVI Robotics" }, { id: "sms", name: "Sequoia Marine Solutions", parent: "avi" },
      ...Array.from({ length: 26 }, (_, i) => ({ id: `extra${String(i).padStart(2, "0")}`, name: `ZZ Extra Team ${String(i).padStart(2, "0")}`, parent: "avi" }))],
  };
  fs.writeFileSync(path.join(dataDir, "accounts.json"), JSON.stringify(accounts));
}

function signedHeaders(body, { stale = false, tamper = false } = {}) {
  const now = Math.floor(Date.now() / 1000);
  const timestamp = String(stale ? now - 600 : now);
  const signature = cryptoSign(null, Buffer.concat([Buffer.from(timestamp), Buffer.from(body)]), privateKey).toString("hex");
  return {
    "content-type": "application/json",
    "x-signature-timestamp": timestamp,
    "x-signature-ed25519": tamper ? `${signature[0] === "0" ? "1" : "0"}${signature.slice(1)}` : signature,
  };
}

async function post(payload, opts = {}, url = baseUrl) {
  const body = typeof payload === "string" ? payload : JSON.stringify(payload);
  return fetch(`${url}/discord/interactions`, { method: "POST", headers: signedHeaders(body, opts), body });
}

function command(name, userId, options = [], applicationId = appId) {
  return {
    application_id: applicationId, type: 2, version: 1,
    data: { id: "423456789012345678", name, type: 1, options },
    member: { user: { id: userId } },
  };
}

async function expectEphemeral(response, contains) {
  if (response.status !== 200) throw new Error(`expected 200, got ${response.status}: ${await response.text()}`);
  const json = await response.json();
  assert.equal(json.type, 4, "slash command must return an interaction message");
  assert.equal(json.data?.flags, 64, "every slash-command reply must be ephemeral");
  assert.equal(typeof json.data?.content, "string");
  if (contains) assert.ok(json.data.content.includes(contains), `reply did not contain ${JSON.stringify(contains)}: ${json.data.content}`);
  return json.data.content;
}

async function expectPrompt(response, contains) {
  if (response.status !== 200) throw new Error(`expected 200, got ${response.status}: ${await response.text()}`);
  const json = await response.json();
  assert.equal(json.type, 4, "clock-in prompts must create a private interaction message");
  assert.equal(json.data?.flags, 64, "clock-in prompts must be ephemeral");
  assert.ok(Array.isArray(json.data?.components) && json.data.components.length > 0, "clock-in prompt must have interactive components");
  if (contains) assert.ok(json.data.content.includes(contains), `prompt did not contain ${JSON.stringify(contains)}: ${json.data.content}`);
  return json.data;
}

async function expectUpdated(response, contains) {
  if (response.status !== 200) throw new Error(`expected 200, got ${response.status}: ${await response.text()}`);
  const json = await response.json();
  assert.equal(json.type, 7, "clock-in component actions must update the same private prompt");
  assert.equal(typeof json.data?.content, "string");
  if (contains) assert.ok(json.data.content.includes(contains), `updated prompt did not contain ${JSON.stringify(contains)}: ${json.data.content}`);
  return json.data;
}

async function waitReady(proc, url) {
  const deadline = Date.now() + 45_000;
  while (Date.now() < deadline) {
    if (proc.exitCode !== null) throw new Error(`app exited early (${proc.exitCode}); ${logs}`);
    try {
      const response = await fetch(`${url}/healthz`);
      if (response.ok) return;
    } catch { /* still starting */ }
    await new Promise(resolve => setTimeout(resolve, 250));
  }
  throw new Error(`app did not become ready at ${url}; ${logs}`);
}

async function stopProcess(proc) {
  if (!proc || proc.exitCode !== null) return;
  proc.kill("SIGTERM");
  await Promise.race([
    new Promise(resolve => proc.once("exit", resolve)),
    new Promise(resolve => setTimeout(resolve, 5_000)),
  ]);
  if (proc.exitCode === null) proc.kill("SIGKILL");
}

function startApp(url) {
  const env = { ...process.env };
  // Keep local interaction tests isolated even when the test runner inherited real environment secrets.
  delete env.Discord__ClientSecret;
  delete env.Discord__RedirectUri;
  delete env.Discord__BotToken;
  Object.assign(env, {
    ASPNETCORE_ENVIRONMENT: "Development",
    ASPNETCORE_URLS: url,
    Store__DataDirectory: dataDir,
    Discord__ApplicationId: appId,
    Discord__PublicKey: publicHex,
    Discord__PublicBaseUrl: baseUrl,
    DOTNET_CLI_TELEMETRY_OPTOUT: "1",
    DOTNET_NOLOGO: "1",
  });
  const proc = spawn("dotnet", ["run", "--no-build", "--no-launch-profile", "--configuration", "Release", "--project", "src/AviSite.Web/AviSite.Web.csproj"], {
    cwd: repo,
    env,
    stdio: ["ignore", "pipe", "pipe"],
  });
  proc.stdout.on("data", chunk => { logs += chunk.toString(); });
  proc.stderr.on("data", chunk => { logs += chunk.toString(); });
  return proc;
}

try {
  writeSeed();
  app = startApp(baseUrl);
  app2 = startApp(baseUrl2);
  await Promise.all([waitReady(app, baseUrl), waitReady(app2, baseUrl2)]);

  const db = new DatabaseSync(path.join(dataDir, "site.db"));
  const link = db.prepare("INSERT INTO discord_links (email, discord_user_id) VALUES (?, ?)");
  link.run("self@example.test", selfId);
  link.run("other@example.test", otherId);
  assert.throws(() => link.run("intruder@example.test", selfId), /UNIQUE constraint failed/, "SQLite must enforce one Discord ID per AVI account even across app instances");
  const now = Date.now();
  const selfStart = new Date(now - 3 * 60 * 60 * 1000).toISOString();
  const otherStart = new Date(now - 6 * 60 * 60 * 1000).toISOString();
  const end = new Date(now - 60 * 60 * 1000).toISOString();
  const created = new Date(now).toISOString();
  const insert = db.prepare("INSERT INTO hours (email, team, category, start_utc, end_utc, note, source, created_utc, updated_utc, updated_by) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)");
  insert.run("self@example.test", "avi", "Build", selfStart, end, "PRIVATE_SELF_NOTE", "manual", created, created, "self@example.test");
  insert.run("other@example.test", "avi", "Outreach", otherStart, end, "PRIVATE_OTHER_NOTE", "manual", created, created, "other@example.test");
  db.close();

  const unsigned = await fetch(`${baseUrl}/discord/interactions`, { method: "POST", headers: { "content-type": "application/json" }, body: "{}" });
  const unsignedBody = await unsigned.text();
  assert.equal(unsigned.status, 401, `unsigned interactions must be rejected (body: ${unsignedBody})`);
  const tampered = await post({ application_id: appId, type: 1 }, { tamper: true });
  assert.equal(tampered.status, 401, "invalid signatures must be rejected");
  const stale = await post({ application_id: appId, type: 1 }, { stale: true });
  assert.equal(stale.status, 401, "stale signatures must be rejected");

  const ping = await post({ application_id: appId, type: 1, version: 1 });
  assert.equal(ping.status, 200);
  assert.deepEqual(await ping.json(), { type: 1 }, "PING must be acknowledged with type 1");
  const wrongApp = await post({ application_id: "423456789012345678", type: 1, version: 1 });
  assert.equal(wrongApp.status, 401, "signed requests for a different app must be rejected");
  await expectEphemeral(await post(command("myhours", "333456789012345678")), "Link your Discord account");
  const quickLinksResponse = await post(command("link", "333456789012345678"));
  assert.equal(quickLinksResponse.status, 200);
  const quickLinks = await quickLinksResponse.json();
  assert.equal(quickLinks.type, 4, "link command must return an interaction message");
  assert.equal(quickLinks.data?.flags, 64, "website links must remain private");
  assert.match(quickLinks.data?.content ?? "", /Quick links to AVI Hours/);
  const websiteButtons = quickLinks.data.components[0].components;
  assert.deepEqual(websiteButtons.map(button => [button.type, button.style, button.label, button.url]), [
    [2, 5, "Open Hours", `${baseUrl}/admin/hours`],
    [2, 5, "Link Discord", `${baseUrl}/admin/discord`],
  ], "unlinked users must get mobile-friendly website and account-link URL buttons");
  assert.ok(websiteButtons.every(button => !("custom_id" in button)), "URL buttons must not masquerade as interaction buttons");
  const malformed = await post("{");
  assert.equal(malformed.status, 400, "signed malformed JSON must be rejected");

  const ownHours = await expectEphemeral(await post(command("myhours", selfId)), "2.0 h");
  assert.ok(ownHours.includes("Build"));
  assert.ok(!ownHours.includes("Outreach"), "personal hours must not include another account's category");
  assert.ok(!/example\.test|Private (Self|Other) Name|PRIVATE_.*_NOTE|123456789012345678/.test(ownHours), "personal reply must not expose account identifiers or notes");
  const secondInstanceHours = await expectEphemeral(await post(command("myhours", selfId), {}, baseUrl2), "2.0 h");
  assert.ok(secondInstanceHours.includes("Build"), "second app instance must resolve the same unique link from shared SQLite");

  const otherHours = await expectEphemeral(await post(command("myhours", otherId)), "5.0 h");
  assert.ok(otherHours.includes("Outreach"));
  assert.ok(!otherHours.includes("Build"), "the second account must see only its own hours");

  const clock = await expectEphemeral(await post(command("clockstatus", selfId)), "not clocked in");
  assert.ok(!/example\.test|PRIVATE_.*_NOTE/.test(clock));

  const denied = await expectEphemeral(await post(command("teamhours", otherId)), "only to a linked team lead");
  assert.ok(!/Private (Self|Other) Name|example\.test|PRIVATE_.*_NOTE/.test(denied));

  const team = await expectEphemeral(await post(command("teamhours", selfId, [{ name: "team", type: 3, value: "avi" }])), "7.0 h");
  assert.ok(!team.includes("Build") && !team.includes("Outreach"), "team output must be total-only, without category detail");
  assert.ok(!/Private (Self|Other) Name|example\.test|PRIVATE_.*_NOTE|123456789012345678|223456789012345678/.test(team), "team reply must be aggregate-only");

  // ---- Bot management: allowed servers, command channels, clock-panel buttons, pause ----
  const guildId = "523456789012345678";
  const channelId = "623456789012345678";
  const otherChannel = "723456789012345678";
  const inGuild = (payload, channel = channelId, guild = guildId) => ({ ...payload, guild_id: guild, channel_id: channel });
  const button = (customId, userId) => ({ application_id: appId, type: 3, version: 1, data: { custom_id: customId, component_type: 2 }, member: { user: { id: userId } } });
  const component = (customId, userId, componentType, values = []) => ({ application_id: appId, type: 3, version: 1, data: { custom_id: customId, component_type: componentType, ...(componentType === 3 ? { values } : {}) }, member: { user: { id: userId } } });

  // No servers listed yet: any server works.
  await expectEphemeral(await post(inGuild(command("myhours", selfId), channelId, "823456789012345678")), "2.0 h");

  const admin = new DatabaseSync(path.join(dataDir, "site.db"));
  admin.prepare("INSERT INTO discord_guilds (guild_id, name, team, command_channels, added_by, added_utc) VALUES (?, ?, ?, ?, ?, ?)")
    .run(guildId, "Test server", "avi", channelId, "owner@example.test", new Date().toISOString());

  await expectEphemeral(await post(inGuild(command("myhours", selfId), channelId, "823456789012345678")), "isn't enabled in this server");
  const wrongChannel = await expectEphemeral(await post(inGuild(command("myhours", selfId), otherChannel)), `<#${channelId}>`);
  assert.ok(!wrongChannel.includes("2.0 h"), "a command in the wrong channel must not answer");
  await expectEphemeral(await post(inGuild(command("myhours", selfId))), "2.0 h");
  await expectEphemeral(await post(command("myhours", selfId)), "2.0 h"); // DMs stay personal and keep working
  await expectEphemeral(await post(inGuild(command("teamhours", selfId))), "7.0 h"); // defaults to the server's team

  // /clock asks users with multiple teams to choose; category and team can each be added later.
  const clockSubcommand = (name, userId) => command("clock", userId, [{ type: 1, name }]);
  const teamPrompt = await expectPrompt(await post(inGuild(clockSubcommand("in", selfId))), "Choose a team");
  assert.match(teamPrompt.content, /Add later.*AVI Robotics/, "team Add later explains which default team will be used");
  const teamSelect = teamPrompt.components[0].components[0];
  assert.equal(teamSelect.type, 3, "multiple teams must be offered as a select menu");
  assert.equal(teamSelect.options.length, 25, "team menus must respect Discord's 25-option limit");
  assert.ok(teamSelect.options.some(o => o.value === "team:avi") && teamSelect.options.some(o => o.value === "team:sms"));
  const nextTeams = teamPrompt.components[1].components.find(c => c.label === "Next");
  assert.ok(nextTeams && !nextTeams.disabled, "large team lists must offer another page");
  const expiringPrompt = await expectPrompt(await post(inGuild(clockSubcommand("in", selfId))), "Choose a team");
  const expiredTeamId = expiringPrompt.components[0].components[0].custom_id.split(":");
  expiredTeamId[expiredTeamId.length - 1] = "1";
  await expectUpdated(await post(inGuild(component(expiredTeamId.join(":"), selfId, 3, ["team:sms"]))), "expired");
  const teamPageTwo = await expectUpdated(await post(inGuild(component(nextTeams.custom_id, selfId, 2))), "Choose a team");
  assert.equal(teamPageTwo.components[0].components[0].options.length, 3);
  assert.ok(teamPageTwo.components[1].components.some(c => c.label === "Add later"), "Add later remains available on every team page");
  const previousTeams = teamPageTwo.components[1].components.find(c => c.label === "Previous");
  const teamPageOne = await expectUpdated(await post(inGuild(component(previousTeams.custom_id, selfId, 2))), "Choose a team");
  const firstPageTeamSelect = teamPageOne.components[0].components[0];
  const teamLaterButton = teamPageOne.components[1].components.find(c => c.label === "Add later");
  assert.ok(teamLaterButton, "team prompt must include Add later");
  const invalidTeam = await expectUpdated(await post(inGuild(component(firstPageTeamSelect.custom_id, selfId, 3, ["team:forged"]))), "no longer available");
  const refreshedTeamSelect = invalidTeam.components[0].components[0];

  const wrongOwner = await expectEphemeral(await post(inGuild(component(refreshedTeamSelect.custom_id, otherId, 3, ["team:sms"]))), "different Discord account");
  assert.ok(wrongOwner);
  const categoryPrompt = await expectUpdated(await post(inGuild(component(refreshedTeamSelect.custom_id, selfId, 3, ["team:sms"]))), "What category");
  assert.ok(categoryPrompt.content.includes("Sequoia Marine Solutions"), "selected team controls the category prompt");
  const categorySelect = categoryPrompt.components[0].components[0];
  const programmingOption = categorySelect.options.find(o => o.label === "Programming");
  assert.ok(programmingOption && programmingOption.value.length <= 100, "category option values fit Discord's limit");
  const invalidCategory = await expectUpdated(await post(inGuild(component(categorySelect.custom_id, selfId, 3, ["category:forged"]))), "no longer available");
  const refreshedCategorySelect = invalidCategory.components[0].components[0];
  const refreshedProgrammingOption = refreshedCategorySelect.options.find(o => o.label === "Programming");
  assert.ok(refreshedProgrammingOption, "invalid choices must leave a usable category menu");
  const clockedIn = await expectUpdated(await post(inGuild(component(refreshedCategorySelect.custom_id, selfId, 3, [refreshedProgrammingOption.value]))), "Clocked in · AVI Robotics › Sequoia Marine Solutions · Programming");
  assert.ok(clockedIn.content.includes("/admin/hours"), "the confirmation links to later edits");
  const selectedEntry = admin.prepare("SELECT team, category FROM hours WHERE email = ? AND end_utc IS NULL").get("self@example.test");
  assert.equal(selectedEntry.team, "sms");
  assert.equal(selectedEntry.category, "Programming");
  await expectEphemeral(await post(inGuild(clockSubcommand("status", selfId))), "Clocked in");
  await expectEphemeral(await post(inGuild(clockSubcommand("out", selfId))), "Clocked out");
  await expectEphemeral(await post(inGuild(clockSubcommand("out", selfId))), "not clocked in");

  // A single-team member skips the team question but still gets the category choice.
  const singleTeamPrompt = await expectPrompt(await post(clockSubcommand("in", otherId)), "What category");
  assert.ok(!singleTeamPrompt.content.includes("Choose a team"));
  assert.equal(singleTeamPrompt.components[0].components[0].options.length, 10);
  const otherCategoryMenu = singleTeamPrompt.components[0].components[0];
  const outreachOption = otherCategoryMenu.options.find(o => o.label === "Outreach");
  assert.ok(outreachOption);
  await expectUpdated(await post(component(otherCategoryMenu.custom_id, otherId, 3, [outreachOption.value])), "Clocked in · AVI Robotics · Outreach");
  await expectEphemeral(await post(clockSubcommand("out", otherId)), "Clocked out");

  // Clock panel buttons work in any channel of a listed server, and only for linked accounts.
  await expectEphemeral(await post(inGuild(button("avi:clock:in", "333456789012345678"), otherChannel)), "Link your Discord account");
  const panelTeamPrompt = await expectPrompt(await post(inGuild(button("avi:clock:in", selfId), otherChannel)), "Choose a team");
  const panelTeamLater = panelTeamPrompt.components[1].components.find(c => c.label === "Add later");
  const panelCategoryPrompt = await expectUpdated(await post(inGuild(component(panelTeamLater.custom_id, selfId, 2), otherChannel)), "What category");
  assert.ok(panelCategoryPrompt.content.includes("Sequoia Marine Solutions"), "team Add later uses the last-used team");
  const categoryLater = panelCategoryPrompt.components[1].components.find(c => c.label === "Add later");
  await expectUpdated(await post(inGuild(component(categoryLater.custom_id, selfId, 2), otherChannel)), "No reason yet");
  await expectEphemeral(await post(inGuild(button("avi:clock:in", selfId), otherChannel)), "already clocked in");
  await expectEphemeral(await post(inGuild(command("clockstatus", selfId))), "Clocked in");
  await expectEphemeral(await post(inGuild(button("avi:clock:status", selfId), otherChannel)), "Clocked in");
  await expectEphemeral(await post(inGuild(button("avi:clock:out", selfId), otherChannel)), "Clocked out");
  await expectEphemeral(await post(inGuild(button("avi:clock:out", selfId), otherChannel)), "not clocked in");
  const running = admin.prepare("SELECT COUNT(*) AS n FROM hours WHERE email = ? AND end_utc IS NULL").get("self@example.test");
  assert.equal(Number(running.n), 0, "clock out from Discord must end the session");
  const panelEntry = admin.prepare("SELECT team, category, source FROM hours WHERE email = ? ORDER BY id DESC LIMIT 1").get("self@example.test");
  assert.equal(panelEntry.team, "sms", "team Add later uses the last-used team while allowing later edits");
  assert.equal(panelEntry.category, "", "category Add later leaves the category blank for site editing");

  // Long custom category lists paginate without losing the Add later control.
  const addCategory = admin.prepare("INSERT INTO hours_categories (org, name, sort) VALUES (?, ?, ?)");
  for (let i = 0; i < 27; i++) addCategory.run("avi", `Category ${String(i).padStart(2, "0")}`, i);
  const longCategory = "L".repeat(150); // A legacy/imported category must not break Discord's 100-character value cap.
  addCategory.run("avi", longCategory, 27);
  const pagedTeamPrompt = await expectPrompt(await post(inGuild(clockSubcommand("in", selfId))), "Choose a team");
  const pagedTeamSelect = pagedTeamPrompt.components[0].components[0];
  const pagedCategoryPrompt = await expectUpdated(await post(inGuild(component(pagedTeamSelect.custom_id, selfId, 3, ["team:sms"]))), "What category");
  assert.equal(pagedCategoryPrompt.components[0].components[0].options.length, 25);
  const nextCategories = pagedCategoryPrompt.components[1].components.find(c => c.label === "Next");
  assert.ok(nextCategories && pagedCategoryPrompt.components[1].components.some(c => c.label === "Add later"));
  const lastCategoryPage = await expectUpdated(await post(inGuild(component(nextCategories.custom_id, selfId, 2))), "What category");
  assert.equal(lastCategoryPage.components[0].components[0].options.length, 3);
  assert.ok(lastCategoryPage.components[1].components.some(c => c.label === "Add later"));
  const longCategoryOption = lastCategoryPage.components[0].components[0].options.find(o => o.label.endsWith("…"));
  assert.ok(longCategoryOption && longCategoryOption.value.length <= 100 && longCategoryOption.label.length <= 100);
  const category26Option = lastCategoryPage.components[0].components[0].options.find(o => o.label === "Category 26");
  await expectUpdated(await post(inGuild(component(lastCategoryPage.components[0].components[0].custom_id, selfId, 3, [category26Option.value]))), "Category 26");
  await expectEphemeral(await post(inGuild(clockSubcommand("out", selfId))), "Clocked out");

  // Pause: commands and buttons answer "paused" on every instance; Discord's PING still passes.
  admin.prepare("UPDATE discord_bot SET paused = 1 WHERE id = 1").run();
  await expectEphemeral(await post(inGuild(command("myhours", selfId))), "paused");
  await expectEphemeral(await post(command("myhours", selfId), {}, baseUrl2), "paused");
  await expectEphemeral(await post(inGuild(button("avi:clock:in", selfId))), "paused");
  const pausedPing = await post({ application_id: appId, type: 1, version: 1 });
  assert.deepEqual(await pausedPing.json(), { type: 1 }, "PING must still pass while paused");
  admin.prepare("UPDATE discord_bot SET paused = 0 WHERE id = 1").run();
  await expectEphemeral(await post(inGuild(command("myhours", selfId))), "2.0 h");

  // Leaderboard: only when the organization turned it on; names, never emails.
  await expectEphemeral(await post(inGuild(command("leaderboard", selfId))), "hasn't turned on a leaderboard");
  admin.prepare("INSERT INTO hours_settings (org, leaderboard, season_month, season_day) VALUES ('avi', 1, 8, 1) ON CONFLICT(org) DO UPDATE SET leaderboard = 1").run();
  const board = await expectEphemeral(await post(inGuild(command("leaderboard", otherId))), "AVI Robotics leaderboard");
  assert.ok(board.indexOf("Private Other Name") < board.indexOf("Private Self Name"), `ranked by hours: ${board}`);
  assert.ok(board.includes("← you"), "the caller's own row is marked");
  assert.ok(!/example\.test|PRIVATE_.*_NOTE/.test(board), "leaderboard shows names only, never emails or notes");

  // The status log keeps counts by command and outcome, never who.
  const outcomes = new Set(admin.prepare("SELECT DISTINCT outcome FROM discord_events").all().map(r => r.outcome));
  for (const o of ["ok", "blocked", "channel", "unlinked", "paused"]) assert.ok(outcomes.has(o), `activity log records ${o}`);
  const kinds = admin.prepare("SELECT DISTINCT kind FROM discord_events").all().map(r => r.kind);
  assert.ok(kinds.includes("component:clock-in-flow"));
  assert.ok(kinds.every(k => !k.includes(selfId) && !k.includes("team:sms") && !k.includes("category:Programming")), "flow choices never enter the activity-log kind");
  const columns = admin.prepare("PRAGMA table_info(discord_events)").all().map(c => c.name);
  assert.deepEqual(columns.sort(), ["at_utc", "guild_id", "id", "kind", "outcome"], "activity log has no user columns");
  admin.close();

  const hugeBody = JSON.stringify({ application_id: appId, type: 1, padding: "x".repeat(70_000) });
  const oversized = await post(hugeBody);
  assert.equal(oversized.status, 413, "oversized interactions must be rejected before parsing");

  success = true;
  console.log("Discord interaction E2E passed: signatures, account isolation, permissions, /clock team/category selection, Add later paths, component ownership and validation, clock buttons, pause, activity log, and ephemeral replies.");
} catch (error) {
  console.error(error);
  console.error(`Discord E2E scratch kept at ${scratch}`);
  if (logs) console.error(logs);
  process.exitCode = 1;
} finally {
  await Promise.all([stopProcess(app), stopProcess(app2)]);
  if (success) fs.rmSync(scratch, { recursive: true, force: true });
}
