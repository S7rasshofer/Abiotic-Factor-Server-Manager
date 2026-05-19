# Player Roster and Activity Plan

Snapshot: 2026-05-18 local / 2026-05-19 log UTC

## Status: IMPLEMENTED (2026-05-19)

- `PlayerRosterEvent` / `PlayerRosterEntry` / `PlayerRosterTracker` +
  `PlayerRosterParser` added in `AbioticServerManager.Core.Runtime`. Real AF
  lines parsed: accepted connection (remote address), `Login request`
  (name / ConnectID / SteamID64 / platform), `Join succeeded`,
  `CHAT LOG: … has entered the facility`, `PlayerCount`, server-stop. Generic
  "X joined/left" retained as fallback. Stable identity: SteamID64 > connect id
  > name.
- Per-world persistence at `<DataRoot>/players/<worldId>/roster.json`
  (`IPlayerRosterStore` / `JsonPlayerRosterStore`); seeded on world load, saved
  on identity/session changes; volatile online/session state stripped before save.
- `Logs & Status > Players` rebuilt roster-first: online status, name, ID,
  platform, address, current session, last seen; recent roster activity and play
  sessions kept secondary. Header shows `Players Online: n/Max` plus server
  `PlayerCount` and a mismatch warning.
- On server stop/crash all sessions close and players go offline.
- Tests: `PlayerRosterTrackerTests` cover the observed TorchWood sequence,
  player-count mismatch, server stop, and the persist→reseed→reconnect round-trip.

### Update 2026-05-19 (real-log hardening)

- **Source fix:** the captured process stdout only carries `Display`-level lines,
  so `LogNet:` login/connection detail (SteamID, platform, address, disconnect)
  was missing — that is why ID/Platform/Address were blank. Roster + health are
  now driven by a follower of `<install>/AbioticFactor/Saved/Logs/AbioticFactor.log`
  (`AbioticServerLogTail`), which starts on server start and stops on stop.
  stdout still feeds the live Log view + generic sessions (no double counting).
- **Disconnect captured** (Open Question resolved): real shape is
  `LogNet: UNetConnection::Close: ... UniqueId: ...EOSPlus..[0x..]_+_|<connectIdHex>`.
  Players are now marked offline individually by correlating that connect-id hex
  to the login's `ConnectID`; server-stop still offlines everyone as a fallback.
- **Parser fix:** full `ConnectID` is captured (so SteamID64 / PrimaryId /
  platform populate) and `NotifyAcceptedConnection` `RemoteAddr` is parsed.
- **Moderation:** roster rows are selectable; **Ban / Unban** edit the real
  sectioned `Admin.ini` `[BannedPlayers]` (preserving `[Moderators]`, comments,
  examples). A live **kick** is intentionally not faked: Abiotic Factor only
  supports it via its remote console (`LogAbioticRemoteConsole … explicitly
  disabled` by default) — Ban + optional restart is offered instead.

## Current Test Note

The user is currently connected to the TorchWood dedicated server over the direct LAN IP.

Observed in the live Abiotic Factor server log:

- Remote LAN client: `192.168.254.3:54007`
- Server accepted the connection at log timestamp `2026.05.19-03.17.00`
- Player login name: `S7razzy`
- Connect ID prefix: `76561198104903704`
- Platform: `EOSPlus`
- Join confirmation: `Join succeeded: S7razzy`
- In-game arrival signal: `CHAT LOG:  S7razzy has entered the facility.`
- Session browser player count changed from `PlayerCount = 0` to `PlayerCount = 1`

This proves that the Players view should be driven by Abiotic Factor's real Unreal/EOS log lines, not only the placeholder patterns currently used by `PlayerActivityParser`.

## Existing State

- The app already has a `Logs & Status > Players` tab.
- `ServerProcessService` forwards server stdout/stderr into `MainViewModel.OnLogReceived`.
- `ServerInstanceViewModel.ApplyPlayerActivity` updates:
  - `ActivePlayers`
  - `PlayerActivityHistory`
  - `PlayerSessions`
  - `PlayerActivityStatusText`
- `PlayerActivityTracker` is runtime-only. It does not persist known players across app restarts.
- The current parser handles generic text like `Player "Alice" joined` and `Alice left the server`.
- Real Abiotic Factor dedicated-server logs use lines such as:
  - `Login request: ?Name=S7razzy??ConnectID=76561198104903704_+_|... platform: EOSPlus`
  - `CHAT LOG:  S7razzy has entered the facility.`
  - `Join succeeded: S7razzy`
  - `EOS_SessionModification_AddAttribute() named (PlayerCount) with value (1)`

## Goal

