#!/usr/bin/env node

const applicationId = process.env.DISCORD_APPLICATION_ID;
const botToken = process.env.DISCORD_BOT_TOKEN;
const guildId = process.env.DISCORD_GUILD_ID;
const snowflake = value => typeof value === "string" && /^\d{17,20}$/.test(value);

if (!snowflake(applicationId) || !botToken) {
  console.error("Set DISCORD_APPLICATION_ID and DISCORD_BOT_TOKEN in the environment. The token is never written to disk or printed.");
  process.exit(2);
}
if (guildId && !snowflake(guildId)) {
  console.error("DISCORD_GUILD_ID must be a 17–20 digit Discord snowflake.");
  process.exit(2);
}

const range = {
  type: 3, name: "range", description: "Hours window",
  required: false,
  choices: [
    { name: "This week", value: "week" },
    { name: "This month", value: "month" },
    { name: "All time", value: "all" },
  ],
};
const commands = [
  { name: "myhours", description: "View your own AVI Hours totals", options: [range] },
  { name: "clockstatus", description: "Check your own clock-in status" },
  {
    name: "teamhours", description: "View aggregate Hours for a team you lead",
    options: [range, { type: 3, name: "team", description: "Team ID (optional if you manage one team)", required: false }],
  },
];

const scope = guildId ? `/guilds/${guildId}` : "";
const endpoint = `https://discord.com/api/v10/applications/${applicationId}${scope}/commands`;
const response = await fetch(endpoint, {
  method: "PUT",
  headers: { Authorization: `Bot ${botToken}`, "Content-Type": "application/json" },
  body: JSON.stringify(commands),
});
if (!response.ok) {
  console.error(`Discord command registration failed: HTTP ${response.status}. Check the application, bot token, and command permissions.`);
  process.exit(1);
}
console.log(`Registered ${commands.length} read-only Discord slash commands${guildId ? " for the selected test guild" : " globally"}.`);
