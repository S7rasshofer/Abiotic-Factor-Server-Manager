---
name: world-identity-model
description: World config (sandbox settings, admins, bans, roster) is durable and lives under `<DataRoot>/worlds/<id>/`. The dedicated server install is disposable runtime. Never resolve a world path inside the server install. Use this skill on any task that touches per-world paths, launch arguments, migration, or reset behavior.
---

## The rule

`World` = permanent identity. `Server Install` = replaceable runtime.

Canonical layout:

```
<DataRoot>/
  worlds/<worldId>/
    config/
      SandboxSettings.ini
      Admin.ini
      metadata.json
    saves/
      backups/
      exports/
    roster/
      roster.json
    runtime/
      cache/
      temp/
  servers/
    abiotic-factor-dedicated/   # disposable
```

## Why
Today the per‑world sandbox/admin files live **inside the server install
path** (`%LOCALAPPDATA%\…\servers\…`). A SteamCMD validate, repair, or
reinstall — or a wipe of `%LOCALAPPDATA%` — destroys them. Users
reasonably expect their world's tuning, admins, and bans to outlive a
server reinstall. The current behavior creates a "world exists, but its
soul evaporated" experience.

## How to apply
- Never store a per‑world ini path *inside* the server install.
- `ServerInstance` should hold only the world id; resolve paths through
  `IAppPaths` at launch time.
- Pass `-SandboxIniPath` as an **absolute path** under
  `<DataRoot>/worlds/<id>/config/`.
- Backups include world `config/`, `saves/`, and `roster/`; they do not
  include the server install.
- "Reset Everything Managed By Facility Overseer" clears both
  `<DataRoot>` and the volatile server tree together — but only inside
  the active managed roots, never outside.

## Detection signal
Any new code that builds a path with `instance.InstallPath + "\\…\\Config"`
for per‑world state is a regression. Path resolution belongs in
`IAppPaths`.
