# Facility Overseer — Current Build & Architecture

> Audience: the team and AI agents planning a refactor / the new admin toolkit.
> Scope: what exists today (verified against source), how a server is generated
> end‑to‑end, the window/tab structure, how dynamic the app is, and where the
> proposed RCON toolkit would plug in.
> Snapshot: 2026‑05‑19. Tests: 252 passing, 0 build warnings.

---

## 1. Table of Contents

1. Product summary
2. Solution & project layout
3. Layering, DI, and the “discover, don’t hardcode” ethos
4. The single data root (and the OneDrive problem)
5. End‑to‑end: how a server is generated and launched
6. Runtime pipeline: process, logs, roster, health
7. The dynamic sandbox/schema engine
8. Persistence & state
9. Networking / firewall subsystem
10. UI structure — windows, horizontal world tabs, vertical feature tabs
11. How dynamic the app is (summary for refactor planning)
12. Build, publish, and the auto‑publish hook
13. Testing
14. Known limitations / debt
15. Proposed toolkit: RCON / remote admin (not yet built)
16. Refactor hot‑spots & suggested seams

---

## 2. Product summary

Facility Overseer is a **Windows WPF desktop app** that installs, configures,
launches and monitors **Abiotic Factor dedicated servers**. It manages multiple
“worlds” (server profiles), each on its own horizontal tab, and exposes
configuration through buttons/sliders/toggles instead of hand‑editing INI files.
Distribution is a **single self‑contained `FacilityOverseer.exe`** (no .NET
install required).

Motto baked into the design: *“Do not hardcode the facility. Discover it.”* —
unknown game settings and extra launch args are preserved verbatim rather than
dropped.

---

## 3. Solution & project layout

`AbioticServerManager.slnx` → four projects, `net10.0` (App is
`net10.0-windows`, WPF):

| Project | Responsibility | Notable: |
|---|---|---|
| `AbioticServerManager.Core` | Pure domain logic, models, contracts. **No IO.** | Networking math, parsers, schema, validation, planners. Highly unit‑tested. |
| `AbioticServerManager.Infrastructure` | IO‑bound implementations | SteamCMD, process, persistence, firewall PowerShell, log tail, migration. |
| `AbioticServerManager.App` | WPF MVVM shell | `MainWindow.xaml`, ViewModels, converters, behaviors. |
| `AbioticServerManager.Tests` | xUnit | 252 tests; Core logic is the bulk. |

`Directory.Build.props`: `Nullable=enable`, `ImplicitUsings=enable`,
**`TreatWarningsAsErrors=true`**, `LangVersion=latest`. Central package
management via `Directory.Packages.props`. Single‑file publish settings live in
`AbioticServerManager.App.csproj` (`PublishSingleFile`, `SelfContained`,
`win-x64`, embedded debug).

Composition root: `App.xaml.cs` builds a generic `Host`, registers
`AddOverseerCore()` + `AddOverseerInfrastructure()` + `MainViewModel` +
`MainWindow`, Serilog to console + rolling file (`<DataRoot>/logs/overseer-*.log`),
and a global `DispatcherUnhandledException` handler (logs + “it was logged”
dialog).

---

## 4. Layering, DI, and ethos

- **Core has zero IO** and no WPF. Everything testable is a pure static/record
  in Core (`Networking/*`, `Runtime/*Parser`, `Runtime/*Tracker`, `Schema/*`,
  `Admin/AdminIniBanEditor`, `Migration/LegacyDataLocations`,
  `Worlds/WorldSaveLayout`). This is the single most important refactor‑friendly
  property: behavior is verified without a server, a registry, or a network.
- **Infrastructure** implements Core interfaces and is the only place that
  touches the filesystem, processes, PowerShell, HTTP, or sockets.
- **App** is MVVM (CommunityToolkit.Mvvm). ViewModels orchestrate Core +
  Infrastructure interfaces; XAML binds to ViewModels. No business logic in
  code‑behind (only `Behaviors/` attached properties).
- DI: constructor injection throughout; everything is a singleton.

---

## 5. The single data root (and the OneDrive problem)

`IAppPaths` / `AppPaths` is the **canonical location authority**. Everything the
app writes derives from one `DataRoot`:

```
<DataRoot>/
  config/    settings.json, instances.json, schema-cache.json, .legacy-migration-done
  logs/      overseer-YYYYMMDD.log, steamcmd-report-*.txt, migration-*.log
  backups/
  players/   <worldId>/roster.json
  tools/     steamcmd/
  servers/   abiotic-factor-dedicated/
```

