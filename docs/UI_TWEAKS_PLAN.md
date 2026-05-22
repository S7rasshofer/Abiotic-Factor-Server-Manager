# Facility Overseer — UI Tweaks & Cleanup Plan

> A focused, **UI-only** plan captured from a live review (2026‑05‑21).
> Sibling to [`MASTER_PLAN.md`](MASTER_PLAN.md); architecture reference is
> [`CURRENT_BUILD.md`](CURRENT_BUILD.md). This work is the **immediate
> priority** — it runs ahead of Master Plan Phase 3 (RCON).
>
> Baseline: build green, 0 warnings, `TreatWarningsAsErrors=true`.
>
> Update protocol: when a task finishes, set its **State** to `Done` in the
> overview table and add a dated line to the Progress log. Never reorder items.

---

## 1. Read this first — why "it's not in the exe"

Several items below the user marked with `!` were **already implemented in
this worktree** but never reached the published `FacilityOverseer.exe`.

Active development lives in the git worktree on branch
`claude/condescending-bose-283816`. That branch was never merged back to
`main` and the exe the user runs was built from the older tree. So
"3 green lights" and "whole-number sliders" *look* unfixed even though the
code is done here.

**Action X2 (Publishing) is therefore a hard prerequisite for the user to
see ANY of this work.** Either republish the exe from the worktree
(`dotnet publish … -c Release -o publish`) or merge the worktree to `main`
first. Nothing in this plan is "shippable" until that happens.

---

## 2. Decisions locked (from the 2026‑05‑21 review)

1. **Save model.** Three distinct layers, treated differently:
   - **World profile** (`instances.json`: name, ports, passwords, max
     players, LAN flags) — still saved by the title‑card **Save** button.
   - **Sandbox settings** (`SandboxSettings.ini`: World/Player/Enemy game
     knobs) — **saved automatically when the server is Started**. The
     per‑tab **"Save Sandbox"** button is **removed**.
   - **Game save / world data** (the real Abiotic Factor saved game) —
     backups stay manual + automatic‑before‑risky‑actions. Scheduled
     auto‑backup is **deferred** (see §9).
2. **Revert.** A **Revert** button is added **only on the tabs where it is
   relevant** — the sandbox settings tabs (World/Player/Enemy). It discards
   edits made since the last save and reloads the `.ini`. No Revert on the
   title card or other tabs (clutter reduction is an explicit goal).
3. **GATE badge.** Keep as‑is. A proper logo may replace it later — no work
   this pass.
4. **Difficulty.** The Create‑World popup's difficulty picker sets the real
   game difficulty: it writes the **`GameDifficulty`** key (World sandbox
   category — enum `1/2/3` → Normal / Hard / Apocalyptic, default `1`).
5. **Scope.** This pass is **UI only**. The auto‑save‑backup feature and its
   frequency control are out of scope for now (§9).

---

## 3. Overview

State legend: **New** = to build · **Done** = built + verified · **Worktree** = already done in this
worktree, needs publish (X2) · **Partial** = mostly done, small follow‑up ·
**Decided** = resolved, little/no code.

