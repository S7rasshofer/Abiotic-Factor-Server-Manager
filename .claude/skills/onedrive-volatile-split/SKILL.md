---
name: onedrive-volatile-split
description: SteamCMD and the dedicated server payload are rewritten constantly and break under OneDrive/Dropbox/Google-Drive sync. They live in `VolatileRoot` (`%LOCALAPPDATA%\FacilityOverseer`). Durable state (`config`, `worlds`, `logs`, `backups`, `players`) stays in `DataRoot`. This split must NOT leak into the UX. Use this skill whenever touching paths, first-run UX, About panels, or reset behavior.
---

## The rule

- `DataRoot` = `<exe folder>/FacilityOverseerData` when writable, else
  `%LOCALAPPDATA%\FacilityOverseer`.
- `VolatileRoot` = `%LOCALAPPDATA%\FacilityOverseer`. Only `tools/` and
  `servers/` live here when `DataRoot` is a synced location.
- `AppPaths.IsSyncedLocation()` is the pure, tested classifier that
  decides whether to redirect.

## Why
OneDrive/Dropbox/Google‑Drive lock or dehydrate files mid‑write. SteamCMD
fails with "Failed to load steam.dll" or Win32 error 32 mid‑update. The
split prevents that, but it surprises users — deleting `%LOCALAPPDATA%`
*looks* like a clean reset, yet the world tab persists (profile lives in
`DataRoot`) while sandbox settings + world save are destroyed (they live
inside the server install, which is in `VolatileRoot`).

## How to apply
- Keep the split **internal**. Never tell the user about
  `%LOCALAPPDATA%` as if it were their data folder.
- The Welcome popup and About panel show the canonical `DataRoot`, not
  `VolatileRoot`.
- "Open Data Folder" opens `DataRoot`.
- "Reset Everything" clears **both** roots in one gesture.
- New volatile artifacts (e.g., schema caches that get rewritten on every
  launch) can opt into `VolatileRoot`; durable artifacts stay in
  `DataRoot`.

## Detection signal
Any user‑visible string that mentions `%LOCALAPPDATA%` or
`AppData\Local` is almost always wrong. Bind to `IAppPaths.DataRoot`
instead.
