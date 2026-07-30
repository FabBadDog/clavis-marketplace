---
name: wpf-host
pluginId: WpfHost
version: 7.1.1
essential: true
apiVersion: 1.0.0
description: Owns the application windows, regions, and the docking surface.
dependencies:
  - { name: host-contracts, version: 3 }
  - { name: layout-contracts, version: 2 }
  - { name: workspace-contracts, version: 2 }
  - { name: keymap-contracts, version: 1 }
  - { name: clavis-rendering, version: 3 }
language: csharp
assemblyName: WpfHost
rootNamespace: FabioSoft.Nucleus.Plugins.WpfHost
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Host
  - FabioSoft.Contracts.Layout
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
- Lifecycle/snapshot: `ShutdownPreparing` then `ApplicationShutdown` (see the shutdown barrier in Notes), and
  `LayoutSnapshot` (the response to `LayoutSnapshotRequested`).

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
- **Quitting is two-phase** (`ShutdownBarrier`). `ApplicationShutdown` takes effect immediately - the host shuts
  the WPF application down as soon as it sees it - which is fine for work already on disk but not for work that
  has to *start* on the way out, such as handing a session back to a background agent (a spawn that must happen
  while Clavis still exists). So a plugin with such work declares a `ShutdownParticipant` at activation; the
  window owner broadcasts `ShutdownPreparing` and holds `ApplicationShutdown` until every participant has
  answered `ShutdownPrepared`, or until `ShutdownGraceSeconds` expires. **The barrier always opens**: a
  participant that never answers delays the quit, it can never prevent it, and the outstanding names are logged
  so the pause is diagnosable. With nothing declared the behaviour is exactly what it was before - one
  `ApplicationShutdown`, immediately - so the barrier costs nothing when nobody needs it. Both quit gestures (the
  primary window's own close and `ExitApplication`) go through it, and it is idempotent, so closing the window
  during a quit already under way does nothing.
- **Persistence (layout v2).** The saved layout is normalised: a `windows` list carries identity, **role**
  (`primary`/`panel`), the owning **workspace** and one set of bounds each, and a separate `layouts` list carries
  one docking tree + slide-ins per **(window, workspace)** pair. Geometry is deliberately not per workspace -
  otherwise the primary's bounds would be duplicated once per workspace and the copies would drift. The layout
  also persists `activeWorkspaceId` itself, so it stays self-sufficient and the reveal keeps waiting on exactly
  the two answers it always did (a third precondition would be a third way for boot to hang). A **version-1**
  layout is migrated forward rather than discarded (`LayoutMigration.FromVersion1`), with `Guid.Empty` as an
  explicit "unassigned" workspace that `Adopt` binds on the first `WorkspaceActivated`; `DropOrphans` discards
  layouts of workspaces that no longer exist. All of it saved as this plugin's runtime *state* - the `WpfHost` section
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
- **A workspace's extra windows travel with it.** Every secondary window records the workspace it was torn
  off in, and `OrderedWindows()` is scoped to the active one. That is the single funnel the reveal, summon,
  banish and the cross-window Tab ring all read, so a window belonging to a workspace you are not looking at is
  uniformly absent from every one of them instead of each site remembering to filter. Switching hides the other
  workspaces' windows and fades this one's back in. The primary is the constant - it carries the chrome for
  every workspace and belongs to none. A secondary's layout is keyed by *its* workspace, not the active one, so
  a hidden window is not refiled to whatever you were looking at when the save fired.
- **One docking surface per workspace, per window** (`WorkspaceSurfaces`), created lazily and kept alive;
  `WindowHost.Surface` forwards to the active one so every existing call site is unchanged. N surfaces rather
  than one captured-and-restored surface: swapping a single surface would rebuild every panel view on each
  switch (scroll lost, `PanelClosed` disposing instances, git-log timers restarting) on a gesture used dozens of
  times an hour - and keeping background panels alive is what makes "workspace 3 is working" mean anything. The
  three chrome collaborators (`ActivePanelWatcher`, `FocusVisualController`, `PanelTitleController`) take an
  accessor for the active surface rather than capturing one, so they follow the switch. Surface handlers are
  attached per surface. The initial `Guid.Empty` surface is **adopted** on the first real activation rather than
  replaced, so the panels restored during boot stay put. Switches cross-fade; the outgoing surface is collapsed,
  not merely transparent, so it stops taking hit tests and tab stops.
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
