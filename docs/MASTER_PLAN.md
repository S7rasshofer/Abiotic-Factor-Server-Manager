# Facility Overseer — Master Plan

> **Single source of truth.** Supersedes `MASTER_CORRECTION_PLAN.md` and
> `PLAYER_ROSTER_ACTIVITY_PLAN.md` (kept for history). Architecture reference:
> [`CURRENT_BUILD.md`](CURRENT_BUILD.md). Snapshot: 2026‑05‑19. Baseline: 252
> tests passing, 0 warnings, `TreatWarningsAsErrors=true`.
>
> **Active near-term priority:** [`UI_TWEAKS_PLAN.md`](UI_TWEAKS_PLAN.md) — a
> focused UI tweaks & cleanup pass (review 2026-05-21) that runs **ahead of
> Phase 3**.
>
> Update protocol: when a task finishes, flip `[ ]` to `[x]` in both the
> overview table **and** its section. Add new tasks at the bottom of the
> relevant phase, never reorder finished items.

---

## 1. Overview

| # | Topic | Phase | Status | Brief |
|---|---|---|---|---|
| 1.1 | Single data root | 0 — Done | [x] | One `<DataRoot>` for config/logs/backups/tools/servers; OneDrive‑safe split for volatile dirs. |
| 1.2 | First‑run setup + install gating | 0 — Done | [x] | Prepare‑server flow, Start disabled until launchable exe detected. |
| 1.3 | Install‑state classification | 0 — Done | [x] | `Missing / EmptyFolder / InvalidFolder / DetectedUnmanaged / SteamCmdManaged`. |
| 1.4 | Header version display | 0 — Done | [x] | `Facility Overseer v<asm> \| <server state/build>`, bound, not hardcoded. |
| 1.5 | SteamCMD self‑heal | 0 — Done | [x] | Self‑update failure detection + clean reinstall + actionable report. |
| 1.6 | Runtime health tracker | 0 — Done | [x] | `Stopped/Starting/Online/Blocked/Crashed` from readiness + blocking signals. |
| 1.7 | Player roster + persistence | 0 — Done | [x] | Real AF log parsing → durable per‑world `roster.json`. |
| 1.8 | Corrupt‑world recovery | 0 — Done | [x] | Blocked state, Open World Folder, Create Fresh World (quarantine, never delete). |
| 1.9 | Conservative migration | 0 — Done | [x] | Detect old roots, copy with approval, never auto‑move/delete. |
| 1.10 | Dynamic sandbox/schema engine | 0 — Done | [x] | Metadata + type inference; unknown settings preserved on Advanced tab. |
| 1.11 | Networking core + firewall | 0 — Done | [x] | Pure classifier/selection/checklist; idempotent per‑world firewall rules. |
| 1.12 | Sectioned ban editor | 0 — Done | [x] | `AdminIniBanEditor` writes `[BannedPlayers]`, preserves `[Moderators]` + comments. |
| 2.0 | **Persistent user data‑root choice** | 1 — Stability | [x] | Saved at `%LOCALAPPDATA%\FacilityOverseer\data-root.txt`; resolver honors it on every launch. First‑launch UI to actually offer the AppData choice → §2.0a follow‑on. |
| 2.1 | **Decouple world from server install** | 1 — Stability | [x] | Per‑world `SandboxSettings.ini` + `Admin.ini` live under `<DataRoot>/worlds/<id>/config/`; launch args emit absolute paths; copy‑not‑delete migration from legacy in‑install location; idempotent. |
| 2.2 | **Unify admin / ban systems** | 1 — Stability | [x] | One sectioned‑Admin.ini service powering Admin tab + Ban/Unban; delete flat‑file assumption. |
| 2.3 | **Unify health‑state visual logic** | 1 — Stability | [x] | One health‑colored dot per world (grey/yellow/green/red) driven by `ServerHealth`, not `IsRunningState`. |
| 2.4 | **Single "Reset Managed Data"** | 1 — Stability | [x] | `IResetManagedDataService` + "Reset Data…" header button; confirms, clears `DataRoot` + `VolatileRoot`, writes a reset report. |
| 2.5 | **Hide empty Advanced tab** | 1 — Stability | [x] | `SandboxSettingsViewModel.HasAdvancedSettings` + `Visibility` binding hides when empty; loss‑less catch‑all preserved. |
| 2.6 | **Health‑driven status dots** | 1 — Stability | [x] | De‑duplicate three running dots → keep one; add `ServerHealth → Brush` converter. |
| 3.1 | Internal / External IP display | 1 — Stability | [x] | LAN + Public IP strip in top bar; `IPublicIpProbe` (`HttpPublicIpProbe`) probes on launch and on demand. |
| 3.1a | Internal‑IP change detection | 1 — Stability | [x] | Core `InternalIpChangeTracker` + `JsonInternalIpSnapshotStore`; launch‑time banner above world tabs when the LAN IP changed. |
| 3.1b | "Lock this internal IP" guidance | 1 — Stability | [x] | Core `LanIpLockGuidance` + a lock-IP button beside the LAN IP → clipboard-copied DHCP-reservation dialog. |
| 3.2 | Banished‑players page (split out) | 1 — Stability | [x] | Move banned list off Players/Status to its own page; status screen shows active roster only. |
| 3.3 | Admin thematic marker on roster | 1 — Stability | [x] | Admins rendered in thematic color + small superscript privilege glyph (exponent‑style). |
| 4.1 | Actionable diagnostic cards | 2 — Intelligence | [x] | Expander-based accordion (click to expand); severity dot + title visible; centered + MaxWidth 720. |
| 4.2 | Guided recovery flows | 2 — Intelligence | [x] | Contextual recovery panel (ordered steps + action buttons) surfaced when a world is Blocked; `ServerHealthSignals.BlockingTag` picks the flow. |
| 4.3 | Recommended actions panel | 2 — Intelligence | [x] | `RecommendedActions.Build(...)` ranked panel on the Log tab; each row's button runs the matching command via `CommandHint`. |
| 4.4 | Backup confidence indicators | 2 — Intelligence | [x] | Per-backup color-coded confidence badge (Full / Partial / Limited + age + stale) on the Backups tab. |
| 4.5 | "World integrity" validation | 2 — Intelligence | [x] | `IWorldIntegrityInspector` gathers disk facts → `WorldIntegrityValidator`; findings panel + pre-Start blocker gate. |
| 4.6 | Startup sequence summary | 2 — Intelligence | [x] | `StartupSequenceTracker` 7-phase dot strip on Logs & Status, fed by process + per-line log events. |
| 4.7 | Network confidence scoring | 2 — Intelligence | [x] | `NetworkConfidenceScoring` 0–100 score + band + strengths + "to improve" lifts panel on the Network tab. |
| 4.8 | Color‑coded vertical tabs | 2 — Intelligence | [x] | `ConfigTabAccentBrush` (seafoam) + `OpsTabAccentBrush` (slate-blue) applied per TabItem. |
| 4.9 | Room/join code research + display | 2 — Intelligence | [x] | Confirmed: server logs the lobby code as the EOS `ShortCode` attribute. `LobbyCodeParser` extracts it; copyable LOBBY CODE panel on the Network tab. |
| 5.1 | RCON abstraction seam | 3 — Remote Admin | [ ] | `IRemoteConsoleClient` interface (Core pure command builders + parsers; Infra does IO). |
| 5.2 | Verified command catalog | 3 — Remote Admin | [ ] | Data‑driven list of known‑safe AF commands; one‑click buttons; raw box for the rest. |
| 5.3 | Live player actions | 3 — Remote Admin | [ ] | Real Kick (no restart) + Ban‑live (ban + disconnect) from roster row. |
| 5.4 | Announcement / broadcast box | 3 — Remote Admin | [ ] | Single text field → server‑wide message via AF broadcast command. |
| 5.5 | Live moderation feed | 3 — Remote Admin | [ ] | Surface remote‑console responses inline; correlate with roster for authoritative count. |
| 5.6 | Remote save / restart orchestration | 3 — Remote Admin | [ ] | Graceful save → broadcast countdown → restart, all from one button. |
| 5.7 | RCON enable + per‑world config | 3 — Remote Admin | [ ] | Opt‑in toggle, host/port/password on model; firewall rule added only when enabled. |