| # | Item | Group | State | Brief |
|---|---|---|---|---|
| A1 | GATE badge | Title card | Done | Keep as‑is; re‑logo later. |
| A2 | Collapse the Create/Clone/Delete/Save buttons into one group | Title card | Done | A tidy, collapsible button cluster. |
| A3 | Auto‑hide the button group once a world exists | Title card | Done | Slides away after the first world. |
| A4 | Click "FACILITY OVERSEER" to slide the group back out | Title card | Done | Title acts as the toggle. |
| A5 | Keep title‑card **Save**; remove per‑tab **Save Sandbox** | Title card | New | Save = world profile; sandbox saves on Start. |
| A6 | First‑run clean slate — no auto‑world, no tabs | Title card | Done | Empty state + a Create‑World call to action. |
| A7 | Create World popup — name + difficulty | Title card | Done | Modal dialog; difficulty → `GameDifficulty`. |
| B1 | Move the **Network** vertical tab to the bottom | Networking | New | Reorder; fix tab‑index constants. |
| B2 | Remove the **Advanced** vertical tab | Networking | Worktree | Already a dynamic category under Settings. |
| B3 | Hover tooltips on every button | Networking + global | New | Context on hover, not on the page. |
| B4 | Merge "Check Setup" + "Copy Diagnostics" | Networking | New | They run the identical inspection. |
| B5 | Network results → on‑screen side cards | Networking | New | Replace clipboard dumps with a results panel. |
| B6 | Clearer, simpler network button labels | Networking | New | Plain language. |
| B7 | Simplify the firewall button label `!` | Networking | New | "Create / Repair Windows Firewall Rules" is too long. |
| B8 | Firewall rules keyed to AF port + exe, not per‑world | Networking | New | One rule set for Abiotic Factor, not per world. |
| C1 | Order diagnostic checks Failed → Priority | Diagnostics | New | Failures float to the top. |
| C2 | Titles‑only checks, click to expand | Diagnostics | Worktree | Already the per‑check Expander design — verify. |
| C3 | Tag icons next to check titles | Diagnostics | New | E.g. "run server", "setup firewall" glyphs. |
| D1 | Reset + Revert together; remove "Save Sandbox" | Settings tabs | New | Two buttons per settings tab, no Save. |
| D2 | Switches instead of checkboxes for booleans | Settings tabs | Done | Restore the toggle‑switch control. |
| D3 | Whole‑number sliders, no decimals `!` | Settings tabs | Partial | Handled in worktree; audit metadata Steps. |
| D4 | Click the line item (not "?") to open details | Settings tabs | New | Whole row is the hit target. |
| D5 | Remove "Recent Activity" / "Play Sessions" cards | Settings/Players | Worktree | Already gone in this worktree. |
| D6 | Double‑click a player → detail card | Settings/Players | Worktree | Implemented as a Player Detail tab — verify. |
| D7 | Player avatar / bust image (bonus) | Settings/Players | New (stretch) | Steam avatar in the player detail. |
| E1 | Declutter the Logs & Status tab | Logs & Status | New | Umbrella for E2–E5. |
| E2 | One status light, not three `!` | Logs & Status | Worktree | De‑duplicated to the tab chip dot. |
| E3 | Icon buttons (folder / settings cog) | Logs & Status | New | Replace long text buttons with glyphs. |
| E4 | Sub‑tabs on the same line as the buttons | Logs & Status | New | Reclaim vertical space. |
| E5 | Remove on‑page context text → tooltips | Logs & Status | New | Hover context, not page clutter. |
| F1 | Plain-language context on each data-folder choice | First-launch | Done | Describe what each option means. |
| F2 | Rename "Portable" to clearer wording | First-launch | Done | Now "In the Facility Overseer folder". |
| F3 | "User folder" choice names AppData, drops jargon | First-launch | Done | No more "recommended for managed installs". |
| F4 | Hide the Custom path box until Custom is chosen | First-launch | Done | Custom box + its description hidden unless selected. |
| F5 | First-launch note mentions the folder-opening buttons | First-launch | Done | Reassures where data can be opened later. |
| F6 | Server prepared before worlds | First-launch | Done | Install flow decoupled from worlds; empty state leads with server prep. |
| A8 | Remove the "Reset Data" nuke button from the title card | Title card | Done | One-click wipe of all worlds — too dangerous to surface. |
| A9 | Hide the public IP until right-click → Show | Title card | Done | Masked by default; revealed via a context menu. |
| X1 | Save `SandboxSettings.ini` on server Start | Behavior | New | Small behavior change; enables Revert. |
| X2 | Republish the exe from the worktree | Process | New | Prerequisite for the user to see any of this. |
| X3 | Desktop shortcut for users | Distribution | New | Shortcut for the published exe (and VS run if feasible). |

### Progress log

