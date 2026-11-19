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
    groups: [{ id: "avi", name: "AVI Robotics" }, { id: "sms", name: "Sequoia Marine Solutions", parent: "avi" }],
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
  const proc = spawn("dotnet", ["run", "--no-build", "--no-launch-profile", "--configuration", "Release", "--project", "src/AviSite.Web/AviSite.Web.csproj"], {
    cwd: repo,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Development",
      ASPNETCORE_URLS: url,
      Store__DataDirectory: dataDir,
      Discord__ApplicationId: appId,
      Discord__PublicKey: publicHex,
      DOTNET_CLI_TELEMETRY_OPTOUT: "1",
      DOTNET_NOLOGO: "1",
    },
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

  // Clock panel buttons work in any channel of a listed server, and only for linked accounts.
  await expectEphemeral(await post(inGuild(button("avi:clock:in", "333456789012345678"), otherChannel)), "Link your Discord account");
  await expectEphemeral(await post(inGuild(button("avi:clock:in", selfId), otherChannel)), "Clocked in · AVI Robotics");
  await expectEphemeral(await post(inGuild(button("avi:clock:in", selfId), otherChannel)), "already clocked in");
  await expectEphemeral(await post(inGuild(command("clockstatus", selfId))), "Clocked in");
  await expectEphemeral(await post(inGuild(button("avi:clock:status", selfId), otherChannel)), "Clocked in");
  await expectEphemeral(await post(inGuild(button("avi:clock:out", selfId), otherChannel)), "Clocked out");
  await expectEphemeral(await post(inGuild(button("avi:clock:out", selfId), otherChannel)), "not clocked in");
  const running = admin.prepare("SELECT COUNT(*) AS n FROM hours WHERE email = ? AND end_utc IS NULL").get("self@example.test");
  assert.equal(Number(running.n), 0, "clock out from Discord must end the session");
  const panelEntry = admin.prepare("SELECT team, category, source FROM hours WHERE email = ? ORDER BY id DESC LIMIT 1").get("self@example.test");
  assert.equal(panelEntry.team, "avi", "the panel clocks into the server's team");
  assert.equal(panelEntry.category, "", "the reason is picked later on the site");

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
  const columns = admin.prepare("PRAGMA table_info(discord_events)").all().map(c => c.name);
  assert.deepEqual(columns.sort(), ["at_utc", "guild_id", "id", "kind", "outcome"], "activity log has no user columns");
  admin.close();

  const hugeBody = JSON.stringify({ application_id: appId, type: 1, padding: "x".repeat(70_000) });
  const oversized = await post(hugeBody);
  assert.equal(oversized.status, 413, "oversized interactions must be rejected before parsing");

  success = true;
  console.log("Discord interaction E2E passed: concurrent startup, unique-link constraint, signatures, stale-signature rejection, PING, account isolation, team permissions, aggregate-only output, allowed servers, command channels, clock buttons, pause, activity log, and ephemeral replies.");
} catch (error) {
  console.error(error);
  console.error(`Discord E2E scratch kept at ${scratch}`);
  if (logs) console.error(logs);
  process.exitCode = 1;
} finally {
  await Promise.all([stopProcess(app), stopProcess(app2)]);
  if (success) fs.rmSync(scratch, { recursive: true, force: true });
}
