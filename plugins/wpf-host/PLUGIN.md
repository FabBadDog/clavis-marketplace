---
name: wpf-host
pluginId: WpfHost
version: 7.7.0
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

Owns the Clavis application windows and the docking surface. It hosts one window per workspace (window
chrome - title bar and status bar) plus any number of secondary panel-host windows, each with its own named regions
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
- `DefaultPanels` (default `[chat]`) - panel kinds opened for a **workspace with nothing restorable saved**, so
  no workspace is ever a blank window. This is how the host seeds a chat without naming one in code; a saved
  layout always wins, including an empty one (a chat you closed stays closed). See the seeding note below - it
  is deliberately per workspace, not per launch.

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
  `ApplicationShutdown`, immediately - so the barrier costs nothing when nobody needs it. `ExitApplication` is
  the quit gesture and goes through it; it is idempotent, so asking twice during a quit already under way does
  nothing. A workspace window's own close is no longer a quit - it is refused outright (see below).
- **Panel placement is workspace-first** (`PanelPlacements`). The host remembers one window per panel *kind* -
  where you last put a git log, a keymap, an events view - so re-opening a kind returns it to where it was.
  That memory is right for a kind whose subject is the application and wrong for one whose subject is a
  workspace: "where a chat was last placed" names some other workspace's window as often as not. So the
  memory is honoured only while it stays inside the panel's own workspace; otherwise the panel goes to that
  workspace's chrome window. A panel belonging to no workspace keeps the memory unconditionally, which is what
  every kind did before workspaces owned windows. Honouring it unconditionally is what put all four
  workspaces' chats into a single window as tabs while their own windows stood empty - each open remembered
  where the previous one landed, so the first window swallowed the lot.
- **Persistence (layout v2).** The saved layout is normalised: a `windows` list carries identity, **role**
  (`primary`/`panel`), the owning **workspace** and one set of bounds each, and a separate `layouts` list carries
  one docking tree + slide-ins per **(window, workspace)** pair, plus that pair's own bounds once it has one.
  Geometry follows the workspace: switching restores where you had these windows, not only what was in them. A
  workspace that has never been on screen carries no bounds and the window's standing position stands in, so
  geometry is never duplicated across workspaces before anyone has moved anything - copies written up front are
  just copies waiting to drift out of step. The layout
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
- **Restoring is per (window, workspace), never per workspace** (`LayoutMigration.PendingRestores`). A
  workspace owns one tree per window it appears in, and the two kinds of window come back at different
  moments: panel windows are recreated eagerly at boot, chrome windows lazily on the workspace's first
  activation. Guarding "already restored" on the workspace alone let one panel window's restore mark the whole
  workspace done, and its chrome window's tree was then never put on screen - that workspace came up with no
  panels at all. The pair is what can actually be restored twice, so the pair is what is guarded.
- **A panel window keeps the id it was saved under; a chrome window is matched by workspace.** A chrome window
  is created anew each launch, so its saved id can never match and the *workspace* is its stable identity
  (`RebindWorkspaceWindow`). A panel window is matched by id, so recreating it must not mint a fresh one -
  doing so left the saved layout naming a window that no longer existed, an entry that could never be restored
  into again and that capture went on carrying over for ever.
- **The bootstrap window is named as soon as it holds a workspace's panels.** It is created before any
  workspace is known, and used to stay anonymous until the first `WorkspaceActivated` adopted it - whichever
  workspace that turned out to be. But the boot has already restored `activeWorkspaceId`'s tree into it, and
  that id comes from `state.yaml` while the workspace that activates first comes from `configuration.yaml`:
  two files, written at different moments, free to disagree. When they did, the first workspace to activate
  adopted a window holding another workspace's chat and the rightful owner restored its own alongside - one
  workspace with two chats, the other missing one. Adoption is now only for a start with nothing saved, where
  the window is genuinely anonymous.
- **Every panel is put on the books before the placement paths diverge** (`PlacePanel`). Workspace, kind and
  the kind's cardinality used to be recorded only when a panel was opened fresh; both restore paths returned
  first. After a restart every restored panel was therefore of no known workspace and no known kind, so asking
  for a kind that had been restored found nothing to reuse and opened a second one beside it, and an
  unclosable kind stopped being unclosable.
- **Transient workspaces are never persisted.** A workspace flagged `IsFleetAgent` stands for an agent running
  outside Clavis; its owner deliberately does not write it down, so neither does the layout. Activating one must
  not record it as `activeWorkspaceId` or give it a docking tree - the layout stores one tree per workspace, so a
  saved reference to a workspace that no longer exists restores an **empty surface on every tab**, with nothing
  in the UI able to explain or undo it. The last non-transient workspace is saved as active instead.