Phase ordering rule: **do not start Phase 2 until §2.* are green, do not
start Phase 3 until Phase 2 is meaningfully under way.** Stability first,
then intelligence, then remote admin.

---

## 2. Architectural Pivot — World as First‑Class Identity

The single most important shift in this plan. Today the architecture still
treats *world = server install*. Sandbox settings, admins, bans, and staged
world state die during reinstall/repair/reset because they live inside the
server payload (`%LOCALAPPDATA%\…\servers\…`). World profiles and the player
roster survive (they live under `DataRoot`), so the world's *identity*
outlives its *soul* — a psychologically jarring split.

### Target layout

```
<DataRoot>/
  worlds/
    <worldId>/                  # durable, portable, exportable, backup‑safe
      config/
        SandboxSettings.ini     # was: inside server install
        Admin.ini               # was: inside server install
        metadata.json           # world name, created, last‑played, fingerprint
      saves/
        backups/                # world‑scoped backups
        exports/                # user‑initiated portable bundles
      roster/
        roster.json             # moved from players/<id>/roster.json
      runtime/
        cache/                  # transient, safe to nuke
        temp/
  servers/
    abiotic-factor-dedicated/   # disposable, repairable, replaceable, stateless
```

Server install becomes **a runtime tool**, not a state store. A repair, wipe,
or reinstall must never destroy a world's tuning, admins, bans, or roster.

