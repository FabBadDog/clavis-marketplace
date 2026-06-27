---
name: wpf-host
pluginId: WpfHost
version: 3.0.3
essential: true
apiVersion: 1.0.0
description: Owns the application windows, regions, and the docking surface.
dependencies:
  - { name: host-contracts, version: 1 }
  - { name: workspace-contracts, version: 1 }
  - { name: keymap-contracts, version: 1 }
  - { name: clavis-rendering, version: 2 }
language: csharp
assemblyName: WpfHost
rootNamespace: FabioSoft.Nucleus.Plugins.WpfHost
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Host
  - FabioSoft.Contracts.Workspace
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

Owns the Clavis application windows and the docking surface. It hosts a primary window (conversation
chrome - title bar, prompt input, status bar) plus any number of secondary panel-host windows, each with
its own named regions (`main-content`, `title-bar-right`, `status-bar`, `status-bar-right`) and a
`DockingSurface` that tiles dockable panels. It materialises UI contributions other plugins announce,
opens/closes/toggles panels, manages edge slide-ins and a global summon hotkey, and persists the whole
workspace across launches. The host itself owns no conversation logic - it only provides the window and
region surface.

## Location

`src/plugins/WpfHost/` - a **UI plugin** (`UseWPF`), compiled-on-launch. `WindowManager.cs`,
`WindowHost.cs`, and `RegionManager.cs` carry the bulk of the logic.

## Config (`WpfHostConfig`)

- `UiScaleFactor` (default `1.6`) - global UI zoom; validated to the range 0.5-4.0.
- `DefaultWidth` (default `740`) / `DefaultHeight` (default `640`) - primary window size; each must be
  >= the matching minimum.
- `MinWidth` (default `400`) / `MinHeight` (default `260`) - minimum window size.
- `DefaultSlidePanels` - panel kinds shown as edge slide-ins by default (`usage-pace`->right,
  `git-log`->left, `keymap`->bottom). A saved layout that docks a kind as a tab overrides its default.

## Messages published

- Input: `UserSubmittedPrompt`, `UserAborted`, `UserCancelledQueued`.
- Commands/keymap: `RunCommand`, `RunPanelCommand`, `RequestKeymap`, `RequestCommands`, and
  `SummonClavis` (from `SummonSignal`, see Notes).
- Panels: `OpenPanel`, `RestorePanel`, `PanelClosed`, `SlideInRegistered`, `SlideInClosed`.
- Windows: `WindowOpened`, `WindowClosed`, `WindowFocusChanged`.
- Lifecycle/snapshot: `ApplicationShutdown`, and `WorkspaceSnapshot` (the response to
  `WorkspaceSnapshotRequested`).

## Messages subscribed

- UI regions: `UiRegionContribution`, `UiRegionRemoved`.
- Panels: `PanelInstanceReady`, `PanelStateChanged`, `OpenConversation`, `ShowSlideIn`, `TogglePanel`,
  `CloseActivePanel`.
- Keymap/commands: `KeymapChanged`, `CommandsAvailable`, `ToggleShortcutHelp`.
- Windows/app: `CloseWindow`, `CloseActiveWindow`, `FocusInputRequested`, `SummonClavis`,
  `ToggleClavis`, `BootstrapComplete`, `WorkspaceSnapshotRequested`.

## Notes

- **UI-thread bound.** Activation and all window/region work marshal onto `Application.Current.Dispatcher`.
- **Persistence.** Docking trees + per-panel state, each window's on-screen bounds, AND its edge slide-ins
  (panel, edge, saved state) are all saved together as this plugin's runtime *state* - the `WpfHost` section
  of `state.yaml` via the Configuration plugin (`SaveState`/`GetState`); `WorkspaceStore` owns the YAML
  (de)serialization, and every window carries its own `Bounds`, so there is no separate per-window state
  file. This is disposable state, not configuration: deleting `state.yaml` only resets the layout to the
  default. The layout loads asynchronously: the primary window shows immediately with a seeded conversation,
  then `StateResult` restores the saved bounds, docking tree, secondary windows and panels onto it. Panels are re-materialised
  via `RestorePanel` (deferred until `BootstrapComplete` so the registry can resolve their kinds) - docked
  panels swap into their slot, and slide-ins are re-anchored parked (hidden) on their saved edge, so a panel
  that was a slide-in or lived in an extra window comes back the same rather than as a default tab.
- **Snapshot.** It answers `WorkspaceSnapshotRequested` by building a `WorkspaceSnapshot` (windows,
  panels, focused window/panel) on the dispatcher - this is the response half of a bus request, used by
  AgentGateway's `workspace_snapshot` tool.
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
