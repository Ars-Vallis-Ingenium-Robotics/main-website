# Discord Hours integration (read-only)

This is a stateless Discord Interactions endpoint. It is not a gateway bot: no persistent Discord connection, Message Content intent, clock controls, or public replies.

## Commands

- `/myhours range:<week|month|all>` — only the linked caller's own Hours totals.
- `/clockstatus` — only the linked caller's current clock status.
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

The Discord app needs the `applications.commands` installation scope. No gateway intents are needed. Do not enable Message Content. The script registers only the three read-only commands above.

## Verification

Run the release build, then the isolated signed-webhook E2E test:

```sh
dotnet build src/AviSite.Web/AviSite.Web.csproj -c Release
node tests/e2e/discord-interactions.mjs
```

The E2E test creates an ephemeral Ed25519 key, isolated account/database files under the scratch directory, and two loopback app instances sharing one SQLite database. It verifies concurrent schema startup, the SQLite one-to-one link constraint, bad/missing/stale signatures, application ID checks, PING, per-account `/myhours` isolation, `/clockstatus`, lead-only aggregate `/teamhours`, ephemeral flags, and absence of personal identifiers/notes in team output.

## Operational boundary

This repository change does not configure a Discord application, register commands, add production secrets, deploy the site, or alter the separate Hours pie-chart PR. After review and authorization, an operator must configure the application, secrets, interaction URL, and command registration in the target environment.