### Migration rules (non‑negotiable)

- **Copy, never move or delete** the existing `instance.sandboxIniPath` /
  `adminIniPath` payloads from the server install into `<DataRoot>/worlds/<id>/config/`.
- Update `ServerInstance` to store **only** the world id; resolve paths from
  `IAppPaths` at launch time. Pass `-SandboxIniPath` as an **absolute** path.
- Preserve a `worlds/<id>/migration-YYYYMMDD-HHMMSS.log` per world.
- Leave the old in‑server‑install files in place (and untouched) until the
  user confirms cleanup in the new Reset UI (§2.4).

### Acceptance gates (§2.1)

- Wiping `%LOCALAPPDATA%\FacilityOverseer` (VolatileRoot) **does not** lose
  sandbox tuning, admins, bans, or roster for any world.
- A SteamCMD `validate` of the dedicated server **does not** touch
  `<DataRoot>/worlds/`.
- All 252 existing tests still pass; new Core tests cover the path‑resolution
  fork (legacy in‑server vs new in‑DataRoot) without IO.

---

## 3. New User‑Requested Features (Phase 1)

### 3.1 Internal / External IP display

- **Internal (LAN) IPv4** — already computed by `Ipv4Selection` and shown as
  "Current LAN IPv4" on the Network tab. Surface it on the world status
  header too (next to the health dot), copyable.
- **External (public) IP** — not yet computed. Add an opt‑in lookup:
  - Core: `IPublicIpProbe` contract (pure interface). Infrastructure:
    `HttpPublicIpProbe` calling a small, well‑known plaintext endpoint
    (no auth, no payload — just an IP string). Cache for the app session;
    refresh on user click.
  - Show as "Public IPv4 (unverified — for sharing)" with a copy button.
  - Never call out automatically when **LAN Only** is on. Honor a global
    privacy toggle in Settings.