- **2026-05-21** — Sandbox settings (Group D). Booleans now render as **toggle
  switches** (D2) — a new `ToggleSwitch` theme style replaces the checkbox in
  the sandbox `ToggleTpl`. The category sub-tabs (World / Enemy / Player / …)
  are now a **vertical** strip. Confirmed already in place from earlier work:
  the parser reads the `; === WORLD ===` banner comments into dynamic section
  tabs (`CategoryHint` wins over metadata), every numeric setting renders as a
  slider, and `setting-metadata.json` carries researched min/max plus integer
  `step` for whole-number settings (Base Inventory Size, Bonus Perk Points,
  Structural Support Limit). Server-tab booleans still to convert. Build
  green, 436 tests passing.

- **2026-05-21** — Top-bar safety + privacy. Removed the **"Reset Data…"**
  button from the title card (A8) — a one-click wipe of every world, backup,
  and the server install is too dangerous to sit on the main bar. The
  `IResetManagedDataService` machinery stays in code but is no longer
  reachable from the UI (pending a decision to delete or relocate it). The
  **public IP** is now masked by default and revealed only via a right-click
  "Show public IP" menu (A9) — it is moderately sensitive. Build green,
  436 tests passing.

- **2026-05-21** — Server before world (F6). The dedicated-server install is
  now world-independent: `CanPrepareServer` and `InstallOrUpdateServer` no
  longer require a selected world, so the server can be downloaded with zero
  worlds present. `MaybePromptInstallAsync` evaluates the shared managed
  install directly. New `IsServerPrepared` flag + `RefreshServerInstallState`;
  the first-run empty state is now two-phase — "First, prepare the server"
  until the server is installed, then "Create your first world". Reinforces
  the master-plan §2 separation — worlds are stored apart from the server
  download and survive a server repair or reinstall. Build green, 436 tests.

- **2026-05-21** — Launch flow + data-folder dialog. Fixed the app quitting
  after the folder picker: the picker closed before MainWindow existed and
  `OnLastWindowClose` shutdown mode quit the app — now held with
  `OnExplicitShutdown` until MainWindow is shown. Dialog trimmed to two
  choices (AppData option removed), per-choice context cut down, full paths
  moved into hover tooltips, flash-drive reminder kept in the footer.
  **Open:** a `Program Files\Facility Overseer` default is not viable as-is —
  Program Files is read-only for a non-elevated app and Facility Overseer
  writes gigabytes (SteamCMD, the server install, saves, backups). Option 1
  stays as the application folder pending a decision on running elevated.

