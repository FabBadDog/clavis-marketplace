namespace FabioSoft.Contracts.Layout

open System
open System.ComponentModel

/// Threaded into a panel's view factory when an instance is created. Carries the opaque per-instance
/// state blob (empty for a fresh panel, the restored blob otherwise) and a callback the panel invokes
/// whenever its state changes so the host can persist it. The host and registry never interpret the
/// blob - each panel owns its own format.
///
/// `WorkspaceId` is which workspace the instance is being created for. A panel whose content is per
/// workspace cannot get this from the state blob, which is empty for a fresh panel, so without it the only
/// thing such a factory could do is guess from whatever was on screen - and every workspace's panel ended up
/// bound to the same thing. `Guid.Empty` means no workspace (a panel created before workspaces exist).
[<Sealed>]
type PanelInstanceContext
    (instanceId: Guid, kind: string, savedState: string, workspaceId: Guid, onStateChanged: Action<string>) =

    new(instanceId, kind, savedState, onStateChanged) =
        PanelInstanceContext(instanceId, kind, savedState, Guid.Empty, onStateChanged)

    member _.InstanceId = instanceId
    member _.Kind = kind
    member _.SavedState = savedState
    member _.WorkspaceId = workspaceId
    member _.OnStateChanged = onStateChanged

/// How many instances of a panel kind may live at once, and within what boundary. Declared by the owning
/// plugin instead of inferred by the host, which used to dedupe every kind application-wide - a rule that
/// conflated "there is one of these per chat" with "there is one of these in the whole application".
/// String literals so they cross load contexts and round-trip through YAML without enum-identity concerns
/// (same pattern as KeymapScope).
[<RequireQualifiedAccess>]
module PanelCardinality =

    /// The default: one instance per workspace, so today's feel repeats per workspace.
    [<Literal>]
    let OnePerWorkspace = "one-per-workspace"

    /// One instance for the whole application, wherever it is docked (the plugin manager, the overview).
    [<Literal>]
    let OnePerApplication = "one-per-application"

    /// No dedupe at all - every open mints a new instance (the markdown:{id} family).
    [<Literal>]
    let Many = "many"

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

    /// How many instances of this kind may live at once (a PanelCardinality literal). Settable for the same
    /// reason as StatusTemplate, so existing registrations keep their seven-argument constructor. Empty is
    /// read as OnePerWorkspace, the default the host applied to every kind before cardinality was declared.
    member val Cardinality = PanelCardinality.OnePerWorkspace with get, set

/// The registry broadcasts this on its own activation; panel plugins subscribe and re-announce their
/// kinds. Makes activation order irrelevant (a fire-and-forget registration sent before the registry
/// subscribed would otherwise be lost).
[<Sealed>]
type PanelKindsRequested() =
    do ()

/// WorkspaceId scopes the request to one workspace; Guid.Empty means "the active workspace", which only
/// the host can resolve - so the registry never learns which workspace is active.
[<Sealed>]
[<Description("Open a panel of the given kind in the active window")>]
type OpenPanel(kind: string, workspaceId: Guid) =

    new(kind) = OpenPanel(kind, Guid.Empty)

    member _.Kind = kind
    member _.WorkspaceId = workspaceId

/// Open the panel of the given kind if none is live, or close/dismiss it if one already is - so a single
/// gesture both summons and banishes a panel. A live docked tab is closed; a live slide-in is hidden if
/// shown and revealed if hidden; with no live instance it behaves like OpenPanel.
[<Sealed>]
[<Description("Toggle a panel of the given kind in the active window")>]
type TogglePanel(kind: string, workspaceId: Guid) =

    new(kind) = TogglePanel(kind, Guid.Empty)

    member _.Kind = kind
    member _.WorkspaceId = workspaceId

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
type RestorePanel(instanceId: Guid, kind: string, savedState: string, workspaceId: Guid) =

    new(instanceId, kind, savedState) = RestorePanel(instanceId, kind, savedState, Guid.Empty)

    member _.InstanceId = instanceId
    member _.Kind = kind
    member _.SavedState = savedState
    member _.WorkspaceId = workspaceId

/// The registry resolved a kind to a realised view and hands it to the host for placement. View is the
/// cross-ALC BCL delegate producing the FrameworkElement.
[<Sealed>]
type PanelInstanceReady
    (instanceId: Guid,
     kind: string,
     title: string,
     minWidth: float,
     minHeight: float,
     view: Func<obj>,
     workspaceId: Guid) =

    new(instanceId, kind, title, minWidth, minHeight, view) =
        PanelInstanceReady(instanceId, kind, title, minWidth, minHeight, view, Guid.Empty)

    member _.InstanceId = instanceId
    member _.Kind = kind
    member _.Title = title
    member _.MinWidth = minWidth
    member _.MinHeight = minHeight
    member _.View = view
    member _.WorkspaceId = workspaceId

    /// The kind's declared cardinality, carried through from its registration so the host can enforce a
    /// declared rule without subscribing to registrations itself. Settable, so the registry fills it in
    /// without the placement message growing another positional argument.
    member val Cardinality = PanelCardinality.OnePerWorkspace with get, set

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