- Both addresses appear on the per‑world status strip *and* on the Network
  tab. Render `?` when the probe is disabled, offline, or behind CGNAT.

### 3.2 Banished players — their own list

- **Status / Players** screens show **active roster only**. Banned entries
  do not appear inline.
- New top‑level vertical tab on the world: **"Banned"** (or sub‑tab under
  Admin). Columns: ID, display name (if ever seen), date banned, source
  (manual / raw INI), notes.
- Powered by the existing `AdminIniBanEditor` (no new file format). Unban
  from this page rewrites `[BannedPlayers]` in place.
- A small badge on the Admin tab header shows the ban count when non‑zero.

### 3.3 Admin thematic marker on roster

- The admin list is consulted at render time; matching SteamID64 players in
  the roster get:
  - **Thematic color** — define `AdminAccentBrush` in `Themes/Overseer.xaml`
    (a warm "facility gold" distinct from the standard accent teal).
  - **Privilege glyph** — small superscript marker rendered after the
    display name, like `S7razzy⁺` (or `★`, decide in styling pass). Tooltip:
    "Server administrator — has admin commands."
- Marker is **decorative only**; ban/unban/kick still operate on the
  underlying ID, not the visual.

---

## 4. Phase 1 — Stability & Identity (Highest Priority)

Goal: stabilize trust. The user must be able to predict what survives a
wipe, a reinstall, and a reset. Status indicators must not lie.

- [x] **2.0** Persistent user data‑root choice (saved in
      `%LOCALAPPDATA%\FacilityOverseer\data-root.txt`; honored on every
      launch; no folder appears beside the exe if the user picked AppData).
      *Implemented 2026‑05‑19: `AppPaths.ResolveDataRoot(savedChoice, …)`
      + `DataRootChoiceFile`; first launch pins the auto‑detected default;
      subsequent launches use the saved value. Tests: 259 passing, 0
      warnings.*
- [ ] **2.0a** First‑launch data‑root picker UI (offer Portable / AppData /
      Custom; calls `DataRootChoiceFile.TrySave`)
- [x] **2.1** Decouple world config from server payload (architectural — §2).
      *Implemented 2026‑05‑20: `IAppPaths.WorldSandboxIniPath` /
      `WorldAdminIniPath` resolve under `<DataRoot>/worlds/<id>/config/`;
      `WorldIdentityMigrationService` copies legacy in‑install files on
      first load (copy‑not‑delete, idempotent, per‑world migration log);
      `LaunchArgumentBuilder` emits absolute path when outside Saved/;
      `AdminListService` honors the migrated path. New
      `WorldIdentityMigrationTests` (9) + `WorldIdentityPathsTests` (7)
      including the "server install wipe leaves worlds/ intact" gate.
      335 tests passing, 0 warnings.*
- [x] **2.2** Unify admin / ban systems (delete the flat‑file assumption in
      `AdminListService`; route Admin tab + Ban/Unban through one sectioned
      service). *Implemented 2026‑05‑20: Core `AdminIniModeratorEditor`
      (pure, mirrors `AdminIniBanEditor`); `AdminListService` rewritten to
      load / replace only `[Moderators]` lines via
      `AdminIniModeratorEditor.ReplaceModerators(...)`, leaving comments,
      blank lines, and the `[BannedPlayers]` section byte‑identical. The
      sectioned `Admin.ini` is now the single file targeted by both the
      Admin‑tab editor and the Ban/Unban commands. Tests: 312 passing, 0
      warnings (+15 new: `AdminIniModeratorEditorTests`,
      `AdminBanUnifiedServiceTests`, `AdminListService` round‑trip).*
- [x] **2.3** Unify health‑state visual logic (one dot, `ServerHealth →
      Brush`, single source of truth). *Implemented 2026‑05‑20.*