- **2026-05-21** — Fixed unreadable tooltips: the dark theme had no `ToolTip`
  style, so every hover popup rendered as a blank white box (light text on
  the system's default light background). Added an app-wide dark `ToolTip`
  style (dark surface, light text, border, wraps at 340px). All existing and
  Group A tooltips are now readable — this also unblocks **B3** (hover
  tooltips on every button).

- **2026-05-21** — First-launch data-folder dialog (`DataRootPickerWindow`)
  reworked per review: plain-language context on each choice (**F1**),
  "Portable" renamed to "In the Facility Overseer folder" (**F2**), the
  AppData choice now names AppData and drops the "managed installs" jargon
  (**F3**), the Custom path box is hidden until Custom is selected and its
  description removed (**F4**), and the footer note now points to the in-app
  folder-opening buttons (**F5**). Also fixed a latent bug — the per-choice
  paths never displayed (`ElementName=Self` matched no element); they now
  bind via the DataContext. **X3** (desktop shortcut) is logged for later.

- **2026-05-21** — Group A title-card + first-run batch implemented and
  verified (build green, 0 warnings, 436 tests passing): **A1** (GATE kept),
  **A2/A3/A4** (collapsible world-action cluster that slides away once a world
  exists and reopens from the title), **A6** (first-run clean slate — the
  auto-created "My World" is gone, replaced by an empty-state prompt), **A7**
  (Create World dialog — name + difficulty, writing `GameDifficulty`). **A5**
  is intentionally deferred to the save-model batch (A5 + X1 + D1).

---

## 4. Group A — Facility Overseer title card

Current top bar (`MainWindow.xaml` lines ~67–127): `GATE` badge,
**FACILITY OVERSEER** + `HeaderInfoText`, then **Prepare / Update Server**,
**Create World**, **Clone**, **Delete**, **Reset Data…**, **Save**, plus the
LAN/Public IP strip. Every button is always visible.

### A1 — GATE badge — keep
- A purely decorative `TextBlock Text="GATE"` in an accent border
  (`MainWindow.xaml:74‑79`); the theme is literally "Clean GATE facility
  console". No function. **Decision: keep.** A logo swap is a later pass.

### A2 — Collapse Create / Clone / Delete / Save into one group
- **Observed:** four world‑management buttons sit loose in the top bar.
- **Desired:** group them into one tidy, collapsible cluster (a bordered
  `StackPanel`, or an overflow/"⋯" menu).
- **Approach:** wrap the four buttons in a named container; keep
  **Prepare / Update Server** and **Reset Data…** outside the group (they
  are install/maintenance, not world CRUD). The IP strip is untouched.

### A3 — Auto‑hide the group after the first world exists
- **Desired:** once at least one world exists, the A2 group slides away so
  the title card is calm during normal use.
- **Approach:** bind the group's `Visibility`/width to a new
  `MainViewModel` flag (e.g. `IsWorldButtonGroupExpanded`). Default the flag
  from `Worlds.Count`: collapsed when worlds exist, expanded when none.
  A `ThicknessAnimation`/`DoubleAnimation` gives the "slide".

### A4 — Click "FACILITY OVERSEER" to slide the group back out
- **Desired:** clicking the title text toggles the A2 group back into view.
- **Approach:** a `MouseLeftButtonUp` (or an invisible toggle button over
  the title) flips `IsWorldButtonGroupExpanded`. Add a small chevron/`ToolTip`
  ("Show world actions") so the affordance is discoverable.

### A5 — Keep title‑card Save; remove per‑tab Save Sandbox
- **Save** (title card) continues to persist the **world profile**
  (`instances.json`). It joins the A2 collapsible group.
- The world‑level **Save Sandbox** toolbar (`SandboxToolbar` DataTemplate,
  `MainWindow.xaml:41‑57`) is **removed** — the sandbox `.ini` now saves on
  Start (X1). See D1 for what replaces it on the settings tabs.

### A6 — First‑run clean slate
- **Observed:** `MainViewModel.InitializeAsync` (`MainViewModel.cs:384‑391`)
  auto‑creates **"My World"** when `Worlds.Count == 0`, so the user never
  sees an empty app.
- **Desired:** on first run show **only** the title card + Prepare/Update —
  no horizontal world tabs at all, a true clean slate.
- **Approach:** delete the `AddWorldAsync("My World")` bootstrap. When
  `Worlds.Count == 0`, hide the world `TabControl` and show an empty‑state
  panel: a short line of copy + a prominent **Create World** button (which
  runs A7). Keep the staged‑config safety (no partial save folder is
  created until the user actually makes a world).

### A7 — Create World popup (name + difficulty)
- **Observed:** `CreateWorld()` (`MainViewModel.cs:638‑640`) silently adds
  `"World N"` with default settings — no prompt.
- **Desired:** a modal popup on creation: **world name** + **difficulty**
  (Normal / Hard / Apocalyptic).
- **Approach:** new `CreateWorldDialog` window, modelled on the existing
  `Views/DataRootPickerWindow` (precedent for a modal dialog). Returns
  `(name, difficulty)`. `AddWorldAsync` takes the name; after the sandbox is
  staged, write `GameDifficulty` = `1|2|3` into the World category of the
  new world's `SandboxSettings.ini`.
- **Server‑facing:** confirm the `GameDifficulty` key, values, and the
  `HardcoreMode` interaction with the **`abiotic-factor-server`** subagent
  before wiring (see §7).
- **Acceptance:** creating a world prompts; the chosen difficulty is
  visible afterward on the World settings tab as `Game Difficulty`.

---

## 5. Group B — Networking tab

### B1 — Move Network to the bottom of the vertical tab list
- **Observed:** vertical tab order is Server, **Network**, Settings, Admin,
  Backups, Logs & Status (`MainWindow.xaml`).
- **Desired:** Network last.
- **Approach:** move the `<TabItem Header="Network">` block to the end.
  **Audit hardcoded tab indices** when reordering — `SelectedVerticalTabIndex`
  bindings and any `…TabIndex` constants (`LogsStatusTabIndex`, etc.).

### B2 — Remove the Advanced tab — already done
- The standalone **Advanced** vertical tab is already gone; unknown/
  uncategorised keys now appear as a dynamic **"Advanced" category** under
  the **Settings** tab (`MainWindow.xaml:1421‑1424`). Loss‑less catch‑all
  preserved. No work — documented for the user.

### B3 — Hover tooltips on every button
- **Observed:** most top‑bar and Network buttons have no `ToolTip`.
- **Desired:** every actionable button explains itself on hover.
- **Approach:** add concise `ToolTip` text to each `Button` (top bar,
  per‑world toolbar, Network, Backups, Logs & Status). This is the
  mechanism that lets E5 / B‑group remove on‑page explanatory text.

### B4 — Merge "Check Setup" and "Copy Diagnostics"
- **Observed:** `CheckNetworkSetup` (`MainViewModel.cs:816`) and
  `CopyNetworkDiagnostics` (`:952`) run the **identical**
  `_networkSetup.InspectAsync(...)` + `ApplyNetworkSetupStatus(...)`. Copy
  Diagnostics merely *also* dumps text to the clipboard and pops a dialog —
  which is why it feels slow and redundant and "re‑feeds the long list".
- **Desired:** **one** action. Keep a single primary button (e.g.
  **"Check Network"**); drop the separate Copy Diagnostics button.
- **Approach:** delete `CopyNetworkDiagnosticsCommand` and its button; if a
  copy‑to‑clipboard escape hatch is still wanted, make it a small "Copy"
  affordance **inside** the results card (B5), not a top‑level button.

### B5 — Network results as on‑screen side cards
- **Observed:** Router Checklist and Diagnostics are delivered via
  `Clipboard.SetText` + a `MessageBox`.
- **Desired:** results open as **cards on the right side of the screen** —
  readable in‑app, no clipboard round‑trip.
- **Approach:** a right‑hand results panel/column on the Network tab (mirror
  the SettingDetails side panel pattern in `SandboxCategoryPanel.xaml:176‑228`).
  Render the router checklist + diagnostic summary as cards; each card may
  carry its own small "Copy" button.

### B6 — Clearer, simpler network button labels
- Plain‑language labels, e.g. **Check Network**, **Firewall** (B7),
  **Router Help**. Pair every one with a B3 tooltip carrying the detail.

### B7 — Simplify the firewall button label `!`
- **Observed:** the label is `Create / Repair Windows Firewall Rules`
  (`MainWindow.xaml:352`) — far too long.
- **Desired:** short label, e.g. **"Firewall Rules"** or **"Set Up
  Firewall"**, with the full explanation in a B3 tooltip (the confirm
  dialog at `MainViewModel.cs:852‑858` already explains the action).