- **Default panels are seeded per workspace, not per launch** (`LayoutMigration.NeedsDefaultPanels`). A
  workspace is one chat plus its panels, so the second workspace you open needs a chat exactly as much as the
  first - and it has no saved layout to restore either. Seeding once per launch left every workspace but one a
  blank surface with no chat and no way to type. The test is "is there a saved entry for this workspace on a
  window that still exists", deliberately not "does that entry have panels": an entry with no panels is a chat
  you closed and is taken at its word, while an entry naming a window that is gone restores nothing at all. That
  second case is not hypothetical - it is what a layout written before transient workspaces were excluded looks
  like, and by itself it produced an empty surface on every tab with nothing in the UI able to explain it.
  Seeding waits for the same bar the restore sends do, since an `OpenPanel` for a kind the registry cannot
  resolve yet is simply dropped.
- **Window bounds are clamped on save, not only on restore.** `IsCenterWithin` already refuses to restore
  off-desktop bounds, but summon and banish animate a window through a position above the screen, so a snapshot
  taken while it is banished or mid-animation records somewhere it can never be seen again - and loses wherever
  it actually was. `LayoutTree.ClampToDesktop` pulls the whole window back on, so the title bar stays draggable;
  a window that reopens centred is a far quieter failure than one that reopens invisible.
- **The bar reserves its strip from a maximized window.** `BarPlacement.Reserve` computes the work area a
  maximized window should use once the bar has taken the top, and `WorkAreaMaximize` applies it when answering
  `WM_GETMINMAXINFO`. Without it a maximized window expands under the always-on-top bar and loses the top of its
  own chrome - its title bar sits behind a strip it cannot be dragged out from. The reservation is in DIPs while
  every rectangle in that message is in physical pixels, so the window's own DPI factor is read rather than
  assumed; the two only coincide at 100% scaling. (`Reserve` existed, documented and unit-tested, for some time
  before anything called it - the tests passed throughout, because they tested the function and not the wiring.)
- **The bar paints itself opaque.** It is the only content of a window with `AllowsTransparency`, so a
  background brush that fails to resolve leaves the desktop showing through wherever there is no tab - and
  `SetResourceReference` resolves at runtime, so a key that exists in neither the theme file nor the XAML
  fallback reports nothing and simply renders as transparent.
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
  workspaces' windows and fades this one's back in. **A workspace's own chrome window is one of them** - it is
  the workspace, not a constant the workspaces take turns inside, so exactly one is on screen at a time. The
  bootstrap window (created before any workspace is known) is adopted by the first activation and every later
  workspace mints its own. Every window's layout is keyed by *its* workspace, never by the active one, so a
  hidden window is not refiled to whatever you were looking at when the save fired - the exception that used to
  be made for the chrome window was a defect, and it wrote one workspace's panels into another's layout.
- **A workspace window cannot be closed.** It has no close cross and refuses its own `Closing` until teardown,
  because it holds that workspace's unclosable chat: closing it could only take the workspace with it, which is
  the bar's gesture and not this window's. Quitting is `ExitApplication`.
- **Window ids are minted per launch, so a saved layout is matched by workspace, not by window id**
  (`LayoutMigration.RebindWorkspaceWindow`). Without that a workspace's saved chrome window names an id no live
  window has, which reads as "nothing restorable" - the workspace is seeded a fresh default chat while its saved
  one is carried over untouched, and ends up with two.
- **One docking surface per workspace** (`WorkspaceSurfaces`), created lazily and kept alive. Now that a
  workspace owns its window, each window activates onto exactly one workspace and never swaps again, so the
  mechanism carries the adoption of the bootstrap window and little else. Background panels stay alive either
  way, which is what makes "workspace 3 is working" mean anything. The three chrome collaborators
  (`ActivePanelWatcher`, `FocusVisualController`, `PanelTitleController`) take an accessor for the surface
  rather than capturing one; surface handlers are attached per surface.
- **Region contributions are routed per workspace window** (`WindowManager.Regions.cs`). A contribution naming
  a `WorkspaceId` goes to that workspace's window and stays; one naming none belongs to whichever workspace is
  on screen and is *moved* there on a switch. Moved rather than copied because today's contributors return one
  long-lived element from their factory (`() => titleLeft.Element`) and a WPF element has one parent - which is
  honest while exactly one workspace is visible. Every contribution is remembered and replayed, since
  contributors announce their chrome once at activation, long before a later workspace has a window.
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