- [x] **2.4** Implement single **"Reset Everything Managed By Facility
      Overseer"** that clears both roots and refreshes the in‑app **About**
      panel to show the canonical `DataRoot`. *Implemented 2026‑05‑20:
      `IResetManagedDataService` clears both `DataRoot` and `VolatileRoot`
      children, recreates the canonical layout, writes a per‑reset report;
      header "Reset Data…" button with strong confirmation + status
      preserves the data‑root choice pointer.*
- [x] **2.5** Hide empty Advanced tab instead of removing it (preserve
      loss‑less catch‑all). *Implemented 2026‑05‑20:
      `SandboxSettingsViewModel.HasAdvancedSettings` flips
      `TabItem.Visibility`; raised on every Load. Loss‑less catch‑all
      remains for future game updates.*
- [x] **2.6** Convert status dots to true health indicators
      (grey=Stopped, yellow=Starting, green=Online, red=Blocked/Crashed);
      de‑duplicate the three running dots → keep the world tab chip.
      *Implemented 2026‑05‑20: Core `HealthIndicators.For(ServerHealth)` +
      App `HealthToBrushConverter`; world tab chip is the single dot; the
      Logs & Status header and per‑world toolbar dots are removed.*
- [x] **3.1** Internal / external IP display. *Implemented 2026‑05‑20:
      LAN + Public IP strip on the top bar; `IPublicIpProbe` /
      `HttpPublicIpProbe` (ipify.org, cached per session); `RefreshPublicIp`
      command; 13 new tests including `PublicIpParsingTests`.*
- [x] **3.1a** Internal‑IP change detection. *Implemented 2026‑05‑20:
      `JsonInternalIpSnapshotStore` persists to
      `<DataRoot>/config/last-internal-ip.json`;
      `MainViewModel.DetectInternalIpChangeAsync` runs on launch;
      `InternalIpChangeBannerText` + dismissable banner above the world
      tabs. 9 tests in `InternalIpChangeTrackerTests`.*
- [x] **3.1b** "Lock this internal IP" guidance. *Completed 2026‑05‑21:
      `LanIpLockGuidance.Compose(LanIpLockContext)` produces
      router‑agnostic copy‑paste text (UniFi/ASUS/Netgear/TP‑Link hints, no
      network call), 5 tests. A lock-IP button beside the LAN IP on the top
      bar composes the guidance, copies it to the clipboard, and shows it
      in a dialog (`ShowLanIpLockGuidanceCommand`).*
- [x] **3.2** Banished‑players page split out. *Implemented 2026‑05‑20:
      `RosterPresentation.FilterActive(...)` excludes banned SteamID64s from
      the live roster VM; `BuildBannedRows(...)` produces the new
      `BannedPlayerRow` records (id, last‑known display name from the
      roster, date banned, source = manual / raw INI, notes). Surface is a
      "Banned" sub‑tab under Admin (kept the Admin tab as the parent so the
      admin‑password / moderator editor / banned list sit together as one
      moderation hub). A red badge with the ban count appears on the Admin
      tab header when non‑zero. Powered by the same sectioned `Admin.ini`
      reader as Ban/Unban — no new file format. 7 new
      `RosterPresentationTests`.*
- [x] **3.3** Admin thematic marker on roster. *Implemented 2026‑05‑20:
      `RosterPresentation.IsAdmin(entry, moderatorIds)` (pure Core);
      `RosterRowViewModel` exposes a derived `IsAdmin` (Core model
      untouched — decoration only). `AdminAccentBrush` (#E0B65C, warm
      facility gold) added to `Themes/Overseer.xaml`; admin rows render
      the display name in gold with a U+207A superscript "+" glyph and a
      tooltip "Server administrator — has admin commands." Ban/unban/kick
      still operate on the underlying captured id.*

**Definition of done for Phase 1:** the app's identity model survives any
reasonable "reset" gesture without surprising the user. No status indicator
contradicts another. Bans and admins live in one consistent place. IPs are
visible. Tests stay green, no warnings.