### B8 — Firewall rules keyed to Abiotic Factor, not per‑world
- **Observed:** `FirewallScriptBuilder` uses **per‑world rule identity** —
  every world creates its own rules even when ports are shared.
- **Desired:** rules describe **Abiotic Factor** itself — keyed to the UDP
  port value(s) + the dedicated‑server executable — so one rule set covers
  the game regardless of which world/profile is selected.
- **Approach:** change rule identity/naming to `(protocol, port, direction)`
  + the AF server exe (e.g. `Abiotic Factor Dedicated Server (UDP 7777)`).
  Worlds sharing a port share one rule; idempotency is by port, not world id.
- **Server‑facing:** validate port semantics and default ports
  (game `7777` / query `27015`) with the **`abiotic-factor-server`**
  subagent (§7). Core change → needs a `FirewallScriptBuilder` test update.

---

## 6. Group C — Diagnostic checks (Network tab)

Current: `NetworkChecks` grouped by `Category` via a `CollectionViewSource`,
each category an expanded `Expander`, each check a collapsed `Expander`
(status + label in the header, detail on expand) — `MainWindow.xaml:473‑567`.

### C1 — Order Failed → Priority
- **Observed:** checks render in category order; a failure can sit below
  passing checks.
- **Desired:** **failed items at the top**, then ordered by priority.
- **Approach:** sort before display by `(Status: Failed → Warning → Pass,
  then Priority)`. Add a `Priority` to the check model if absent
  (`Core/Diagnostics/NetworkSetupModels.cs`). A failed‑first sort can
  replace — or sort within — the category grouping; recommend a single
  failed‑first flat list so problems are unmissable.

