# Facility Overseer Master Correction Plan

Snapshot: 2026-05-18

This replaces the separate visibility and install/update notes. The older files are useful history, but this is the plan to review and correct going forward.

## Progress Table

| Done | Task | Status |
|---|---|---|
| [x] | Single data root | Implemented for new installs: config, logs, backups, tools, and server files derive from one data root |
| [x] | First-run setup | Implemented: first launch offers to prepare the app-managed server folder; skipping leaves Start disabled |
| [x] | Server install state | Implemented: missing/empty/invalid/unmanaged/SteamCMD-managed detection |
| [x] | Start gating | Implemented: Start requires a launchable detected server executable |
| [x] | Version display | Implemented: header shows `Facility Overseer v<asm>` + server build/state from `appmanifest_2857200.acf`, bound (not hardcoded) |
| [x] | Install/update flow | Implemented: Prepare / Update uses SteamCMD under the app-managed data root |
| [x] | Runtime health state | Implemented: `ServerHealthTracker` → Stopped/Starting/Online/Blocked/Crashed from readiness + blocking log signals; surfaced in status + detail |
| [x] | Server-list readiness | Implemented: Network tab UDP game/query + A2S checks, plus log-readiness driving the Online state |
| [x] | Player roster/activity | Implemented: real AF login/join/entered/PlayerCount parsing → durable per-world roster; see `docs/PLAYER_ROSTER_ACTIVITY_PLAN.md` |
| [x] | Corrupt-world recovery | Implemented: Blocked state on corruption signal; Open World Folder always opens an existing folder; Create Fresh World quarantines (never deletes) |
| [x] | Migration/cleanup | Implemented (conservative): detect old roots, COPY small config on approval, write migration report; old folders never moved/deleted (cleanup stays user-driven by design) |
| [x] | Tests | Implemented: 241 tests incl. roster/health/migration/path layout/install-state; full suite passing, 0 warnings |
| [x] | Docs | Updated: README, plan docs, NETWORK_TAB/NETWORK_EXPOSURE describe the app-managed data root and features |
| [x] | Logs | Implemented: `AutoScrollBehavior` keeps the log pinned to newest; pauses when the user scrolls up; resumes at bottom. Warning lines yellow, errors red |
| [x] | Files | Single data root; rewrite-heavy tools/servers auto-relocate off synced (OneDrive) roots; legacy roots imported by copy on approval. Source folders are never auto-deleted (explicit user choice) |

## Key Decisions

### 1. Do Not Store Files All Over Windows

Current behavior uses multiple places:

- `%APPDATA%\FacilityOverseer` for persistent world profiles.
- `%LOCALAPPDATA%\FacilityOverseer` for logs, backups, SteamCMD tools.
- `C:\Facility Overseer` for the dedicated server install.
- older paths such as `C:\AbioticFactorServer` may still exist from earlier testing.

That is too easy to misunderstand and too hard to reset. The app should use one canonical data root.

Recommended default:

`<folder containing FacilityOverseer.exe>\FacilityOverseerData`

Example for the current publish folder:

`C:\Users\kidds\OneDrive\Documents\Abiotic Factor Server Manager\publish\FacilityOverseerData`

Fallback if the exe folder is not writable:

`%LOCALAPPDATA%\FacilityOverseer`

The app should show the active data root in Settings/About and provide an `Open Data Folder` button.

### 2. Do Not Use Roaming AppData

There is no strong reason for this app to split state between Roaming and Local.

Everything should live under the single data root:

```text
FacilityOverseerData/
  config/
    settings.json
    instances.json
    schema-cache.json
  logs/
    overseer-YYYYMMDD.log
  backups/
  tools/
    steamcmd/
  servers/
    abiotic-factor-dedicated/
  worlds/
    managed app-side config when needed
```

### 3. Do Not Put the Dedicated Server Inside the Exe

The `.exe` should not hold the Abiotic Factor dedicated server files.

Reasons:

- The dedicated server is about 2.97 GB locally.
- The server is mutable and updated through Steam.
- A single-file exe is not a practical writable container for server files, logs, saves, backups, and Steam manifests.
- Replacing the app exe during updates would risk destroying or orphaning server state if the exe were treated as storage.
- Bundling third-party server files creates size, licensing, and update problems.

The right model is:

- the app exe is the manager
- the data root beside it contains all mutable files
- SteamCMD installs/validates/updates the server under that data root

### 4. No Default `C:\Facility Overseer` Install

Do not default to `C:\Facility Overseer`.

Recommended server path:

`<DataRoot>\servers\abiotic-factor-dedicated`

Users can still choose an existing server folder, but app-managed installs should stay under the single data root.

