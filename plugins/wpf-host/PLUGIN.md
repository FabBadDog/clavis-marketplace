---
name: wpf-host
pluginId: WpfHost
version: 4.1.0
essential: true
apiVersion: 1.0.0
description: Owns the application windows, regions, and the docking surface.
dependencies:
  - { name: host-contracts, version: 3 }
  - { name: layout-contracts, version: 2 }
  - { name: keymap-contracts, version: 1 }
  - { name: clavis-rendering, version: 2 }
language: csharp
assemblyName: WpfHost
rootNamespace: FabioSoft.Nucleus.Plugins.WpfHost
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Host
  - FabioSoft.Contracts.Layout
  - FabioSoft.Contracts.Keymap
  - FabioSoft.Contracts.Services
resources:
  - Fonts/Rajdhani-Medium.ttf
  - Fonts/Syne-Regular.ttf
  - Fonts/Inter-Light.ttf
  - Fonts/Inter-Regular.ttf
  - Fonts/JetBrainsMono-Regular.ttf
---

# WpfHost

## Purpose

Owns the Clavis application windows and the docking surface. It hosts a primary window (window chrome -
title bar and status bar) plus any number of secondary panel-host windows, each with its own named regions
(`title-bar-left`, `title-bar-right`, `status-bar`, `status-bar-right`) and a `DockingSurface` that tiles
dockable panels. It materialises UI contributions other plugins announce, opens/closes/toggles panels,
manages edge slide-ins and a global summon hotkey, and persists the whole layout across launches. The host
owns no conversation logic and no chat vocabulary at all - the chat is a registered panel kind (`chat`,
owned by Conversation, carrying its own prompt input) placed on the surface like any other.

## Location

`src/plugins/WpfHost/` - a **UI plugin** (`UseWPF`), compiled-on-launch. `WindowHost.cs` and
`RegionManager.cs` carry the per-window logic; `WindowManager` is one class split across partial files by
concern - the core (fields, construction, the window ring, disposal) plus `.Subscriptions` (the whole bus
routing table), `.Visibility` (reveal / summon / banish), `.Windows` (window lifecycle), `.LayoutRestore`
(persist and restore), `.Panels` (placement, toggle, close, retitle), `.CrossWindowDrag`, `.Snapshot` and
`.Placeholder`. The pure tree walks and geometry it relies on live in `LayoutTree.cs`, outside the class and
unit-tested.

## Config (`WpfHostConfig`)

- `UiScaleFactor` (default `1.6`) - global UI zoom; validated to the range 0.5-4.0.
- `DefaultWidth` (default `740`) / `DefaultHeight` (default `640`) - primary window size; each must be
  >= the matching minimum.
- `MinWidth` (default `400`) / `MinHeight` (default `260`) - minimum window size.
- `DefaultSlidePanels` - panel kinds shown as edge slide-ins by default (`usage-limits`->right,
  `git-log`->left, `keymap`->bottom). A saved layout that docks a kind as a tab overrides its default.
- `DefaultPanels` (default `[chat]`) - panel kinds opened on a launch with **no saved layout**, so a first
  run is never a blank window. This is how the host seeds a chat without naming one in code; a saved layout
  always wins, including an empty one (a chat you closed stays closed).

## Messages published

- Input: `UserSubmittedPrompt`, `UserAborted`, `UserCancelledQueued`.
- Commands/keymap: `RunCommand`, `RunPanelCommand`, `RequestKeymap`, `RequestCommands`, and
  `SummonClavis` (from `SummonSignal`, see Notes).
- Panels: `OpenPanel`, `RestorePanel`, `PanelClosed`, `SlideInRegistered`, `SlideInClosed`.
- Windows: `WindowOpened`, `WindowClosed`, `WindowFocusChanged`.
- Lifecycle/snapshot: `ApplicationShutdown`, and `LayoutSnapshot` (the response to
  `LayoutSnapshotRequested`).

## Messages subscribed

- UI regions: `UiRegionContribution`, `UiRegionRemoved`.
- Panels: `PanelInstanceReady`, `PanelStateChanged`, `ShowSlideIn`, `TogglePanel`,
  `CloseActivePanel`.
- Keymap/commands: `KeymapChanged`, `CommandsAvailable`, `ToggleShortcutHelp`.
- Windows/app: `CloseWindow`, `CloseActiveWindow`, `ExitApplication` (the one gesture that ends the process -
  the palette's `exit` closes a workspace now), `SummonClavis`, `ToggleClavis`, `BootstrapComplete`,
  `LayoutSnapshotRequested`.

## Notes

- **UI-thread bound.** Activation and all window/region work marshal onto `Application.Current.Dispatcher`.
- **Persistence.** Docking trees + per-panel state, each window's on-screen bounds, AND its edge slide-ins
  (panel, edge, saved state) are all saved together as this plugin's runtime *state* - the `WpfHost` section
  of `state.yaml` via the Configuration plugin (`SaveState`/`GetState`); `LayoutFile` owns the YAML
  (de)serialization, and every window carries its own `Bounds`, so there is no separate per-window state
  file. This is disposable state, not configuration: deleting `state.yaml` only resets the layout to the
  default. The layout loads asynchronously: the primary window shows with an empty surface,
  then `StateResult` restores the saved bounds, docking tree, secondary windows and panels onto it. Panels are re-materialised
  via `RestorePanel` (deferred until `BootstrapComplete` so the registry can resolve their kinds) - docked
  panels swap into their slot, and slide-ins are re-anchored parked (hidden) on their saved edge, so a panel
  that was a slide-in or lived in an extra window comes back the same rather than as a default tab.
- **Declared cardinality, not app-wide dedupe.** The host used to treat every panel kind as an
  application-wide singleton. It now enforces the kind's *declared* `PanelCardinality`, carried from its
  registration onto `PanelInstanceReady`: `many` never reuses, `one-per-application` reuses an instance in any
  window, and `one-per-workspace` (the default, and what an unset value means) reuses only one in the same
  workspace. The rule itself is the pure `LivePanels.Find`; the host only enumerates what is live. Every panel
  id is remembered against its workspace, all `Guid.Empty` until workspaces exist - which is exactly the old
  application-wide behaviour.
- **Snapshot.** It answers `LayoutSnapshotRequested` by building a `LayoutSnapshot` (windows,
  panels, focused window/panel) on the dispatcher - this is the response half of a bus request, used by
  AgentGateway's `layout_snapshot` tool.
- A `GlobalHotkey` on the primary window feeds `RunCommand`; its default chord runs `ToggleClavis`.
- **Summon/hide toggle.** `SummonClavis` always brings every window to the foreground (windows that were
  hidden or minimized fall in from the top via `Motion.fallInWindow`, the primary is activated last).
  `ToggleClavis` - the global hotkey's command - summons the same way when no Clavis window is focused,
  and otherwise hides every window (each rises up out of the screen via `Motion.riseOutWindow`, then
  `Hide()`). Hiding never exits the app (`OnExplicitShutdown`) and the hotkey stays registered on the
  hidden primary's live HWND, so the same gesture brings everything back.
- **Single instance.** A second Clavis launch for the same Clavis home never boots: the host signals a
  named activation event (its name advertised via the `ClavisActivationEvent` environment variable) and
  exits. `SummonSignal` listens on that event and publishes `SummonClavis`, so the running instance's
  primary window comes to the foreground through the same path as the global hotkey. Inert when the
  variable is absent (an older host without the guard).