### C2 — Titles‑only, click to expand — verify
- The per‑check row is already a collapsed `Expander` (title + status
  visible, detail/steps on expand). This matches the request — **verify it
  reads cleanly after C1** and tighten padding if needed.

### C3 — Tag icons next to check titles
- **Desired:** small glyphs for frequent action tags — e.g. "run server",
  "set up firewall" — so a check's gist is scannable without expanding.
- **Approach:** add a `Tag`/`ActionKind` enum to the check model; map each
  to a small icon/emoji in a converter; render it before the title in the
  `Expander.Header`. Keep the set tiny (server, firewall, router, port).

---

## 7. Group D — World / Player / Enemy settings tabs

These are dynamic sub‑tabs under the **Settings** tab, rendered by
`SandboxCategoryPanel.xaml` from the sandbox schema.

### D1 — Reset + Revert together; remove "Save Sandbox"
- **Observed:** **Save Sandbox** is a world‑level toolbar
  (`SandboxToolbar`); **Reset Tab to Defaults** is a per‑category button
  inside `SandboxCategoryPanel.xaml:158‑162`.
- **Desired:** since the sandbox `.ini` now saves on Start (X1), drop
  **Save Sandbox** entirely. Each settings tab keeps two buttons, side by
  side: **Reset to Defaults** and **Revert**.
- **Approach:** remove the `SandboxToolbar` DataTemplate + its
  `ContentControl` host. In `SandboxCategoryPanel`, place **Reset** and a
  new **Revert** together in the row‑0 toolbar. Revert = reload the
  category's settings from the last‑saved `.ini`, discarding in‑memory
  edits. Keep the existing "● unsaved changes" dirty indicator beside them.

### D2 — Switches instead of checkboxes
- **Observed:** boolean settings render as a plain `<CheckBox>`
  (`SandboxCategoryPanel.xaml` `ToggleTpl`, line ~34); the standalone
  Server‑tab booleans (LAN Only, Use Local IPs) are checkboxes too.
- **Desired:** restore **toggle switches**.
- **Approach:** add a `ToggleSwitch` `Style` (a templated `ToggleButton`
  styled as a sliding switch) to `Themes/Overseer.xaml`; apply it in
  `ToggleTpl` and to the Server‑tab booleans. WPF has no native
  ToggleSwitch — it is a `ControlTemplate` on `ToggleButton`.

### D3 — Whole‑number sliders `!` — verify + audit
- **Already handled in this worktree:** `SettingViewModel` exposes
  `IsInteger` / `Step`, the slider snaps (`IsSnapToTickEnabled`,
  `TickFrequency={Binding Step}`), and `DoubleValue` rounds integer
  settings — so a slider cannot produce `12.7`.
- **Follow‑up:** audit `Core/Schema/setting-metadata.json` — any
  integer‑valued setting with a **fractional or missing `step`** should
  declare `"step": 1` so it snaps to whole numbers. Mostly a data check.

