---
name: keymap-panel
pluginId: KeymapPanel
version: 1.0.1
apiVersion: 1.0.0
description: Keybinding inspector dockable panel.
dependencies:
  - { name: keymap-contracts, version: 1 }
  - { name: workspace-contracts, version: 1 }
  - { name: clavis-controls, version: 1 }
language: csharp
assemblyName: KeymapPanel
rootNamespace: FabioSoft.Nucleus.Plugins.KeymapPanel
useWpf: true
globalUsings:
  - FabioSoft.Nucleus.Contracts
  - FabioSoft.Contracts.Keymap
  - FabioSoft.Contracts.Workspace
---

# KeymapPanel

## Purpose

Announces the `keymap` dockable panel kind: a shortcut-management view that lists the current key
bindings grouped by scope and lets the user add, change, and remove them. The view reads the live
`KeymapChanged` and `CommandsAvailable` broadcasts and sends edits back to the KeyMap plugin. Gestures
are typed as text (e.g. `Ctrl+Shift+K`) and normalized by KeyMap on save, so the view needs no key
capture.

## Location

`src/plugins/KeymapPanel/` - UI plugin (`UseWPF`). Registers panel kind `"keymap"` (toggle alias
`shortcuts`), user-openable, min size 320x200.

## Config (`KeymapPanelConfig`)

Empty record - no configurable fields.

## Messages published

- `PanelKindRegistration` - announces the `keymap` panel kind (on activation and on each
  `PanelKindsRequested`).
- `SetKeyBinding` - when the user binds/changes a gesture via the add row.
- `RemoveKeyBinding` - when the user removes a binding row.
- `RequestKeymap` / `RequestCommands` - sent when a panel view loads, to pull the current bindings and
  command catalog.
- `LogEntry` via `bus.LogInfo` (activation notice).

## Messages subscribed

- `PanelKindsRequested` - re-announces the panel kind so activation order does not matter.
- `KeymapChanged` (per live view) - rebuilds the scope-grouped binding list.
- `CommandsAvailable` (per live view) - refreshes the bindable-command dropdown.

## Notes

- The `KeymapChanged` / `CommandsAvailable` subscriptions are per-view-instance, created on the view's
  `Loaded` and disposed on `Unloaded`. The plugin itself holds only the `PanelKindsRequested`
  subscription.
- The add row offers scope `application` / `system` / `panel`; the panel-kind field is only used for
  panel scope. Only commands with `IsBindable` true are offered in the dropdown.
- No persistence of its own - all binding state lives in the KeyMap plugin's config.
