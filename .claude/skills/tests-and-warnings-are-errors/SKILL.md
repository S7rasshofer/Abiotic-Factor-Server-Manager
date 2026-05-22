---
name: tests-and-warnings-are-errors
description: `Directory.Build.props` sets `TreatWarningsAsErrors=true`. Baseline is 252 xUnit tests passing. Every new Core behavior gets a test. Do NOT lower the warning level, suppress warnings without justification, disable tests, or merge with red CI. Use this skill on any change to behavior, even small ones.
---

## The rule

- `TreatWarningsAsErrors=true` is global. New warnings break the build —
  they are not "to fix later".
- Baseline: **252 tests passing, 0 warnings.**
- Every new Core behavior gets a unit test. Pure Core + xUnit means there
  is no excuse to skip it.
- Infrastructure tests are allowed and welcome, but the Core suite is
  the spine.

## Why
- The 252‑test net is what makes the architecture refactorable. Lose it
  and every change becomes risky.
- Warnings turn into bugs the moment someone has to interpret them. The
  project's policy is to fix the cause, not the message.

## How to apply
- New feature → new tests, written first when feasible.
- Compiler warning → fix the cause. Suppress only with a comment that
  names the specific rule + reason.
- Failing test → fix the cause. Do **not** mark it `Skip` to ship.
- Disabled test → treat as a red flag in review. Justify or re‑enable.

## Detection signal
- New `#pragma warning disable` without a comment.
- New `[Fact(Skip = "…")]`.
- New `<NoWarn>` entries in csproj.
- A PR that drops the test count.

All four are smells; investigate before accepting.