### D4 — Click the line item to open details
- **Observed:** every setting row has a `?` button that runs `SelectCommand`
  to populate the SettingDetails side panel.
- **Desired:** clicking **anywhere on the row** opens its details; the `?`
  is unnecessary.
- **Approach:** put an `InputBinding` / clickable surface on the
  `SettingRow` `Border` that runs `SelectCommand` with the row's VM. Remove
  the `?` button from all five templates (Toggle/Slider/Number/Dropdown/
  Text) to cut clutter; guard against the click stealing focus/clicks from
  the actual editor control (slider/textbox/combobox).

### D5 — Recent Activity / Play Sessions removed — already done
- Those cards are no longer in the UI; the Players sub‑tab is roster‑first
  and the per‑player history lives on the Player Detail tab. No work —
  documented.

### D6 — Double‑click a player → detail — already done
- `RosterList` has a `LeftDoubleClick` `MouseBinding` →
  `ShowPlayerDetailCommand`, opening a **Player Detail** tab with that
  player's activity + chat (`MainWindow.xaml:1257‑1416`). Verify it works.
- **Optional:** the request said "card **popup**". Current surface is a
  tab. Recommend keeping the tab (no extra window management); restyle its
  content as a clean "employee record" card. Treat popup‑vs‑tab as a minor
  open decision (§10).

### D7 — Player avatar / bust image (bonus / stretch)
- **Desired:** show a player's avatar so the detail reads like an employee
  file.
- **Approach:** Steam avatars are reachable from a SteamID64 with **no API
  key** via the public profile XML
  (`https://steamcommunity.com/profiles/<id>?xml=1` → `<avatarFull>`). Add
  an Infrastructure `ISteamAvatarProbe` (Core stays pure), cache per
  session, render in the Player Detail header. Clearly a **stretch** item —
  do last, behind the core UI work.

---

## 8. Group E — Logs & Status tab

The user's headline: **"this tab is VERY CLUTTERY."** E1 is the umbrella;
E2–E5 are the concrete cuts.

### E2 — One status light, not three `!` — already done
- Master Plan §2.6 already de‑duplicated the three running dots to a single
  health‑driven dot on the world tab chip; the per‑world toolbar and the
  Logs & Status header no longer carry a dot (see the comments at
  `MainWindow.xaml:217‑218` and `:936‑937`). No work — documented. (This is
  a prime example of the §1 publish gap.)

### E3 — Icon buttons
- **Observed:** the header row has long text buttons — **Open World
  Folder**, **Create Fresh World**, **Validate Config**
  (`MainWindow.xaml:947‑955`).
- **Desired:** compact icon buttons — a folder glyph for World Folder, a
  check/cog for settings/validate — each with a B3 tooltip for the words.

### E4 — Sub‑tabs on the buttons' line
- **Observed:** the **Log / Players / Player Detail** `TabControl` sits on
  its own row, below the header `DockPanel`.
- **Desired:** the sub‑tab strip shares the row with the (now compact, E3)
  buttons — they do not need a full row each.
- **Approach:** host the sub‑tab headers and the icon buttons in one
  `DockPanel`/`Grid` row; reclaim the vertical space.