Resolution rules (verified in `AppPaths`):

- `DataRoot` = `<exe folder>/FacilityOverseerData` if that folder is writable,
  else `%LOCALAPPDATA%/FacilityOverseer`.
- **Critical nuance — `VolatileRoot`**: SteamCMD and the dedicated server are
  rewritten constantly; OneDrive/Dropbox/Google‑Drive lock/dehydrate files
  mid‑write (“Failed to load steam.dll”, Win32 error 32). So when `DataRoot` is
  a synced location, `tools/` and `servers/` are redirected to a non‑synced
  `%LOCALAPPDATA%/FacilityOverseer` while `config/backups/logs/players` stay put
  (small, sync‑safe, no data loss). `AppPaths.IsSyncedLocation()` is the pure,
  tested classifier.
- **Legacy migration** (`ILegacyMigrationService`): detects old roots
  (`%APPDATA%`, `%LOCALAPPDATA%`, `C:\Facility Overseer`, `C:\AbioticFactorServer`),
  offers a one‑time **copy** of small config into the current root, writes a
  `migration-*.log`, and **never moves or deletes** source. Runs first in
  `MainViewModel.InitializeAsync`.

### 5.1 Observed behavior — “deleting `%LOCALAPPDATA%` does not reset the app”

> Verified on a live build (2026‑05‑19) by inspecting disk. This is **current
> behavior, not a proposed change.** Recorded here for the refactor review.

With the exe at `…\OneDrive\Documents\…\publish\FacilityOverseer.exe`:

- **Real `DataRoot` = `…\publish\FacilityOverseerData`** (exe folder is writable
  → portable‑beside‑exe). That folder is inside OneDrive, so the OneDrive‑safety
  split applies.
- **`VolatileRoot` = `%LOCALAPPDATA%\FacilityOverseer`** receives **only**
  `tools/` (SteamCMD) and `servers/` (the dedicated server install).
- Everything else stays in `DataRoot` (beside the exe, OneDrive side):
  `config/instances.json` (world profiles), `config/schema-cache.json`,
  `players/<id>/roster.json`, `logs/`.

Confirmed contents at the time of writing:

