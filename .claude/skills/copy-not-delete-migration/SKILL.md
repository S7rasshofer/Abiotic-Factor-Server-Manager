---
name: copy-not-delete-migration
description: Migration and reset operations COPY data into the new layout; they never auto-move or auto-delete the source. Old roots stay readable until the user approves cleanup. Quarantine, do not delete. Use this skill on any change that moves files between locations, runs first-launch migration, or implements reset behavior.
---

## The rule

- **Migration copies, never moves.** `ILegacyMigrationService` already
  follows this; new migrations (e.g., the world‑identity move in Phase 1
  §2.1) must too.
- **Quarantine over delete.** Corrupt world folders move to a
  timestamped quarantine subfolder; they are not removed. Same for any
  destructive‑looking action.
- **Reset is explicit.** "Reset Everything Managed By Facility Overseer"
  is the only place we clear managed roots, and only inside the active
  managed roots (never outside).
- **Write a report.** Every migration/reset writes
  `<DataRoot>/logs/migration-YYYYMMDD-HHMMSS.log` (or
  `reset-YYYYMMDD-HHMMSS.log`) listing what was touched.

## Why
- Users delete `%LOCALAPPDATA%` thinking it's a reset and lose work.
  We're explicitly the kind of tool that does not pile onto that mistake.
- Worlds, saves, admins, bans, and roster are *user data*. Losing them
  costs trust in a way speed never recovers.

## How to apply
- New migration step? It copies. The user approves cleanup later.
- New "delete" button? Make sure it actually quarantines, or get an
  explicit confirm and write a report.
- Reset paths must be derived from `IAppPaths` and never accept an
  arbitrary outside path.

## Detection signal
- `Directory.Delete(path, recursive: true)` against any user‑data path
  outside a known quarantine/reset code path.
- A migration that calls `File.Move` over `File.Copy`.
- A "reset" that touches paths outside the active managed roots.