### E5 — Remove on‑page context → tooltips
- **Observed:** explanatory paragraphs sit on the page — the `HealthDetail`
  line, the roster help text ("Roster is built from the live Abiotic
  Factor server log…"), the Ban help sentence, etc.
- **Desired:** **no context text on the page**; move it to hover tooltips
  on the relevant control (depends on B3).
- **Keep:** the genuinely actionable Phase‑2 guidance panels (recovery
  flow, recommended actions, world integrity, diagnostic cards) — those are
  *actions*, not prose. Trim only static explanatory copy.

---

## 9. Cross‑cutting & process items

### X1 — Save `SandboxSettings.ini` on server Start
- **Behavior:** pressing **Start Server** flushes the in‑memory sandbox
  edits to the world's `SandboxSettings.ini` before launch. This is what
  makes **Save Sandbox** (A5/D1) unnecessary and gives **Revert** (D1) a
  defined "last saved" state.
- **Open question:** to avoid losing edits made and *not* followed by a
  Start, also persist the `.ini` on **app close** and on **world switch**
  (§10). Small behavior change — explicitly requested by the user; not pure
  UI, but in scope.

### X2 — Republish the exe from the worktree
- See §1. Republish (`dotnet publish src/AbioticServerManager.App … -c
  Release -o publish`) or merge the worktree to `main`. **Without this the
  user sees none of this plan, including the already‑done `!` items.**

---

## 10. Server‑facing validation (required)

Per project policy, changes touching the dedicated server are validated
against DFJacob's docs with the **`abiotic-factor-server`** subagent
(read‑only/advisory; it reports `file:line` drift, fixes are applied by us):

- **A7 difficulty** — confirm `GameDifficulty` key/values and the
  `HardcoreMode` "forces Apocalyptic" interaction.
- **B8 firewall** — confirm default ports (game `7777`, query `27015`) and
  that keying rules to the AF executable + ports is correct.

Point the subagent at the worktree path.

---

## 11. Suggested sequence

1. **X2 + verify Worktree items** — publish, then confirm B2, C2, D3, D5,
   D6, E2 actually look right in the running exe. Cheap, high‑trust.
2. **Group A** (title card) — highest visibility; A6/A7 change first‑run.
3. **Group D** (settings tabs) — switches, row‑click, Reset/Revert.
4. **Groups B + C** (networking + diagnostics) — needs §10 validation.
5. **Group E** (Logs & Status declutter) — depends on B3 tooltips.
6. **D7** (avatar) — stretch, last.

---

## 12. Test & verification

`TreatWarningsAsErrors=true`; keep the build green and add tests for every
new Core behavior (house rule `tests-and-warnings-are-errors`).

| Item | Verification |
|---|---|
| A6 | First run with no `instances.json` → no tabs, empty state shown; no `worlds/` save folder created. |
| A7 | Create‑World dialog returns name + difficulty; `GameDifficulty` written to the new world's `.ini`. |
| B4 | Only one network inspection action remains; no duplicate `InspectAsync` path. |
| B8 | `FirewallScriptBuilderTests` — rule identity is `(protocol, port, exe)`; two worlds sharing a port yield one rule set; still idempotent. |
| C1 | Given mixed check statuses, failed checks sort first, then by priority. |
| D1 | No "Save Sandbox" button anywhere; Reset + Revert present on each settings tab; Revert restores last‑saved values. |
| D3 | `setting-metadata.json` audit — every integer setting has an integer `step`. |
| X1 | Starting the server writes the current sandbox edits to `SandboxSettings.ini`. |

---

## 13. Out of scope (this pass)

- **Scheduled auto‑backup + "Auto‑Save frequency" control.** Deferred by
  the user ("if it is not initialized yet … stick to the UI"). When picked
  up later: it is *timestamped backup snapshots on a timer* (an app
  feature), not the game's own live autosave; natural home is the **Backups
  tab**. This also reverses `MASTER_PLAN.md` §10's current "out of scope"
  line for scheduled backups — note that when scheduling the work.
- World‑profile auto‑save (kept on the explicit **Save** button for now).
- Re‑logo of the GATE badge.
- Anything in Master Plan Phase 3 (RCON) — this UI plan runs first.

---

## 14. Open questions

- **X1** — besides server Start, when else should the sandbox `.ini` be
  flushed so un‑started edits are not lost? (App close, world switch.)
- **D6** — keep the Player Detail as a **tab**, or convert to a floating
  **popup card**? (Recommend tab + card‑styled content.)
- **C1** — keep the category grouping on the diagnostic checks, or flatten
  to a single failed‑first list? (Recommend flatten for clarity.)
- **A2** — collapsible inline cluster vs an overflow "⋯" menu for the
  world‑action buttons? (Recommend an inline cluster with a slide.)
