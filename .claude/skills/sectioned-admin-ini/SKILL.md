---
name: sectioned-admin-ini
description: The real Abiotic Factor `Admin.ini` is sectioned (`[Moderators]`, `[BannedPlayers]`) with comments and example placeholders. Always go through `AdminIniBanEditor` (or its successor unified service). Never assume a flat list of SteamIDs. Use this skill whenever editing admins, bans, or anything that writes `Admin.ini`.
---

## The rule

`Admin.ini` shape that must be preserved:

```ini
[Moderators]
Moderator=76561198000000001
; comments and examples kept verbatim

[BannedPlayers]
BannedPlayer=76561198000000002
```

Everything outside the managed `Moderator=` / `BannedPlayer=` lines —
section headers, comments, blank lines, placeholder examples — is
preserved byte‑for‑byte.

## Why
- AF reads the sectioned format. A flat list silently breaks moderators
  or bans.
- Users sometimes hand‑edit comments and examples. We must not stomp
  them.
- `AdminListService` historically assumed a flat file. That's the bug
  Phase 1 §2.2 fixes — do not replicate it in new code.

## How to apply
- Read/write through `AdminIniBanEditor` (Core, pure) for bans.
- The Admin tab editor must move to the same sectioned writer for
  `[Moderators]`.
- Round‑trip property: parse → mutate one line → write → only that one
  line changes; everything else is byte‑identical.
- Tests:
  - `AdminBanUnifiedServiceTests` for the unified service.
  - Round‑trip property test for arbitrary INI bodies.

## Detection signal
Any new code that does `File.WriteAllLines(adminIniPath, ids)` or
`string.Join('\n', ids)` over a flat list is a regression. Reject it.
