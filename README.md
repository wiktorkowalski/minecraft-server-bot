# minecraft-server-bot

Discord bot for operating a Minecraft server over RCON. Runs in homelab Docker, talks to a remote Minecraft server (RCON + Server List Ping). Slash commands plus natural-language LLM chat via OpenRouter + SemanticKernel.

## Stack
- .NET 10, ASP.NET minimal host
- DSharpPlus 4.5 (Discord) with SlashCommands
- SemanticKernel + OpenRouter (default model: `deepseek/deepseek-v4-flash`)
- CoreRCON for RCON; hand-rolled SLP client for status pings
- EF Core SQLite for audit log, conversation history, player sessions
- Quartz.NET for scheduled tasks
- Serilog + OpenTelemetry → Langfuse

## Quick start
```bash
cp .env.example .env
# fill in Discord token, MC host/RCON password, OpenRouter key
docker compose up -d --build
curl http://localhost:8080/health
```

## Configuration
All knobs live in `appsettings.json` with sensible defaults. Override via environment variables (double-underscore notation: `Section__Key=value`) or `.env`. See `.env.example` for the secret/required set.

## Slash commands
| Command | Notes |
| --- | --- |
| `/status` `/players` `/motd` `/version` | Read-only status |
| `/say <text>` `/save` `/restart` | Server control (some require confirmation) |
| `/exec <cmd>` | Raw RCON, **admin-only**, blocklist + rate-limit |
| `/schedule add\|list\|cancel` | Quartz-backed scheduled actions |

LLM activates on `@mention` in the configured channel or via DM (admins only). Destructive actions show a Discord confirmation button (60s timeout).

## Deploy
GitHub Actions workflow `build.yml` builds the image, pushes to GHCR, and deploys to the self-hosted homelab runner via docker-compose.