Turn the Players tab into a durable player list that shows who is online now and what each player has recently done.

The expected first version should show:

- Online status per player
- Display name
- SteamID64 / connect ID when present
- Platform when present
- Remote address when present
- Current session start time
- Last seen time
- Session duration
- Recent activity rows
- Server-reported player count, when available

## Data Model

Add a roster model in the core runtime layer:

- `PlayerRosterEntry`
  - `DisplayName`
  - `PrimaryId`
  - `SteamId64`
  - `Platform`
  - `RemoteAddress`
  - `IsOnline`
  - `CurrentSessionStartedAt`
  - `LastSeenAt`
  - `LastActivity`
  - `TotalSessions`
- `PlayerRosterEvent`
  - `Timestamp`
  - `Kind`
  - `DisplayName`
  - `PrimaryId`
  - `SteamId64`
  - `Platform`
  - `RemoteAddress`
  - `PlayerCount`
  - `RawLine`

Recommended event kinds:

- `ConnectionAccepted`
- `LoginRequested`
- `EnteredFacility`
- `JoinSucceeded`
- `PlayerCountChanged`
- `Disconnected`
- `ServerStopped`

Use stable identity in this order:

1. SteamID64/connect ID prefix, when present.
2. EOS/user id token, when present.
3. Display name, only as a fallback.

## Parser Plan

Extend or replace `PlayerActivityParser` with real Abiotic Factor patterns:

1. Parse accepted LAN/remote endpoint:
   - Source: `NotifyAcceptedConnection` / `AddClientConnection`
   - Capture: remote address, Unreal connection name.
2. Parse login request:
   - Source: `Login request: ?Name=...??ConnectID=... userId: ... platform: ...`
   - Capture: display name, connect ID, SteamID64 prefix, platform.
3. Parse join success:
   - Source: `Join succeeded: <name>`
   - Mark that player online if a prior login request exists, or create a name-only entry.
4. Parse in-game arrival:
   - Source: `CHAT LOG:  <name> has entered the facility.`
   - Treat as an online/activity confirmation, not as a duplicate session.
5. Parse player count:
   - Source: `EOS_SessionModification_AddAttribute() named (PlayerCount) with value (N)`
   - Store as server count. If count conflicts with roster, show a warning state instead of silently overwriting known players.
6. Parse disconnect/leave once we capture real lines from a player exit.
   - Until then, infer all players offline on managed server stop.

## UI Plan

Replace the top section of `Logs & Status > Players` with a roster-first view:

- Header: `Players Online: {onlineRosterCount}/{MaxPlayers}` plus optional `Server count: {PlayerCount}`.
- Roster list columns:
  - Status
  - Player
  - ID
  - Platform
  - Address
  - Current session
  - Last seen
- Activity list remains below or beside the roster, but should be secondary.
- Keep play sessions, but bind them to roster events rather than only generic join/leave text.
- When no real player signals have been seen, show the current empty state.

## Persistence Plan

Create a per-world roster state file under the app data root:

```text
<DataRoot>/players/<worldId>/roster.json
```

Persist only durable, non-secret player facts:

- display name
- stable ID / SteamID64
- platform
- first seen
- last seen
- total sessions

Do not persist volatile remote ports as durable identity. The latest remote address can be held in memory and optionally persisted as last-seen diagnostic context.

## Implementation Steps

1. Add real log-line parser tests from the observed TorchWood log lines.
2. Add `PlayerRosterEvent`, `PlayerRosterEntry`, and `PlayerRosterTracker` in `AbioticServerManager.Core.Runtime`.
3. Preserve existing generic join/leave support as fallback test coverage.
4. Update `ServerInstanceViewModel` collections from string/session-only data to roster entries plus activity events.
5. Update the Players tab XAML to show roster rows first and activity second.
6. Add a roster persistence service in infrastructure.
7. Load roster state when worlds are loaded and save it after player identity/session changes.
8. On server stop/crash, close active sessions and mark online players offline.
9. Add manual QA:
   - Start server
   - Join over LAN IP
   - Confirm player appears online
   - Confirm SteamID64/connect ID is captured
   - Confirm `PlayerCount` matches online count
   - Leave server
   - Capture disconnect log shape and add parser coverage
   - Restart app and confirm known player remains listed as offline/last seen

## Open Questions

- Need one real disconnect log sample from a client leaving normally.
- Need one crash/timeout disconnect sample if the client closes unexpectedly.
- Need confirmation whether `ConnectID` always starts with SteamID64 for Steam players and what it looks like for other platforms.
- Need to decide whether remote LAN IP should be displayed by default or hidden behind a diagnostic/details expander.
