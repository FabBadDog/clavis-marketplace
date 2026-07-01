namespace FabioSoft.Contracts.Workspace

open System
open System.ComponentModel

/// Threaded into a panel's view factory when an instance is created. Carries the opaque per-instance
/// state blob (empty for a fresh panel, the restored blob otherwise) and a callback the panel invokes
/// whenever its state changes so the host can persist it. The host and registry never interpret the
/// blob - each panel owns its own format.
[<Sealed>]
type PanelInstanceContext
    (instanceId: Guid, kind: string, savedState: string, onStateChanged: Action<string>) =

    member _.InstanceId = instanceId
    member _.Kind = kind
    member _.SavedState = savedState
    member _.OnStateChanged = onStateChanged

/// A panel plugin announces a kind of panel it can create. ViewFactory is a BCL delegate so it crosses
/// AssemblyLoadContext boundaries (same pattern as UiRegionContribution.ViewFactory). Returns obj, not
/// FrameworkElement, so this contract assembly stays WPF-free; the host casts. IsUserOpenable is false for
/// a kind that can still be restored from a saved layout but should not be offered as an openable panel
/// (no synthesised toggle command, no default shortcut) - e.g. a kind that only makes sense once some
/// prerequisite feature exists.
[<Sealed>]
type PanelKindRegistration
    (kind: string,
     title: string,
     minWidth: float,
     minHeight: float,
     icon: string,
     isUserOpenable: bool,
     viewFactory: Func<PanelInstanceContext, obj>) =

    member _.Kind = kind
    member _.Title = title
    member _.MinWidth = minWidth
    member _.MinHeight = minHeight
    member _.Icon = icon
    member _.IsUserOpenable = isUserOpenable
    member _.ViewFactory = viewFactory

    /// An optional default status-bar template for this panel kind, shown while the panel is the active
    /// docked panel and the user has not configured the status bar for it. Empty means the panel ships no
    /// default, so the window collapses the status bar entirely (the panel fills the space) until one is set.
    /// Settable so a C# registration can use an object initializer; the seven-argument constructor is unchanged.
    member val StatusTemplate = "" with get, set

/// The registry broadcasts this on its own activation; panel plugins subscribe and re-announce their
/// kinds. Makes activation order irrelevant (a fire-and-forget registration sent before the registry
/// subscribed would otherwise be lost).
[<Sealed>]
type PanelKindsRequested() =
    do ()

[<Sealed>]
[<Description("Open a panel of the given kind in the active window")>]
type OpenPanel(kind: string) =
    member _.Kind = kind

/// Open the panel of the given kind if none is live, or close/dismiss it if one already is - so a single
/// gesture both summons and banishes a panel. A live docked tab is closed; a live slide-in is hidden if
/// shown and revealed if hidden; with no live instance it behaves like OpenPanel.
[<Sealed>]
[<Description("Toggle a panel of the given kind in the active window")>]
type TogglePanel(kind: string) =
    member _.Kind = kind

/// Close or dismiss the focused panel. A parameterless companion to ClosePanel so a panel-scoped gesture
/// (e.g. Esc) can banish the focused panel without naming its instance - the host resolves "focused"
/// itself: an open slide-in is hidden, otherwise the active docked panel is closed.
[<Sealed>]
[<Description("Close or dismiss the focused panel")>]
type CloseActivePanel() =
    do ()

/// Re-materialise a previously-open panel during layout restore. Like OpenPanel but seeds the existing
/// instance id and saved state instead of minting a fresh instance.
[<Sealed>]
type RestorePanel(instanceId: Guid, kind: string, savedState: string) =
    member _.InstanceId = instanceId
    member _.Kind = kind
    member _.SavedState = savedState

/// The registry resolved a kind to a realised view and hands it to the host for placement. View is the
/// cross-ALC BCL delegate producing the FrameworkElement.
[<Sealed>]
type PanelInstanceReady
    (instanceId: Guid, kind: string, title: string, minWidth: float, minHeight: float, view: Func<obj>) =

    member _.InstanceId = instanceId
    member _.Kind = kind
    member _.Title = title
    member _.MinWidth = minWidth
    member _.MinHeight = minHeight
    member _.View = view

/// Request to close a panel instance (e.g. from a command). The host removes it from its surface and
/// announces PanelClosed.
[<Sealed>]
type ClosePanel(instanceId: Guid) =
    member _.InstanceId = instanceId

/// Announced by the host after a panel's tab is removed, so the registry drops the instance and disposes
/// it (stops timers etc.).
[<Sealed>]
type PanelClosed(instanceId: Guid) =
    member _.InstanceId = instanceId

