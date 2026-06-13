---
name: command-palette
pluginId: CommandPalette
version: 1.1.0
apiVersion: 1.0.0
description: Keyboard-first command palette and primary input surface.
projectFile: ./CommandPalette.csproj
dependencies:
  - { name: host-contracts, version: 1 }
  - { name: session-contracts, version: 2 }
  - { name: services-contracts, version: 1 }
  - { name: keymap-contracts, version: 1 }
  - { name: workspace-contracts, version: 1 }
  - { name: clavis-rendering, version: 2 }
  - { name: yamldotnet, version: 1 }
---

# CommandPalette

## Purpose

The command palette UI (toggled by `ToggleCommandPalette`, default Ctrl+Shift+P). It catalogs every
available command - string-constructible bus message types, user/built-in aliases, agent commands (built-in
commands + skills), panel-scoped commands, and synthesised `toggle-<kind>`/`slide-<title>` aliases - and runs
them, whether typed in the popup or triggered by a keymap gesture. The shell is impure (owns the bus + the
window); parsing, construction, and routing are pure and provider-neutral.

## Location

`src/plugins/CommandPalette/` - a UI plugin (`UseWPF`). Registers no panel kind of its own; it owns a popup
window.

## Config (`CommandPaletteConfig`)

- `PaletteWidth` (default `952`) - popup width in DIPs. Must be between 200 and 1200.

## Messages published

- `CommandsAvailable` - the merged command catalog, broadcast whenever it changes.
- `UserSubmittedPrompt` - when a routed command resolves to an agent prompt.
- `GetConfig`, `SaveConfig` - loads its alias config; seeds a starter file when none exists.
- `RequestKeymap`, `RequestPanelCommands`, `PanelKindsRequested` - pulls current keymap/panel commands/panel
  kinds on activation (order-independent).
- `SetKeyBinding` - when the user binds a gesture to a command from the palette.
- Any concrete bus-message type a routed command constructs, published via reflection by `BusSender`
  (e.g. `TogglePanel`, `ShowSlideIn`).
- `LogEntry` via `bus.LogInfo`/`LogWarn`.

## Messages subscribed

- `ToggleCommandPalette` - shows/hides the popup.
- `RunCommand` - runs a command line (the keymap routes gestures through here).
- `RequestCommands` - re-broadcasts `CommandsAvailable`.
- `AgentStreamEvent` - matches the `AgentCommandsAvailable` case to ingest agent commands (a direct
  `Subscribe<AgentCommandsAvailable>` would never fire, since the bus dispatches by the base type).
- `ConfigResult`, `ConfigChanged` - loads/reloads aliases for its own plugin id.
- `KeymapChanged` - rebuilds the command->gesture lookup for display.
- `PanelCommandsRegistered` - folds panel-scoped commands into the catalog.
- `PanelKindRegistration` - synthesises a `toggle-<kind>` alias per user-openable kind.
- `SlideInRegistered`, `SlideInClosed` - adds/removes a `slide-<title>` summon command per slide-in panel.

## Notes

- The popup is the shared `SelectorWindow` (clavis-rendering) in free-text mode: `PaletteSelector` wires
  the suggestion provider, the routing-backed validation, Tab completion, and the palette-specific
  Alt+Enter shortcut capture into `SelectorOptions`; search, list navigation, input history, and the
  open/close animation live in the shared control.
- Window interaction runs on the WPF dispatcher; bus subscriptions run on bus threads. The window is lazily
  created on first toggle and reused.
- A user-authored alias overrides a synthesised `toggle-<kind>` alias of the same name.
- No persistence beyond the alias config file (managed by the Configuration plugin); the live catalog is
  rebuilt from incoming registrations.
