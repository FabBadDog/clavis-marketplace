namespace FabioSoft.Contracts.Layout

open System
open System.ComponentModel

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

/// Hands a workspace the docking surface of its window, addressed to that workspace's scope. The window
/// owner creates windows and surfaces; who fills one is the workspace-scoped host bound to that scope.
///
/// This is what makes a workspace's panels structurally its own: exactly one plugin instance can ever
/// hold a given surface, and that instance carries only that workspace's restore state - so filing one
/// workspace's panels under another stops being expressible rather than merely being avoided.
///
/// `Surface` is `obj` for the same reason `UiRegionContribution.ViewFactory` is: a contract module stays
/// free of WPF and of the control library, and the one consumer that needs the concrete type casts to it.
[<Sealed>]
type WorkspaceSurfaceReady(workspaceId: Guid, windowId: Guid, surface: obj) =
    member _.WorkspaceId = workspaceId
    member _.WindowId = windowId
    member _.Surface = surface
