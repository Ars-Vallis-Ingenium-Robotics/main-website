# Discord Hours integration

This is a stateless Discord Interactions endpoint. It is not a gateway bot: no persistent Discord connection and no Message Content intent. Every reply is private (ephemeral). With a bot token the site can also post two kinds of channel messages: a clock panel and a weekly team total (see "Bot management").

## Commands

- `/myhours range:<week|month|all>` — only the linked caller's own Hours totals.
- `/clock in`, `/clock out`, `/clock status` — the same private clock actions as the channel panel. Clock-in uses the caller's last-used eligible team in the server's organization (or their first eligible team); choose the reason later on the site.
- `/clockstatus` — only the linked caller's current clock status.
- `/leaderboard` — the organization's season leaderboard, only where the organization turned one on (see Bot management).
- `/teamhours range:<week|month|all> team:<id>` — aggregate totals for a team the caller may manage under the existing `HoursRules` access model. Owners, officers, and team leads retain the same scope as the website. It never returns member names, emails, notes, or individual rows.

Every application-command response is ephemeral. The interaction endpoint rejects unsigned, invalid, stale (over five minutes), oversized, malformed, or wrong-application requests before acting. Discord `PING` is acknowledged with `PONG`.

## Configuration

Keep secrets in environment variables or user-secrets, never in `appsettings*.json`, source control, or logs. The ASP.NET configuration keys are:

```text
Discord__ApplicationId=<Discord application ID>
Discord__PublicKey=<64-character Ed25519 public key from the Developer Portal>
Discord__ClientSecret=<OAuth client secret; needed only for account linking>
Discord__RedirectUri=https://<site-host>/admin/discord/callback
Discord__PublicBaseUrl=https://<site-host>
Discord__BotToken=<bot token; optional, enables posting, command registration and live status>
```

Register the exact callback URL in the Discord Developer Portal. HTTPS is required except for loopback-only local development. The interaction endpoint URL is:

```text
https://<site-host>/discord/interactions
```

Set the **Interactions Endpoint URL** in the Developer Portal to that address. Discord's verification `PING` must receive HTTP 200 with `{"type":1}`. Configure the public key and application ID before enabling the endpoint.

For local development, use `dotnet user-secrets` from `src/AviSite.Web` or environment variables; do not commit secret values. OAuth linking requests only `identify`, uses a short-lived protected state cookie and PKCE S256, binds the callback to the still-signed-in AVI account, and stores the Discord ID in a dedicated SQLite table. Unique constraints on both email and Discord ID enforce one-to-one links across app processes. The temporary access token is used in-memory for `/users/@me` and is not persisted.

## Register slash commands

Use Node 18+ and supply a bot token only in the environment. The script never writes or prints it. Use a guild ID for quick testing; omit it for global registration:

```sh
DISCORD_APPLICATION_ID=<application-id> \
DISCORD_BOT_TOKEN=<bot-token> \
DISCORD_GUILD_ID=<test-guild-id> \
node scripts/register-discord-commands.mjs
```

The Discord app needs the `applications.commands` installation scope. No gateway intents are needed. Do not enable Message Content. The script registers the Hours query commands and the `/clock` subcommands above.

## Bot management (`/admin/discord/bot`)

Who can do what:

- **Site owner** (the `Admin:Owners` accounts of the main site): pause and start the bot, add and remove servers and pick each server's team, register commands (one server or everywhere), point Discord's Interactions Endpoint at this site, and see live status from Discord.
- **Leads** of a server's team (or a team above it): that server's command channels, updates channel, weekly summary, clock panel channel and weekly meeting time; post a clock panel or a summary right away.
- **Officers**: view only, every server. They don't manage servers.

Behavior:

- **Pause** answers every command and button with "paused" and stops scheduled posts. Discord's verification `PING` is still answered, so Discord keeps the endpoint and starting again is instant.
- **Allowed servers**: with no servers listed the bot answers anywhere it's installed. Once any server is listed, only listed servers get answers. DMs and user installs (no server) keep working for personal commands.
- **Command channels**: empty means any channel. Otherwise slash commands in other channels reply with where to use them. Panel buttons work wherever the panel is.
- **Clock panel and `/clock`**: a message with Clock in / Clock out / My status buttons, with equivalent `/clock in`, `/clock out`, and `/clock status` subcommands. Clock in picks a team the person can log to inside the server's team (their last-used one first), with the reason picked later on the site, like the header's quick clock-in. Clock out applies the site's 24-hour cap. With a weekly meeting set, a fresh panel goes up 15 minutes before it (the previous panel is deleted).
- **Leaderboard**: if the server's organization turned its leaderboard on (Hours settings), `/leaderboard` answers privately with the season's top 10 and your own rank, and the server can post it in the updates channel every Monday (or right away). Names only: someone whose name is an email, or who has left, shows as "AVI member". Organizations without a leaderboard get a note instead.
- **Weekly summary**: Mondays from 9:00 Pacific, last week's total hours and head count for the server's team, plus one line per team directly under it. Totals only, never names.
- **Activity**: counts per command/button and result for 30 days. No Discord IDs, names or message text are stored.

Posting needs the bot user in the server with View Channel and Send Messages. The page links Discord's add-to-server flow with `bot applications.commands` and exactly those permissions. One app instance claims each scheduled post in SQLite, so several instances never double-post.

## Verification

Run the release build, then the isolated signed-webhook E2E test:

```sh
dotnet build src/AviSite.Web/AviSite.Web.csproj -c Release
node tests/e2e/discord-interactions.mjs
```

The E2E test creates an ephemeral Ed25519 key, isolated account/database files under the scratch directory, and two loopback app instances sharing one SQLite database. It verifies concurrent schema startup, the SQLite one-to-one link constraint, bad/missing/stale signatures, application ID checks, PING, per-account `/myhours` isolation, `/clock` subcommands and `/clockstatus`, lead-only aggregate `/teamhours`, ephemeral flags, and absence of personal identifiers/notes in team output.

## Operational boundary

The original integration change did not configure a Discord application, register commands, add production secrets, deploy the site, or alter the separate Hours pie-chart PR. After review and authorization, an operator must configure the application, secrets, interaction URL, and command registration in the target environment.
