---
name: health-driven-status
description: Status indicators bind to `ServerHealth` (Stopped/Starting/Online/Blocked/Crashed), not `IsRunningState`. There is ONE dot per world (the world tab chip). Use this skill whenever wiring a status visual, a dot, an LED, or a color that signals server state.
---

## The rule

Colors:
- **grey** = Stopped
- **yellow** = Starting
- **green** = Online
- **red** = Blocked / Crashed

One dot per world. The world tab chip is the canonical visual.

## Why
Today three running dots fan out across the top bar, the world tab strip,
and the Logs & Status header. They bind `IsRunningState` through
`RunningToBrushConverter` (bool → green/grey). A briefly‑running corrupt
world shows **green** while `StatusText` says **"Blocked"** — the most
misleading thing in the current UI.

`ServerHealthTracker` already produces the correct state. The bug is in
the binding.

## How to apply
- Add a `ServerHealth → Brush` converter; cover every enum value.
- Bind dots to `ServerHealth`, not `IsRunningState`.
- De‑duplicate: keep the world tab chip; remove the other two dots.
- Test: `HealthBrushConverterTests` — every enum value maps to a defined
  brush; no leak from `IsRunningState`.

## Detection signal
- Any new binding to `IsRunningState` for a *color* is wrong. Process
  presence ≠ health.
- More than one dot per world for the same world's state is a smell.
