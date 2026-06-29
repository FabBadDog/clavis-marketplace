---
name: keymap
pluginId: KeyMap
version: 1.0.2
apiVersion: 1.0.0
description: Keybinding resolution service.
dependencies:
  - { name: keymap-contracts, version: 1 }
  - { name: services-contracts, version: 1 }
  - { name: yamldotnet, version: 1 }
language: csharp
assemblyName: KeyMap
rootNamespace: FabioSoft.Nucleus.Plugins.KeyMap
globalUsings:
  - FabioSoft.Nucleus.Contracts
  - FabioSoft.Contracts.Keymap
  - FabioSoft.Contracts.Services
---

# KeyMap

## Purpose

Source of truth for the keyboard-shortcut bindings catalog (the Keymap contract group). Owns the YAML
keymap config (load, seed-on-first-run, persist), broadcasts the full resolved binding set as
`KeymapChanged`, and applies add/change/remove edits. It does not capture keys or resolve gestures at
runtime - the WpfHost holds the broadcast snapshot and resolves synchronously so it can swallow the key
event. This is the impure shell; the gesture/file/binding logic is pure.

## Location

`src/plugins/KeyMap/` - non-UI plugin (no `UseWPF`). Registers no panel kind. Uses YamlDotNet for the
config round-trip.

## Config (`KeyMapConfig`)

- `SummonGesture` (default `"Ctrl+Shift+V"`) - the system-scope chord seeded as the global
  summon/hide toggle hotkey. Validated at activation (`ConfigInvalid` if not a parseable gesture). The
  host reads the live value from the broadcast bindings; this is only the seed default.

## Messages published

- `KeymapChanged` - the full binding set, on load and after every change.
- `GetConfig` - on activation, to load the persisted keymap YAML.
- `SaveConfig` - to persist after seeding the starter set or applying a Set/Remove edit.
- `LogEntry` via `bus.LogInfo`/`LogWarn` (activation notice; warns on unparseable config and on
  same-scope duplicate gestures).

## Messages subscribed

- `ConfigResult` - the `GetConfig` reply: `ConfigFound` loads-and-broadcasts the YAML; `ConfigNotFound`
  seeds the defaults, saves the starter YAML, and broadcasts.
- `ConfigChanged` - reloads-and-broadcasts when this plugin's config is rewritten (also the echo from its
  own `SaveConfig`, keeping one broadcast path).
- `RequestKeymap` - re-broadcasts the current bindings (late-subscriber pattern).
- `SetKeyBinding` / `RemoveKeyBinding` - apply the pure `Set`/`Remove`, persist via `SaveConfig`, warn on
  conflicts.

## Notes

- Persisted as `KeyMap.yaml` under `~/.clavis/config` (raw-text passthrough handled by the Configuration
  plugin; this plugin owns the (de)serialization in `KeymapFile`).
- Default bindings (seeded on first run) live in `KeymapBindings.Defaults` - application chords
  (`ToggleCommandPalette`, `SelectPanel`, `ToggleShortcutHelp`, `CloseActiveWindow`), `ToggleClavis` (system), per-kind
  `TogglePanel <kind>` and panel-scoped `Escape`/`CloseActivePanel`, plus the events-panel command set.
- Three scopes (`KeymapScope`): `system`, `application`, `panel` (most-specific wins). Gestures are
  normalized to a canonical chord spelling (`KeyGesture`) so YAML/user gestures compare equal to pressed
  ones. The host resolver produces the same spelling from live WPF input.
- Same-gesture-same-scope duplicates are warned (not blocked); at resolution the first match wins. `Set`
  moves a command's binding and evicts any other command on the same gesture in that scope+panel.