---

## 5. Phase 2 — Operational Intelligence

Goal: make the app *feel smart*. The user gets prompted toward the right
action instead of having to read logs. Confidence is quantified, not
implied.

- [x] **4.1** Actionable diagnostic cards. *Implemented 2026‑05‑20:
      Expander accordion replaces the always-expanded card; centered
      `MaxWidth=720` on the Log tab.*
- [x] **4.2** Guided recovery flows. *Completed 2026‑05‑21:
      `RecoveryFlows` catalog (CORRUPT_WORLD / PORT_CONFLICT /
      MISSING_EXECUTABLE / BROKEN_STEAMCMD; 13 tests). New
      `ServerHealthSignals.BlockingTag` + `ServerHealthTracker.BlockingTag`
      (7 tests) expose the trigger tag; `RefreshGuidance` surfaces the
      matching flow as a contextual panel on the Log tab when a world is
      Blocked (corrupt world / port conflict), each step's button running
      its `ActionHint` via `RunRecoveryStepCommand`. Implemented as an
      inline panel, not a separate modal window — consistent with the
      §4.1 / §4.3 / §4.5 guidance surfaces and avoids introducing the
      app's first secondary window.*
- [x] **4.3** Recommended actions panel. *Completed 2026‑05‑21:
      `RecommendedActions.Build(...)` (10 tests) is rebuilt by
      `MainViewModel.RefreshGuidance` on install / health / network /
      selection changes and rendered as a ranked panel on the
      Logs & Status → Log tab. Each row's "Do this" button runs the
      matching command via `RunRecommendedActionCommand`, dispatching on
      the action's `CommandHint` (install / firewall / create-world /
      restart) — data-driven, no per-action XAML.*
- [x] **4.4** Backup confidence indicators. *Completed 2026‑05‑21:
      `BackupConfidenceCalculator.Evaluate(...)` → Full / Partial / Limited
      + age + IsStale flag with configurable threshold; 9 tests. Each
      backup row on the Backups tab now shows a color-coded confidence
      badge + age (+ a stale marker), evaluated at bind time by
      `BackupEntryToConfidenceConverter`, with a tooltip explaining gaps.*
- [x] **4.5** World integrity validation. *Completed 2026‑05‑21:
      `WorldIntegrityValidator.Validate(...)` (8 tests) is fed by the new
      `IWorldIntegrityInspector` / `WorldIntegrityInspector` (Infrastructure
      gathers disk facts into pure inputs; 4 tests). `StartServer` runs it
      after world prep and aborts on any Blocker with a message; the
      findings render as an expander panel on the Log tab.
      `MainViewModel.RefreshGuidance` keeps it live on selection / install /
      network changes.*
- [x] **4.6** Startup sequence summary. *Completed 2026‑05‑21:
      `StartupSequenceTracker` drives a 7-phase timeline (ProcessStarted
      → NetDriverListening → WorldLoading → WorldLoaded →
      SessionCreating → SessionCreated → PlayersCanJoin) from log
      signals; failed phases carry the reason; 9 tests. `ServerInstanceViewModel`
      feeds it from process start/stop and the per-line log batch; the
      timeline renders as a colour-coded phase-dot strip on the Logs &
      Status tab, shown while starting/running or after a failed startup.*
- [x] **4.7** Network confidence scoring. *Completed 2026‑05‑21:
      `NetworkConfidenceScoring.Score(...)` → 0–100, band (None / Low /
      OK / Good / Great), strengths + lifts; CGNAT caps at Low band;
      LAN-only mode skips public factors; 6 tests. `ApplyNetworkSetupStatus`
      builds the inputs from the network inspection (firewall rules, port
      bindings, LAN + public IP via `IpAddressClassifier`) and renders the
      score, strengths, and "to improve" lifts as a panel on the Network tab.*
