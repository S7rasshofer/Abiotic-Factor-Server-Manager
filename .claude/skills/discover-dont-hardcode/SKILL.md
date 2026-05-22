---
name: discover-dont-hardcode
description: Do not hardcode the facility — discover it. Sandbox settings render from a metadata catalog + type inference, not from hand-built forms. Unknown settings are preserved on the Advanced tab. Executables, paths, and extra launch args are discovered, not assumed. Use this skill whenever adding new sandbox UI, new game settings, or new launch behavior.
---

## The rule (project motto)

> *Do not hardcode the facility. Discover it.*

- `ISettingMetadataCatalog` (+ an optional `<DataRoot>/config/setting-metadata.json`
  override) provides labels, categories, help, control hints.
- `SettingTypeInference` infers a usable control from a value when no
  metadata exists.
- `SandboxSettingsDocument` parses the INI loss‑lessly. Unknown keys are
  preserved on the **Advanced** tab as raw editable text.
- `LaunchArgumentBuilder` appends `AdditionalLaunchArguments` verbatim
  (forward‑compat).
- Executable discovery (`IServerExecutableLocator`) finds
  `AbioticFactorServer*-Win64-Shipping.exe` — never assumes a path.

## Why
A game update that adds settings must require **no app change**. New
settings appear, are editable, and are preserved. The same property
applies to launch args: an unknown flag the user types is forwarded, not
dropped.

## How to apply
- New sandbox keys: do nothing app‑side. They appear via inference. Add
  metadata only if you want a better label/category.
- Never hand‑build a sandbox form. Always go through the schema engine.
- The **Advanced** tab is the safety net for keys that escape
  categorization. **Hide when empty**, do not delete — see Phase 1 §2.5.

## Detection signal
- A new `<TextBox>` in `MainWindow.xaml` bound to a specific sandbox key
  by name is a regression — the schema engine should render it.
- A new `case "SomeSetting"` in a parser is a smell. Metadata + inference
  should cover it.