## Previous Verified Behavior Before This Update

Current saved world before cleanup:

- World: `TorchWood`
- Install path: `C:\Facility Overseer`
- Server executable exists:
  - `C:\Facility Overseer\AbioticFactor\Binaries\Win64\AbioticFactorServer-Win64-Shipping.exe`
- Steam app manifest exists:
  - `C:\Facility Overseer\steamapps\appmanifest_2857200.acf`
- Local dedicated-server app id: `2857200`
- Local server build id: `23174893`
- Last updated: `2026-05-18 17:51:10 -04:00`

Previous startup flow:

1. App loads worlds from `%APPDATA%\FacilityOverseer\instances.json`.
2. If no worlds exist, it creates a default world.
3. It checks only the selected world's `InstallPath`.
4. If an executable is found, the install prompt is skipped.
5. Start is enabled if no world is already running.
6. Start can launch any valid executable found at the install path, even if the Install button was never clicked in this app session.

That explains why the app can still know `TorchWood` after partial cleanup: `instances.json`, logs, backups, or server world files can remain in one of the old locations.

Current implemented behavior:

1. New app paths resolve to one data root.
2. Config, logs, backups, SteamCMD, and managed server files derive from that root.
3. The normal prepare/update command targets `<DataRoot>\servers\abiotic-factor-dedicated`.
4. The first-run prompt offers to prepare that managed server folder.
5. Start is disabled until the selected server folder contains a launchable dedicated server executable.
6. Existing external server folders remain usable through the advanced Server Folder field.

## Desired First-Run Flow

On first launch, show setup focused on the app-managed model:

- `Prepare Server`: download SteamCMD if needed and install/validate the dedicated server into `<DataRoot>\servers\abiotic-factor-dedicated`.
- `Skip For Now`: allow configuration only, but keep Start disabled until a valid server exists.
- Advanced adoption: users can still point the Server Folder field at an existing dedicated server folder.

Also show the data root choice:

- `Portable beside app` if the exe folder is writable.
- `User data folder` if the exe folder is not writable or the user chooses it.
- `Custom folder` for advanced users.

The setup should clearly say where files will be stored before it writes anything.

## Server Install State

Add a service that evaluates the server install path.

States:

- `Missing`: no path configured or folder absent.
- `EmptyFolder`: folder exists but contains no server.
- `InvalidFolder`: folder exists but does not contain the dedicated server.
- `DetectedUnmanaged`: executable exists but no Steam manifest was found.
- `SteamCmdManaged`: executable and `appmanifest_2857200.acf` exist.
- `Installing`: install is running.
- `Updating`: validate/update is running.
- `Ready`: install is valid and launchable.

The service should report:

- data root path
- server install path
- executable path
- manifest path
- server app id
- local build id
- last updated time
- install source
- validation message

## Start Button Gating

Start should be disabled unless:

- selected world exists
- no other world is running
- install state is `Ready`, `SteamCmdManaged`, or accepted `DetectedUnmanaged`
- server executable can be located
- install/update is not running
- blocking config errors are absent

If Start is disabled, show a concise reason:

- `Install server files before starting.`
- `Choose an existing dedicated server folder.`
- `Selected folder does not contain the dedicated server executable.`
- `Server update is currently running.`
- `Configuration has blocking errors.`

## Install / Update Flow

Use SteamCMD as the source of truth.

Button behavior should be stateful:

- missing: `Prepare Server`
- unmanaged: `Adopt / Validate Server`
- managed ready: `Check for Updates`
- unknown/stale: `Validate / Update Server`
- running operation: disabled with progress

Update operation:

1. Ensure SteamCMD exists under `<DataRoot>\tools\steamcmd`.
2. Run `app_update 2857200 validate`.
3. Refresh manifest metadata.
4. Show local build id and update time.
5. Do not start the server automatically after update unless the user explicitly asks.

## Header Version Display

Add compact version/build text near `CASCADE SERVER CONTROL`.

Examples:

```text
Facility Overseer v1.3 | Server not installed
Facility Overseer v1.3 | Server build 23174893 | SteamCMD managed
Facility Overseer v1.3 | Server detected | Update status unknown
```

App version should come from assembly metadata, not hardcoded XAML text.

Server build should come from `appmanifest_2857200.acf` when available.

## Runtime Health and Server Visibility

The app should not equate "process exists" with "server is online."

Desired runtime states:

- `Stopped`: no managed process exists.
- `Starting`: process exists, readiness not proven.
- `Online`: game port is bound and log shows playable map/session startup.
- `Listed`: query checks pass and network/listing checks look good.
- `Blocked`: process exists, but a fatal log/config/network condition is present.
- `Stopping`: stop requested.
- `Crashed`: process exited unexpectedly.