- [x] **4.8** Color‑coded vertical tabs. *Implemented 2026‑05‑20:
      `ConfigTabAccentBrush` (#8CD9D2 seafoam) on Server / World / Player
      / Enemy / Admin / Advanced; `OpsTabAccentBrush` (#B6A8E0 slate-blue)
      on Network / Backups / Logs & Status.*
- [x] **4.9** Room/join code research + display. *Completed 2026‑05‑21:
      confirmed from a real Facility Overseer dedicated-server log — the
      server publishes the in-game "lobby code" as the EOS session
      attribute `ShortCode`, e.g.
      `LogOnlineSession: EOS: EOS_SessionModification_AddAttribute() named (ShortCode) with value (O8TXQ)`.
      Pure `LobbyCodeParser.TryParse` (8 tests, verified against the real
      line) extracts it; `ServerInstanceViewModel.ApplyHealth` captures it
      from the log tail and clears it on start/stop; a copyable LOBBY CODE
      panel shows on the Network tab while the server is running.*

**Definition of done for Phase 2:** a new user can open the app, hit a
problem, and be guided to the fix without reading the docs. Confidence
indicators don't lie.

---

## 6. Phase 3 — Remote Admin (RCON)

Goal: live moderation without restart. Only after Phase 1 identity is
stable — otherwise we risk wiring buttons against a model that's still
shifting.

- [ ] **5.1** `IRemoteConsoleClient` seam — Core‑pure command builders +
      response parsers, Infra speaks the protocol
- [ ] **5.2** Verified command catalog (data‑driven, editable without
      recompile; mirrors sandbox metadata pattern)
- [ ] **5.3** Live player actions — real Kick + Ban‑live from roster
- [ ] **5.4** Announcement / broadcast text box
- [ ] **5.5** Live moderation feed (remote‑console responses → log/roster
      correlation; authoritative `PlayerCount`)
- [ ] **5.6** Remote save / restart orchestration (graceful save →
      broadcast countdown → restart)
- [ ] **5.7** RCON enable + per‑world config (opt‑in; firewall rule only
      when enabled; password masked like other secrets)

**Keep, do not regress:**

- Command‑line/raw box remains **secondary**, not primary.
- Structured operations (buttons + catalog) are the primary surface.
- Never wire buttons against guessed commands — confirm AF's command set
  from a real running server first.

---

## 7. Sub‑Agents (workstream owners)

Agents are defined in `.claude/agents/` so future sessions can delegate
cleanly. Each agent owns a workstream end‑to‑end and follows the skills in
§8.

| Agent | Owns | Primary phase |
|---|---|---|
| `world-identity-architect` | §2 (world ≠ install), migration, path resolution | 1 |
| `roster-moderation-engineer` | Admin/ban unification, banished page, admin marker | 1 |
| `ui-ux-overseer` | Health‑driven dots, IP display surface, diagnostic cards, tab coloring | 1–2 |
| `network-intel-engineer` | Public IP probe, network confidence score, room/join code research | 1–2 |
| `rcon-toolkit-engineer` | RCON seam, command catalog, kick/announce/restart | 3 |

---

## 8. Skills (house rules — keep agents on task)

Skills are defined in `.claude/skills/` and are loaded into agent context
when they touch matching code paths.

| Skill | Enforces |
|---|---|
| `core-pure-discipline` | New behavior is a pure record/static in Core; Infra is the only IO layer. |
| `world-identity-model` | World config lives under `<DataRoot>/worlds/<id>/`, not inside the server install. Absolute `-SandboxIniPath`. |
| `onedrive-volatile-split` | Volatile (SteamCMD, server) → `VolatileRoot`; durable (config, worlds, logs, backups) → `DataRoot`. Never leak the split into UX. |
| `sectioned-admin-ini` | Use `AdminIniBanEditor`; preserve `[Moderators]` / `[BannedPlayers]` / comments. Never assume flat Admin.ini. |
| `discover-dont-hardcode` | Schema + metadata catalog + type inference. Unknown keys → preserved on Advanced tab. |
| `health-driven-status` | Status indicators bind to `ServerHealth`, not `IsRunningState`. One dot per world. |
| `tests-and-warnings-are-errors` | `TreatWarningsAsErrors=true`. Every new Core behavior gets a test. Baseline: 252 passing. |
| `copy-not-delete-migration` | Old roots are copied with user approval, never moved or auto‑deleted. |

---

## 9. Test & verification matrix

Add/extend tests as the boxes flip. **Do not flip a checkbox without
green tests.**

| Task | New test family |
|---|---|
| §2.0 | `DataRootChoiceTests` — saved choice wins over auto‑detect; missing/empty pointer file falls back; round‑trip Save→Load. |
| §2.1 | `WorldIdentityPathsTests` — resolves sandbox/admin under `<DataRoot>/worlds/<id>/`; legacy fallback resolves to old path; migration copy is idempotent. Plus an end‑to‑end test: simulated SteamCMD validate (touches `servers/`) leaves `worlds/` byte‑identical. |
| §2.2 | `AdminBanUnifiedServiceTests` — Admin tab edits + Ban/Unban round‑trip through the same sectioned writer. |
| §2.3 / §2.6 | `HealthBrushConverterTests` — every `ServerHealth` value maps to a defined brush; no leak from `IsRunningState`. |
| §2.4 | `ResetAllManagedTests` — reset clears both roots, preserves user‑external server folders, writes a reset log. |
| §3.1 | `Ipv4SelectionTests` already covers LAN; add `PublicIpProbeTests` (Core: parses a plaintext IP, rejects garbage, handles timeout). |
| §3.1a | `InternalIpChangeTrackerTests` — given last‑seen + current, returns Unchanged/Changed/FirstRun; persists last‑seen atomically. |
| §3.1b | `LanIpLockGuidanceTests` — Core composes router‑agnostic copy/paste text from (LAN IPv4, MAC, gateway, hostname); never makes a network call. |
| §3.2 | `BannedPagePresentationTests` — banned IDs do not appear in roster VM; appear in banned VM. |
| §3.3 | `AdminMarkerPresentationTests` — roster row with admin ID exposes `IsAdmin = true`; non‑admins do not. |
| §4.x | Add per‑feature ViewModel tests + Core builders (e.g., `RecommendedActionsBuilderTests`, `NetworkConfidenceScoreTests`). |
| §5.x | `RemoteConsoleCommandTests` (pure builders); `RemoteConsoleResponseParserTests`. Infrastructure client behind an integration tag. |

---

## 10. Out of scope (for now)

- Importing single‑player saves into a dedicated server world.
- Scheduled / cron‑style automatic backups (still manual + automatic on
  risky actions).
- Per‑world bespoke server installs (one shared install remains the model).
- Cross‑platform support (Windows‑only WPF is intentional).

---

## 11. Open questions (resolve before the relevant task starts)

- **§2.1** When migrating an existing world whose `sandboxIniPath` points
  inside the server install: do we leave the old file in place permanently,
  or offer cleanup *after* the new path proves stable across a server
  reinstall?
- **§3.1** Which public IP endpoint is least invasive? (Plaintext, no
  tracking, no rate limit.) Candidates: `ifconfig.me/ip`,
  `api.ipify.org`, `checkip.amazonaws.com`. Decide and document in
  `HttpPublicIpProbe`.
- **§3.3** Glyph choice — `⁺`, `★`, `✦`, or a small shield icon? Tie to
  `Themes/Overseer.xaml` palette.
- **§4.9** Does AF actually emit a session/lobby short code in
  `LogOnlineSession`? Capture a real session log first; do not invent.
- **§5.1** Confirm AF's `AbioticRemoteConsole` HTTP shape, auth, and
  command surface from a live enabled server before any UI is wired.