/// The registry forwards a panel's state change to the host, which folds it into the persisted layout.
[<Sealed>]
type PanelStateChanged(instanceId: Guid, state: string) =
    member _.InstanceId = instanceId
    member _.State = state

/// Retitle a live panel instance's tab (and its persisted slot title). A panel whose title is derived
/// from user-editable data (e.g. a markdown panel bound to a renamed definition) publishes this so its
/// open tab updates without being reopened. The host applies it to the surface and persists the layout.
[<Sealed>]
type SetPanelTitle(instanceId: Guid, title: string) =
    member _.InstanceId = instanceId
    member _.Title = title

/// Re-open the chat conversation in the primary window. The conversation is a singleton panel; if it is
/// already open this focuses it, otherwise the host re-seeds it. Lets a closed chat be brought back.
[<Sealed>]
[<Description("Open the chat conversation")>]
type OpenConversation() =
    do ()

/// A panel was anchored to a window edge as a slide-in (dragged into an edge's slide zone). The host
/// broadcasts this so the command palette can offer a per-panel command that summons it back after it
/// auto-hides. Title is the panel's tab title, for display.
[<Sealed>]
type SlideInRegistered(instanceId: Guid, title: string) =
    member _.InstanceId = instanceId
    member _.Title = title

/// A slide-in was removed (docked again, closed, or its window closed). The palette drops its summon
/// command.
[<Sealed>]
type SlideInClosed(instanceId: Guid) =
    member _.InstanceId = instanceId

/// Summon a specific slide-in: slide it in from its edge, hiding any conflicting (same or perpendicular
/// edge) slide-in. Dispatched by the palette command synthesised from a SlideInRegistered.
[<Sealed>]
type ShowSlideIn(instanceId: Guid) =
    member _.InstanceId = instanceId

[<Sealed>]
type CloseWindow(windowId: Guid) =
    member _.WindowId = windowId

/// Close the currently focused window. A parameterless companion to CloseWindow so the keymap can bind
/// it without knowing the active window id (the host resolves "active" itself).
[<Sealed>]
[<Description("Close the active window")>]
type CloseActiveWindow() =
    do ()

[<Sealed>]
type WindowOpened(windowId: Guid, title: string) =
    member _.WindowId = windowId
    member _.Title = title

[<Sealed>]
type WindowClosed(windowId: Guid) =
    member _.WindowId = windowId

[<Sealed>]
type WindowFocusChanged(windowId: Guid) =
    member _.WindowId = windowId

/// The active docked panel in the primary window changed to this kind ("" when none). The chrome owner
/// re-templates the window's title bar and status bar to that panel's configured chrome, so the active
/// panel owns the window chrome. Only docked panels raise this - slide-ins never change the title/status bar.
[<Sealed>]
type ActivePanelChanged(kind: string) =
    member _.Kind = kind

/// Asks the window host to report what is currently on screen. The host answers with a single
/// WorkspaceSnapshot, so a caller uses IBus.Request<WorkspaceSnapshotRequested, WorkspaceSnapshot>.
/// Built for introspection (the AgentGateway exposes it as a tool) rather than steady-state events.
[<Sealed>]
type WorkspaceSnapshotRequested() =
    do ()

/// One open application window in a WorkspaceSnapshot.
[<Sealed>]
type WindowSnapshot(windowId: Guid, title: string, isPrimary: bool, isFocused: bool) =
    member _.WindowId = windowId
    member _.Title = title
    member _.IsPrimary = isPrimary
    member _.IsFocused = isFocused

/// One live panel in a WorkspaceSnapshot. Placement is "tab" (docked) or "slide" (edge slide-in).
/// IsVisible means the panel is actually showing: the selected tab of its group, or an open slide-in.
[<Sealed>]
type PanelSnapshot
    (instanceId: Guid,
     kind: string,
     title: string,
     windowId: Guid,
     isFocused: bool,
     isVisible: bool,
     placement: string) =

    member _.InstanceId = instanceId
    member _.Kind = kind
    member _.Title = title
    member _.WindowId = windowId
    member _.IsFocused = isFocused
    member _.IsVisible = isVisible
    member _.Placement = placement

/// The window host's answer to WorkspaceSnapshotRequested: every open window and live panel, plus which
/// window and panel currently hold focus (Guid.Empty when nothing does).
[<Sealed>]
type WorkspaceSnapshot
    (windows: WindowSnapshot[],
     panels: PanelSnapshot[],
     focusedWindowId: Guid,
     focusedPanelInstanceId: Guid) =

    member _.Windows = windows
    member _.Panels = panels
    member _.FocusedWindowId = focusedWindowId
    member _.FocusedPanelInstanceId = focusedPanelInstanceId
