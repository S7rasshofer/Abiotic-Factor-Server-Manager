# Facility Overseer — Agent & Skill Index

> Routing manifest for AI sessions. Read this first, then jump to whichever
> doc/agent/skill matches the task. Keep this file short (≤ ~120 lines).

## Source of truth

- **Master plan:** [`docs/MASTER_PLAN.md`](docs/MASTER_PLAN.md) — single
  consolidated plan with the overview table, three phases, and per‑task
  checkboxes. Flip checkboxes here when work lands.
- **Architecture reference:** [`docs/CURRENT_BUILD.md`](docs/CURRENT_BUILD.md)
  — what exists today, verified against source.
- **History (superseded, do not edit):**
  [`docs/MASTER_CORRECTION_PLAN.md`](docs/MASTER_CORRECTION_PLAN.md),
  [`docs/PLAYER_ROSTER_ACTIVITY_PLAN.md`](docs/PLAYER_ROSTER_ACTIVITY_PLAN.md).

## Sub‑agents — pick the one that owns the workstream

Defined in `.claude/agents/`. Use the `Agent` tool with the matching
`subagent_type` when a task fits.

| Agent | Owns | Master‑plan sections |
|---|---|---|
| `world-identity-architect` | `<DataRoot>/worlds/<id>/` migration, absolute `-SandboxIniPath`, world ≠ install | §2 (architecture), §2.1 |
| `roster-moderation-engineer` | Admin/ban unification, banished page, admin marker | §2.2, §3.2, §3.3 |
| `ui-ux-overseer` | Health‑driven dots, IP display surface, diagnostic cards, tab coloring | §2.3, §2.5, §2.6, §3.1 (display), §4.1, §4.3, §4.4, §4.6, §4.8 |
| `network-intel-engineer` | Public IP probe, network confidence score, room/join code | §3.1 (backend), §4.7, §4.9 |
| `rcon-toolkit-engineer` | RCON seam, command catalog, kick/announce/restart | §5.* (Phase 3 only) |

## Skills — house rules that keep agents on task

Defined in `.claude/skills/<name>/SKILL.md`. The agent definitions list
which skills apply.

| Skill | Enforces |
|---|---|
| `core-pure-discipline` | New behavior is a pure record/static in Core; Infra is the only IO layer. |
| `world-identity-model` | World config lives under `<DataRoot>/worlds/<id>/`, never inside the server install. Absolute `-SandboxIniPath`. |
| `onedrive-volatile-split` | Volatile (SteamCMD, server) → `VolatileRoot`; durable → `DataRoot`. Don't leak the split into UX. |
| `sectioned-admin-ini` | Go through `AdminIniBanEditor`; preserve `[Moderators]` / `[BannedPlayers]` / comments. |
| `discover-dont-hardcode` | Schema + metadata catalog + type inference. Unknown keys preserved on Advanced tab. |
| `health-driven-status` | Status indicators bind to `ServerHealth`, not `IsRunningState`. One dot per world. |
| `tests-and-warnings-are-errors` | 252 tests baseline; `TreatWarningsAsErrors=true`. New Core behavior gets a test. |
| `copy-not-delete-migration` | Old roots are copied with approval, never moved or auto‑deleted. Quarantine over delete. |

## Phase ordering

1. **Phase 1 — Stability & Identity** (§2, §3 of the plan): do this first.
   Identity must be stable before anything else.
2. **Phase 2 — Operational Intelligence** (§4): polish, guidance, scoring.
3. **Phase 3 — Remote Admin / RCON** (§5): only after Phase 1 is green.

Do not start Phase 2 until §2.* are done. Do not start Phase 3 until
Phase 2 is meaningfully under way.

## Project commands

```pwsh
dotnet build  AbioticServerManager.slnx
dotnet test   src/AbioticServerManager.Tests/AbioticServerManager.Tests.csproj
dotnet run    --project src/AbioticServerManager.App/AbioticServerManager.App.csproj
dotnet publish src/AbioticServerManager.App/AbioticServerManager.App.csproj -c Release -o publish
```

Baseline: **252 tests passing, 0 warnings**, `TreatWarningsAsErrors=true`.
Never lower this floor.