| Path | Lives in | Survives deleting `%LOCALAPPDATA%\FacilityOverseer`? |
|---|---|---|
| `config/instances.json` (the **TorchWood tab/profile**) | DataRoot (OneDrive) | **Yes** |
| `players/<id>/roster.json` (player roster) | DataRoot (OneDrive) | **Yes** |
| `config/schema-cache.json`, `logs/` | DataRoot (OneDrive) | Yes |
| SteamCMD, dedicated server install, **world save** | VolatileRoot (AppData\Local) | **No — deleted** |
| **Staged `SandboxSettings.ini`** | VolatileRoot — *inside the server install* (`…\servers\…\AbioticFactor\Saved\Config\FacilityOverseer\<id>\`) | **No — deleted** |
| `Admin.ini` (admins/bans) | inside the server install | **No — deleted** |

Consequences (all expected given the above, but surprising to users):

1. **The world tab persists** after deleting `%LOCALAPPDATA%\FacilityOverseer`,
   because the profile is `instances.json` in `DataRoot`, which is *not* in
   `%LOCALAPPDATA%`. Profiles are intentionally decoupled from server payload.
2. **Sandbox settings and the world save are lost**, because the staged
   `SandboxSettings.ini` (and `Admin.ini`) live *inside the server install path*
   (`instance.sandboxIniPath`/`adminIniPath` point under
   `%LOCALAPPDATA%\…\servers\…`). They die with a server wipe **or a normal
   server reinstall/repair.**
3. **The player roster is NOT lost** — `roster.json` is under `DataRoot/players`
   (OneDrive side). An empty Players tab after a wipe is a *different* cause
   (name‑only pre‑log‑tail entries, or simply shown offline), not deletion.
4. **“Delete the data folder ⇒ clean slate” is false** with the current model:
   the canonical config root is `…\publish\FacilityOverseerData`, not
   `%LOCALAPPDATA%`. A true reset must clear `DataRoot` (esp.
   `config/instances.json` and `players/`). The Welcome popup shows the
   *VolatileRoot server path* (`%LOCALAPPDATA%\…\servers\abiotic-factor-dedicated`),
   which reinforces the misconception that that is “the data folder.”

**Design implications (debt — see §14/§16):** two surprising couplings —
(a) per‑world **sandbox/admin settings are bound to the server install path**
rather than to `DataRoot`, so they do not survive a server reinstall; and
(b) the **state model spans two physical roots** with no single “reset”, and the
first‑run popup points at the wrong one. The OneDrive split *correctly* prevents
`steam.dll` corruption, but at the cost of this mental‑model confusion.

---

## 6. End‑to‑end: how a server is generated and launched

This is the core pipeline a refactor must preserve. Player presses
**Prepare / Update Server** (header) → `MainViewModel.InstallOrUpdateServer`:

1. **Resolve target**: `installPath = _paths.ManagedServerDirectory`
   (`<VolatileRoot>/servers/abiotic-factor-dedicated`); set on the world; save.
2. **SteamCMD bootstrap** (`SteamCmdService`):
   - If `steamcmd.exe` absent → `ExtractFreshSteamCmdAsync`: wipe dir, download
     `steamcmd.zip`, extract.
   - Run `steamcmd +quit` (bootstraps/self‑updates), retry once.
   - **Self‑heal**: if output matches `SteamCmdDiagnostics.LooksLikeSelfUpdateFailure`
     (`Failed to load steam.dll`, `Failed to apply update, reverting`,
     `BCommitUpdatedFiles … (error 32)`), do one clean reinstall; on final
     failure return the actionable `SelfUpdateHelp` text + write a
     `steamcmd-report-*.txt`.
3. **Auto‑backup** before an over‑install if the folder is non‑empty.
4. **App update**: `steamcmd +force_install_dir <path> +login anonymous
   +app_update 2857200 validate +quit`; one retry; repairs SteamCMD if it
   broke; surfaces a structured `SteamCmdResult` (+ log path on failure).
5. Progress streams via `IProgress<InstallProgress>` → `MainViewModel.ApplyBusyProgress`
   → the busy strip (determinate bar + % + phase title + live tail line).
6. After success, sandbox auto‑loads (see §7), install state re‑evaluated.

**Install‑state classification** (`ServerInstallStateService` →
`ServerInstallState`): `Missing / EmptyFolder / InvalidFolder / DetectedUnmanaged
/ SteamCmdManaged` with executable path, `appmanifest_2857200.acf` build id,
last‑updated, source. `IsLaunchable` gates **Start**. The header shows
`Facility Overseer v<asm> | <server state/build>` (bound, not hardcoded).

**Executable discovery** (`IServerExecutableLocator`): finds
`AbioticFactorServer*-Win64-Shipping.exe` by search, preferring
Shipping/Win64/Binaries — discovered, never hardcoded.

**Launch** (`StartServer` → `ServerProcessService.StartAsync`):

- `LaunchArgumentBuilder` builds argv from the world model:
  `-SteamServerName -WorldSaveName -MaxServerPlayers -PORT -QUERYPORT`
  (+ optional `-ServerPassword -AdminPassword -SandboxIniPath -AdminIniPath
  -MultiHome -LANOnly -UseLocalIPs -PlatformLimited`), then **`AdditionalLaunchArguments`
  appended verbatim** (forward‑compat). A masked variant is logged (secrets → `********`).
- Process started with stdout/stderr **redirected** (no `-newconsole`), stdin
  redirected (used for graceful “exit”). Wrapped in a **Win32 Job Object**
  (`Win32JobObject`) so a detached child server is still tracked/killed.
- Stop: write `exit` to stdin → wait → job terminate → kill tree fallback.

**World save layout** (`WorldSaveLayout` + `SandboxResolution`): the app
**stages** `SandboxSettings.ini` under
`AbioticFactor/Saved/Config/FacilityOverseer/<instanceId>/` and passes a
Saved‑relative `-SandboxIniPath`, so it never creates a partial/“corrupt” world
folder before the game does. Real‑save detection is tolerant (case‑insensitive
match inside `SaveGames/Server/Worlds`, never matches another world). One pure
`Resolve()` decides: real‑save path vs staged path vs migration source.

---

## 7. Runtime pipeline: process, logs, roster, health

Two log sources feed the app while a server runs:

- **Process stdout/stderr** → `ServerProcessService.LogReceived` →
  `MainViewModel.OnLogReceived` → in‑app **Log** view (`LogLines`, color‑coded by
  `ServerLogLine.Severity`: red error / yellow warning), generic
  `PlayerActivityTracker` (Play Sessions), and `ServerHealthTracker` (for the
  app’s synthetic `[server exited unexpectedly]` lines). stdout only carries
  `Display`‑level lines.
- **`AbioticFactor.log` file tail** (`AbioticServerLogTail`, started per world on
  run, stopped on stop) → `OnTailLine` → **roster** (`PlayerRosterTracker`) +
  **health** (`ServerHealthTracker`). The file has the rich `LogNet:` lines
  (login/ConnectID/SteamID64/platform, `NotifyAcceptedConnection` address,
  `UNetConnection::Close` disconnect) that stdout lacks. Roster is driven
  **exclusively** by the file tail to avoid double counting.

**Player roster** (`PlayerRosterParser` → `PlayerRosterTracker`): stable identity
SteamID64 > ConnectID > name; per‑world durable persistence at
`<DataRoot>/players/<worldId>/roster.json` (`IPlayerRosterStore`); seeded on load,
saved on identity/session change; server‑reported `PlayerCount` with mismatch
warning; disconnect correlated by connect‑id hex; offline‑all on server stop.

**Runtime health** (`ServerHealthTracker`): `Stopped / Starting / Online /
Blocked / Crashed` from readiness signals (session created, world up, net driver
listening) and blocking signals (corrupt world, port bind fail, EOS/session
failure, fatal). Drives `StatusText` + a health detail line. **Corrupt‑world
recovery**: Blocked state, “Open World Folder” always opens an existing folder,
“Create Fresh World” stops + timestamp‑quarantines (never deletes) + auto‑backup.

**Moderation (current)**: roster row selection → **Ban/Unban** edits the real
sectioned `Admin.ini` `[BannedPlayers]` (`AdminIniBanEditor`, preserves
`[Moderators]`/comments). Ban is enforced on server start; the app offers a
restart to enforce immediately. **No live kick** (requires AF remote console —
see §15).

---

## 8. The dynamic sandbox/schema engine

This is the heart of “how dynamic the app is.” Abiotic Factor’s
`SandboxSettings.ini` is a large, evolving key/value surface. The app does **not**
hardcode it:

- `DefaultSandboxSettings` — embedded `default-sandbox-settings.ini` template
  (Core embedded resource) used to seed a new world.
- `ISettingMetadataCatalog` / `SettingMetadataCatalog` — embedded
  `setting-metadata.json` (labels, categories, help text, control hints) **plus
  an optional override file** in `<DataRoot>/config/setting-metadata.json`, so
  metadata can be updated without recompiling.
- `SettingTypeInference` — when a setting has no metadata, infers the control
  (toggle/slider/dropdown/number/text) from its value, so **new/unknown game
  settings still render as usable controls**.
- `SandboxSettingsDocument` / `SandboxCategoryHeading` / `SettingDescriptor` —
  parse the INI loss‑lessly into categorized, editable settings; unknown and
  uncategorised keys are preserved and surfaced on the **Advanced** tab as raw
  editable text (nothing is ever dropped).
- `SandboxSettingsService` reads/writes; `JsonSchemaCache` caches the derived
  schema (`<DataRoot>/config/schema-cache.json`).
- App side: `SandboxSettingsViewModel`, `SettingViewModel`,
  `SettingTemplateSelector`, `SandboxCategoryPanel` render the schema generically
  via DataTemplates chosen at runtime per inferred type.

Net effect: a game update that adds settings requires **no app change** —
they appear, editable, preserved.

---

## 9. Persistence & state

| Concern | Store | File |
|---|---|---|
| World profiles | `JsonInstanceStore` (atomic, corrupt‑quarantine) | `config/instances.json` |
| Player roster | `JsonPlayerRosterStore` | `players/<id>/roster.json` |
| Schema cache | `JsonSchemaCache` | `config/schema-cache.json` |
| Admins / bans | `AdminListService` / `PlayerBanService` (`Admin.ini`) | server `Saved/SaveGames/Server/Admin.ini` |
| Backups | `FileBackupService` | `backups/` (auto before risky actions) |

`ServerInstance` (Core model) is the per‑world record: name, ports,
passwords, max players, LAN/platform flags, install/world/sandbox/admin paths,
and `AdditionalLaunchArguments`. `MainViewModel` saves are serialized through a
`SemaphoreSlim`.

---

## 10. Networking / firewall subsystem

Pure Core (`Networking/*`, all unit‑tested): `NetworkPortValidation`,
`IpAddressClassifier` (RFC1918/CGNAT/APIPA/loopback/public),
`Ipv4Selection` (best LAN NIC, demotes virtual/VPN), `RouterChecklistBuilder`,
`FirewallScriptBuilder` (idempotent elevated PowerShell, per‑world rule identity),
`FirewallInspectionParser` (PS5.1 scalar‑tolerant). Infrastructure:
`WindowsNetworkSetupService` (elevated apply via temp result file),
`DiagnosticsService` (A2S query, CGNAT/double‑NAT signal, both UDP ports +
process check), `A2SQueryClient`. Exposed on the **Network** tab.

---

## 11. UI structure — windows & tabs

Single `MainWindow.xaml` (no other windows; all dialogs are `MessageBox`).
Theme in `Themes/Overseer.xaml`. Converters in `Converters/Converters.cs`.

### Top bar (global)
`GATE` badge, **FACILITY OVERSEER** + bound `HeaderInfoText`
(`Facility Overseer v<asm> | <server state/build>`), **Prepare / Update
Server**, **Create World**, **Clone**, **Delete**, **Save**.

### Busy strip (global)
Visible when `IsBusy`: phase title, **determinate progress bar + %**, status
line, live monospace detail line (drives the SteamCMD/install UX).

### Horizontal tabs = Worlds
`TabControl ItemsSource={Binding Worlds}`; each tab is a
`ServerInstanceViewModel` (one managed world/server profile). Tab header = name +
running dot. First launch auto‑creates one ready‑to‑rename world (“My World”);
no auto‑Cascade.

### Per‑world toolbar
Start / Stop / Restart + status dot + `StatusText` (health‑driven while running).

### Vertical tabs (per world) — verified order
| # | Tab | Backed by | Purpose |
|---|---|---|---|
| 0 | **Server** | `ServerInstanceViewModel` | Name, passwords, ports, max players, LAN/platform; Open World Folder. |
| 1 | **Network** | `NetworkSetupModels` + commands | Check Setup, Create Firewall Rules, Router Checklist, LAN IPv4, CGNAT. |
| 2 | **World** | sandbox schema (World category) | Dynamic world settings. |
| 3 | **Player** | sandbox schema (Player category) | Dynamic player settings. |
| 4 | **Enemy** | sandbox schema (Enemy category) | Dynamic enemy settings. |
| 5 | **Admin** | `AdminListService` | SteamID64 admin list editor. |
| 6 | **Backups** | `FileBackupService` | Backup now / restore / open folder. |
| 7 | **Logs & Status** | runtime VMs | Nested tabs: **Log** (color‑coded, auto‑scroll via `AutoScrollBehavior`, Diagnostics) and **Players** (roster‑first: status/ID/platform/address/session/last‑seen, Recent Activity, Play Sessions, Ban/Unban). Also Validate Config / Open World Folder / **Create Fresh World**. `LogsStatusTabIndex = 7`. |
| 8 | **Advanced** | sandbox schema (uncategorised) | Raw‑preserved unknown settings. |

(World/Player/Enemy/Advanced are the **same dynamic schema engine** filtered by
category — they are not hand‑built forms.)

---

## 12. How dynamic the app is (refactor summary)

- **Per‑world everything**: N independent worlds, each its own model, sandbox,
  roster, backups, process, health, firewall rules (rule identity is per‑world).
- **Schema‑driven settings UI**: World/Player/Enemy/Advanced render from
  metadata + type inference; unknown settings still editable; nothing dropped.
- **Discovery over hardcoding**: exe location, real save folder, ports (model is
  source of truth for launch + firewall + checklist), extra launch args verbatim.
- **Self‑healing/relocating IO**: data root avoids OneDrive for volatile
  payloads; SteamCMD self‑repair; corrupt files quarantined not deleted.
- **Honest, separated status**: process ≠ online ≠ reachable; health and
  network reachability are distinct, `Unknown` is a valid answer.
- **Behavior is pure + tested**: most logic is Core statics/records → safe to
  refactor against tests.

---

## 13. Build, publish, testing

- `dotnet build AbioticServerManager.slnx`; `dotnet test`.
- Publish: `dotnet publish src/AbioticServerManager.App -c Release -o publish`
  → single `publish/FacilityOverseer.exe`.
- **Auto‑publish guard**: project `.claude/settings.json` Stop hook runs
  `tools/publish-if-stale.ps1` — rebuilds the exe only when `src/` is newer than
  it, staging the build off‑OneDrive then copying the exe in (the exe lives in a
  synced folder; this avoids the same lock that broke SteamCMD). Activation
  requires reloading `/hooks` once.
- Tests: **252 passing, 0 warnings** (`TreatWarningsAsErrors`). Heaviest
  coverage: networking math, roster/health parsers, schema, world layout,
  migration, ban editor.

---

## 14. Known limitations / debt (for the refactor)

- **Data‑split / reset confusion (see §5.1, verified on disk).** State spans
  two physical roots: profiles/roster/logs in `DataRoot` (beside the exe,
  OneDrive side) vs SteamCMD/server in `VolatileRoot` (`%LOCALAPPDATA%`).
  Deleting `%LOCALAPPDATA%\FacilityOverseer` therefore **keeps the world tabs**
  (profiles) but **destroys sandbox settings + world save** — and is *not* an
  app reset. There is no single “Reset Everything” yet, and the Welcome popup
  advertises the VolatileRoot server path as if it were “the data folder”.
- **Per‑world sandbox/admin settings are bound to the server install path**, not
  to `DataRoot`. `instance.sandboxIniPath`/`adminIniPath` point inside
  `%LOCALAPPDATA%\…\servers\…`, so a routine server **reinstall/repair or wipe
  loses all per‑world tuning and the admin/ban list**. Users reasonably expect
  settings to outlive a server reinstall. High‑priority debt.
- **No live kick** (file‑ban + restart only) — see §15.
- `AdminListService` assumes a *flat* SteamID `Admins.ini`, but the real file is
  *sectioned* `Admin.ini` (`[Moderators]`/`[BannedPlayers]`). `PlayerBanService`/
  `AdminIniBanEditor` use the correct sectioned format; the **Admin tab’s editor
  and the ban editor are not yet unified** — a refactor target.
- All dialogs are `MessageBox` — no rich modal/diagnostic panel; failures go to
  logs.
- Server‑readiness/health signals are heuristic token matches (no confirmed AF
  readiness line); fine but should be revisited if AF logging changes.
- Single `MainWindow.xaml` is large; tab content is inline (candidate for
  `UserControl` extraction per tab).
- Roster `Recent Activity`/`Roster` rebuild wholesale each event (selection
  preserved by key) — acceptable now, but a refactor could use observable diffs.

---

## 15. Proposed toolkit: RCON / remote admin (NOT built)

**Status: not implemented.** Verified: no `rcon`/`RemoteConsole` code exists.
Abiotic Factor ships its own `AbioticRemoteConsole` (HTTP/S based, **disabled by
default** — seen in the server log). Today’s moderation is file‑based
(`Admin.ini`) and enforced on (re)start.

Proposed scope (requires confirming AF’s real remote‑console command set first —
do **not** wire buttons against a guessed command list):

1. **Config**: a per‑world RCON section (enable flag, host, port, password) on
   the model; write the matching server config to enable AF’s remote console;
   keep the password out of logs/masked like other secrets.
2. **Firewall**: extend the existing per‑world `FirewallScriptBuilder` with an
   optional RCON port rule **only when RCON is enabled** (the firewall code was
   intentionally built to support this later; no RCON rule is created otherwise).
3. **Client**: an `IRemoteConsoleClient` (Infrastructure) speaking AF’s console
   protocol; Core stays pure (command builders + response parsers, unit‑tested).
4. **UI placement**:
   - **Players tab**: a real **Kick** button on roster selection (instant, no
     restart) + “Ban (live)” that bans *and* disconnects.
   - **Logs & Status / new “Console” sub‑tab**: free‑text command box + output,
     and buttons for verified‑safe commands (save world, announce/broadcast,
     list players, graceful shutdown/restart).
   - **Server tab**: RCON enable/port/password fields.
5. **Health/roster**: feed console “list players” responses to corroborate the
   log‑derived roster (resolve the `PlayerCount` mismatch authoritatively).

Tradeoff to flag to the team: enabling RCON opens a password‑protected,
network‑exposed admin port (off by default for that reason). Recommended path:
**first confirm the AF remote‑console command surface**, then implement
Core(pure command/parse) → Infrastructure(client) → minimal UI, gated behind an
explicit per‑world “Enable remote admin” opt‑in.

### 15.1 Requested: quick admin‑command palette + announcement box (future)

User request (review 2026‑05‑19): on the proposed Console sub‑tab, surface a
**curated list of common admin commands as one‑click buttons** plus a dedicated
**announcement / broadcast text box** ("type a message → send to all players").
Design intent:

- A small, data‑driven command catalog (label → command template, with optional
  argument placeholders) rendered as quick buttons, *plus* the raw free‑text box
  for anything not in the list.
- An "Announce" field with a Send button (maps to AF’s broadcast/announce
  command once confirmed) — the most‑used moderator action.
- Keep the catalog **pure/data in Core** (a list of command descriptors) so it
  is testable and editable without recompiling, mirroring the sandbox metadata
  pattern. Still gated behind the confirmed AF command set (do not guess verbs).

---

## 16. Refactor hot‑spots & suggested seams

- **Decouple per‑world settings from the server payload (high priority, see
  §5.1/§14).** Move staged `SandboxSettings.ini` and the admin/ban file under
  `<DataRoot>\worlds\<id>\` and pass an **absolute** `-SandboxIniPath`, so
  settings/admins survive a server reinstall, repair, or a `%LOCALAPPDATA%`
  wipe. Requires the existing conservative copy‑not‑delete migration for
  worlds whose `sandboxIniPath`/`adminIniPath` currently point inside the
  server install.
- **Single‑root + explicit reset.** Implement the planned “Reset Everything
  Managed By Facility Overseer” and make first‑run/About show the *canonical*
  `DataRoot` (not the VolatileRoot server path), so “delete the data folder ⇒
  clean slate” matches user expectation. Keep the OneDrive split *internally*
  (it correctly prevents `steam.dll` corruption) but stop leaking it into the
  user’s mental model.
- **Unify admin/ban**: one sectioned‑`Admin.ini` service powering both the Admin
  tab and Ban/Unban (delete the flat‑file assumption in `AdminListService`).
- **Extract tab content** into per‑tab `UserControl`s (Server/Network/Players/
  Sandbox) to shrink `MainWindow.xaml` and isolate the new Console tab.
- **Introduce an `IRemoteConsoleClient` seam now** (even stubbed) so kick/ban‑live
  and the Console tab have a stable contract before the protocol is confirmed.
- **A unified log bus**: stdout + AF‑file tail currently fan out ad‑hoc in
  `MainViewModel`; a single typed event stream would simplify roster/health/
  console correlation.
- Keep the **Core‑pure rule**: any new admin/console logic should be pure
  builders/parsers in Core with Infrastructure only doing IO — preserves the
  252‑test safety net during the refactor.

---

## 17. UX backlog & future‑plan notes (review 2026‑05‑19)

Captured from a live review. **Not implemented — recorded for the team/agents.**
Each item: *Observed → Desired → Notes/seam.*

### 17.1 Diagnostic/warning cards are too large
- **Observed:** on **Logs & Status → Log**, the `DiagnosticMessage` cards
  (e.g. `PASSWORD_EMPTY`, `ADMIN_PASSWORD_EMPTY`, `WORLD_PATH_MISSING`) render
  full‑size and stack tall. The user forced this state by deleting
  `%LOCALAPPDATA%\FacilityOverseer` (world folder missing → validation warnings;
  see §5.1).
- **Desired:** show each card **collapsed/compact by default** (severity dot +
  title only); **click to expand** the detail/suggested‑fix; **click elsewhere
  collapses** it back. Effectively an accordion/expander list.
- **Notes:** the data already supports this — `DiagnosticMessage` has
  `Severity/Code/Title/Message/SuggestedFix`. Pure ViewModel/XAML change
  (`Expander` or an `IsExpanded` per‑row + input‑binding); no Core change. Bind
  collapse‑others to selection so only one is open at a time.

### 17.2 Center the diagnostics list on the tab
- **Desired:** keep the warning/diagnostic list horizontally centered within the
  Log tab (constrained max width, centered) rather than full‑bleed left.
- **Notes:** layout‑only (XAML `HorizontalAlignment`/`MaxWidth` on the
  `ItemsControl` container). Apply consistently with the compact‑card change.

### 17.3 Color‑code the vertical (feature) tabs
- **Desired:** give the per‑world vertical tabs (Server/Network/World/Player/
  Enemy/Admin/Backups/Logs & Status) distinct colors so they’re scannable.
- **Notes:** styling on the vertical `TabControl`’s `TabItem` template in
  `Themes/Overseer.xaml`; consider a per‑tab accent keyed by tab. No logic.

### 17.4 “Settings” tabs visually distinct from the rest
- **Desired:** the settings/config tabs should use a **different color** than
  the operational tabs so configuration vs. operation reads at a glance.
- **Notes:** ties into 17.3 — define two tab color groups
  (config: World/Player/Enemy/Server/Admin vs. ops: Network/Backups/
  Logs & Status). Decide the exact grouping with the team; pure styling.

### 17.5 Remove the Advanced tab (currently empty)
- **Observed/Desired:** **Advanced** shows nothing right now → user wants it
  removed.
- **Caveat (important for the team):** Advanced is the **loss‑less catch‑all**
  for *uncategorised/unknown* sandbox keys (the “discover, don’t hardcode”
  safety net, §8). It is empty here only because no server/sandbox is loaded
  (world folder was deleted, §5.1). **Recommendation: hide the tab when it has
  zero items rather than delete it**, so a future game update that adds unknown
  settings still has a home and nothing is silently dropped. Document the
  decision before removing outright.

### 17.6 Status light is green while the world is corrupt (real bug)
- **Observed:** starting a corrupt world shows **green** dots even though the
  status text says **“Blocked — the world save appears to be corrupt.”**
- **Cause:** the dots bind `IsRunningState` through `RunningToBrushConverter`
  (bool → green/grey) — they reflect *process running*, not **health**. The
  text is health‑driven (`ServerHealthTracker`), the dot is not. So a
  briefly‑running corrupt server = green dot + “Blocked” text.
- **Desired:** a single, **health‑colored** status indicator:
  - grey = Stopped, **yellow = Starting**, green = Online,
    **red = Blocked/Crashed** (corrupt, port‑bind fail, etc.).
- **De‑duplicate:** there are **three** running dots (top‑bar world chip, world
  tab strip, Logs & Status header). Keep **one** (the tab chip is sufficient)
  and drive its color from `ServerHealth`, not `IsRunningState`.
- **Notes:** add a `ServerHealth → Brush` converter; bind the kept dot to
  `HealthStatusText`/`ServerHealth`. Core already exposes the states
  (`ServerHealthTracker`); this is a ViewModel/XAML correctness fix, not new
  logic. Cross‑ref §7, §11.

### 17.7 Investigate an Abiotic Factor “room/join code”
- **Ask:** is there a join/room code when hosting (like a short code players can
  enter), and can we surface it?
- **Notes (research item, unconfirmed):** AF uses EOS sessions; the server log
  has `LogOnlineSession: EOS …` lines (we already parse `PlayerCount`). Action:
  inspect `AbioticFactor.log` `LogOnlineSession`/EOS session lines for a
  session/lobby id or short code, and check AF docs for a “Join Code” concept.
  If one exists, parse it (pure, like the roster parser) and show it on the
  Server/Network tab as copyable text. **Do not invent** a code format — confirm
  from a real hosted session first.

### 17.8 Platform column: show “Steam (PC)” vs “Console/Other”, not “EOSPlus”
- **Accepted future change** (user‑requested 2026‑05‑19; deferred — do **not**
  implement yet).
- **Observed (verified against the real log):** every player’s `Login request`
  logs `platform: EOSPlus` and an opaque `userId: EOSPlus:UNKNOWN/INVALID
  [0x…]`. A whole‑log grep for `PSN/XBL/Playstation/Xbox/Sony/Microsoft/
  ExternalAccountType` returned **nothing** — EOS deliberately hides the origin
  platform behind the cross‑play Product User ID.
- **Desired:** replace the meaningless `EOSPlus` value in the roster Platform
  column with a derived label:
  - `ConnectID` is a SteamID64 (17 digits, `7656…`) ⇒ **“Steam (PC)”**
  - otherwise ⇒ **“Console / Other (EOS)”** (Epic‑PC, PS5, or Xbox —
    indistinguishable from logs by design).
- **Hard limit (set expectations):** true **PlayStation vs Xbox vs Epic‑PC**
  per‑player detection is **not possible from the server log**. It would require
  the EOS external‑account‑type via AF’s remote console (RCON) — unconfirmed and
  possibly unavailable from a dedicated server. Cross‑ref §15, §17.7.
- **Seam (pure, testable, low‑risk):** add a pure classifier in Core (e.g.
  `PlayerPlatform.Classify(connectId, steamId64)`) used by
  `PlayerRosterParser`/`PlayerRosterTracker`; `PlayerRosterEntry` already carries
  `SteamId64`/`PrimaryId`, so the existing `IsValidSteamId`‑style check is enough.
  Keep the raw `EOSPlus`/ConnectID available as a tooltip/secondary for support.
  No new IO; one converter‑free string + roster tests mirroring the existing
  `PlayerRosterTrackerTests`.

### Suggested priority for the refactor
1. **17.6** (status color correctness — it actively misleads: green = broken).
2. **17.5** (hide‑don’t‑delete Advanced — avoid regressing the loss‑less net).
3. **17.8** (Steam‑vs‑Console platform label — small, pure, high user value).
4. **17.1/17.2** (diagnostic card compaction + centering — quick UX win).
5. **17.3/17.4** (tab color system — cosmetic, do together).
6. **17.7** (room‑code research — gather evidence before committing).
7. **15.1** (RCON command palette + announce box — with the RCON workstream).
