---
name: core-pure-discipline
description: Anything new in `AbioticServerManager.Core` must be a pure record/static/algorithm with zero IO. The Infrastructure project is the only place that touches the filesystem, processes, PowerShell, HTTP, or sockets. The App is MVVM glue. Use this skill whenever adding a new behavior or interface to keep the 252-test safety net intact.
---

## The rule

- **Core** = pure domain logic. No `System.IO`, no `System.Net`, no
  `System.Diagnostics.Process`, no PowerShell, no WPF. Just records, static
  builders, parsers, validators, planners.
- **Infrastructure** = the only IO layer. Implements Core interfaces.
- **App** = MVVM. ViewModels orchestrate Core + Infrastructure interfaces.
  XAML binds to ViewModels. No business logic in code‑behind.

## Why
- Every behavior worth testing can be tested without a server, a registry,
  or a network.
- 252 tests pass today *because* of this discipline. Breaking it is how
  green CI starts hiding broken features.
- Refactors stay safe — the test suite is the contract.

## How to apply
When adding a feature:
1. Sketch the **input → output** as a pure function on a Core record.
2. Write the test first (xUnit).
3. Implement the Core piece.
4. Add the Infrastructure adapter only if real IO is needed.
5. Wire the ViewModel last.

If you find yourself reaching for `File.ReadAllText`, `Process.Start`, or
`HttpClient` in Core: stop, define an interface, push the call to
Infrastructure.

## Detection signal
Any new `using System.IO;` / `using System.Net.Http;` /
`using System.Diagnostics;` in a file under `src/AbioticServerManager.Core/`
is almost certainly a smell. Review before merging.
