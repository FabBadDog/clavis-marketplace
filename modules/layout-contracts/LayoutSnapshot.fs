namespace FabioSoft.Contracts.Layout

open System

/// Asks the window host to report what is currently on screen. The host answers with a single
/// LayoutSnapshot, so a caller uses IBus.Request<LayoutSnapshotRequested, LayoutSnapshot>.
/// Built for introspection (the AgentGateway exposes it as a tool) rather than steady-state events.
[<Sealed>]
type LayoutSnapshotRequested() =
    do ()

/// One open application window in a LayoutSnapshot.
[<Sealed>]
type WindowSnapshot(windowId: Guid, title: string, isPrimary: bool, isFocused: bool) =
    member _.WindowId = windowId
    member _.Title = title
    member _.IsPrimary = isPrimary
    member _.IsFocused = isFocused

/// One live panel in a LayoutSnapshot. Placement is "tab" (docked) or "slide" (edge slide-in).
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

/// The window host's answer to LayoutSnapshotRequested: every open window and live panel, plus which
/// window and panel currently hold focus (Guid.Empty when nothing does).
[<Sealed>]
type LayoutSnapshot
    (windows: WindowSnapshot[],
     panels: PanelSnapshot[],
     focusedWindowId: Guid,
     focusedPanelInstanceId: Guid) =

    member _.Windows = windows
    member _.Panels = panels
    member _.FocusedWindowId = focusedWindowId
    member _.FocusedPanelInstanceId = focusedPanelInstanceId