Readiness checks:

- parse logs for game net driver listening on game port
- parse logs for facility map loaded
- parse logs for session creation completed
- verify UDP game port is bound
- verify UDP query port is bound
- verify A2S query response locally

Blocking log signals:

- corrupt world save
- failed backup restore
- dedicated server will shut down
- failed port bind
- invalid sandbox/admin path
- session creation failure
- EOS/Steam session creation errors

## Corrupt-World Recovery

When the server reports world corruption:

- show `Blocked`, not just `Running`
- show the exact world name
- show the world folder path
- offer `Open World Folder` - We should always offer to open world folders.
- offer `Create Fresh World`

`Create Fresh World` should:

1. stop the server
2. move the corrupt world folder to a timestamped quarantine folder
3. regenerate or preserve valid `SandboxSettings.ini`
4. restart only after confirmation

Never silently delete world data.

## Migration Plan

On first launch after this change:

1. Detect old roots:
   - `%APPDATA%\FacilityOverseer`
   - `%LOCALAPPDATA%\FacilityOverseer`
   - `C:\Facility Overseer`
   - `C:\AbioticFactorServer`
2. Ask before moving data.
3. Copy or move recognized data into the single data root.
4. Preserve a migration report:
   - `<DataRoot>\logs\migration-YYYYMMDD-HHMMSS.log`
5. Mark old folders as migrated or leave them untouched until the user approves cleanup.
6. Do not delete old folders automatically.

Migration mapping:

```text
%APPDATA%\FacilityOverseer\instances.json
  -> <DataRoot>\config\instances.json

%APPDATA%\FacilityOverseer\settings.json
  -> <DataRoot>\config\settings.json

%LOCALAPPDATA%\FacilityOverseer\logs
  -> <DataRoot>\logs

%LOCALAPPDATA%\FacilityOverseer\backups
  -> <DataRoot>\backups

%LOCALAPPDATA%\FacilityOverseer\tools\steamcmd
  -> <DataRoot>\tools\steamcmd

C:\Facility Overseer
  -> <DataRoot>\servers\abiotic-factor-dedicated
```

## Reset Behavior

Add an explicit reset/help section in the app:

- `Open Data Folder`
- `Open Server Folder`
- `Open Logs Folder`
- `Export Diagnostics`
- `Reset App Configuration`
- `Reset Everything Managed By Facility Overseer`

`Reset Everything` should only operate inside the active data root unless the user explicitly adds old folders to cleanup.

## Tests

Add tests for:

- data root selection
- portable data root when exe folder is writable
- fallback data root when exe folder is not writable
- no Roaming path usage in new installs
- old Roaming/Local data migration
- missing install path
- empty install folder
- executable exists with no manifest
- executable plus `appmanifest_2857200.acf`
- appmanifest build id parsing
- Start disabled when install is missing
- Start enabled when install is ready
- first-run setup shown when no valid install exists
- first-run setup skipped or replaced by "server detected" when install is valid
- runtime health transitions
- corrupt-world blocked state

## Manual Verification

Fresh install:

1. Delete or rename old roots.
2. Launch app.
3. Confirm setup shows data root and install options.
4. Confirm Start is disabled before install/adoption.
5. Install server.
6. Confirm all files are under one data root.
7. Confirm version/build text appears.
8. Start server.
9. Confirm status moves from `Starting` to `Online` or `Blocked` with a reason.

Existing install:

1. Put valid server files in a chosen folder.
2. Launch app.
3. Choose `Use Existing Server`.
4. Confirm app classifies it as managed or unmanaged.
5. Confirm version/update status is shown.
6. Confirm Start behavior matches install state.

Migration:

1. Create old Roaming/Local/C-root test data.
2. Launch app.
3. Confirm migration prompt.
4. Confirm data moves or copies to one root only after approval.
5. Confirm old folders are not silently deleted.

## Open Questions

- Should the default data root be portable beside the exe, or should the first-run setup always ask?
- Should all worlds share one server install, or should advanced users be allowed per-world server installs?
- Should unmanaged installs be launchable immediately, or require SteamCMD adoption first?
- Should update checks run automatically on launch, or only when the user clicks the update button?
- What exact app version should be displayed first: `v1.3` or another value?

## Recommendation

Use a portable single-root model:

- app exe remains small and self-contained
- mutable files live beside it under `FacilityOverseerData`
- SteamCMD installs server files under that root
- Start is disabled until install state is valid
- app and server versions are visible in the header
- old AppData and `C:\` paths are migrated only with user approval
