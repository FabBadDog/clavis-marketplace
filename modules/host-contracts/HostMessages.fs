namespace FabioSoft.Contracts.Host

open System
open System.Collections.Generic
open System.ComponentModel

/// A plugin's contribution of a view into a named host region. Modelled as a class with a
/// 4-argument convenience constructor so callers can omit the optional resources dictionary.
[<Sealed>]
type UiRegionContribution
    (regionId: string, pluginId: string, priority: int, viewFactory: Func<obj>, resources: obj) =

    new(regionId, pluginId, priority, viewFactory) =
        UiRegionContribution(regionId, pluginId, priority, viewFactory, null)

    member _.RegionId = regionId
    member _.PluginId = pluginId
    member _.Priority = priority
    member _.ViewFactory = viewFactory
    member _.Resources = resources

[<Sealed>]
type UiRegionRemoved(regionId: string, pluginId: string) =
    member _.RegionId = regionId
    member _.PluginId = pluginId

[<Sealed>]
[<Description("Submit the typed prompt to the agent")>]
type UserSubmittedPrompt(prompt: string) =
    member _.Prompt = prompt

[<Sealed>]
[<Description("Abort the current agent turn")>]
type UserAborted() =
    do ()

[<Sealed>]
[<Description("Cancel the queued prompt")>]
type UserCancelledQueued() =
    do ()

/// The user moves the highlighted choice of the pending permission prompt (Left = -1, Right = +1).
/// Published by the host's key handler; the Conversation plugin moves the selection in its pure state.
[<Sealed>]
[<Description("Move the permission prompt selection")>]
type UserNavigatedPermission(delta: int) =
    member _.Delta = delta

/// The user confirms the pending permission prompt at its currently highlighted choice (Enter).
[<Sealed>]
[<Description("Confirm the highlighted permission choice")>]
type UserConfirmedPermission() =
    do ()

/// The active panel's owner announces whether its status bar has any configured content. The window host
/// collapses the status row when it has none, so the panel fills the whole space rather than showing an
/// empty bar - and reveals it again when content returns. A host/active-panel concern: the host learns no
/// placeholder vocabulary, only this availability broadcast.
[<Sealed>]
type StatusBarAvailability(available: bool) =
    member _.Available = available

/// A panel asks for keyboard focus to return to the prompt input (keyboard-first navigation). The prompt
/// lives inside the chat panel, so its owner - not the window host - answers this.
[<Sealed>]
[<Description("Move keyboard focus to the prompt input")>]
type FocusInputRequested() =
    do ()

/// The host's global Ctrl+P shortcut asks the command palette to open (or close if already open).
/// The host owns input but not the palette, so it publishes intent and the CommandPalette plugin reacts.
[<Sealed>]
[<Description("Open or close the command palette")>]
type ToggleCommandPalette() =
    do ()

/// Show or hide the keyboard-shortcut help overlay in the active window. The host hosts the overlay in
/// every window and populates it from the merged keymap bindings for the current scope.
[<Sealed>]
[<Description("Toggle the keyboard shortcut help overlay")>]
type ToggleShortcutHelp() =
    do ()

/// Bring Clavis to the foreground unconditionally: show every application window (restoring from
/// minimized or hidden) and activate the primary one. Published by the single-instance guard when a
/// second launch signals the running instance, and by the agent's summon tool - both must never hide
/// anything, which is why this stays separate from ToggleClavis.
[<Sealed>]
[<Description("Bring Clavis to the foreground")>]
type SummonClavis() =
    do ()

/// Quit the application for real. Needed because the palette's `exit` was repurposed to mean "close this
/// workspace": separating the two gestures left no way out of the app, so this is the explicit one. The only
/// destructive gesture in the workspace bar, and the only one that goes through a confirmation first.
[<Sealed>]
[<Description("Quit Clavis")>]
type ExitApplication() =
    do ()

// --- Shutdown barrier ---
//
// The framework's ApplicationShutdown takes effect immediately: the host shuts the WPF application down as soon
// as it sees it, with no drain. That is fine for anything whose work is already on disk, but not for work that
// has to *start* on the way out - handing a session back to a background agent, for instance, which spawns a
// process that must be running before Clavis stops existing.
//
// So quitting is two-phase. A plugin that needs a moment declares itself a participant, and the window owner
// broadcasts ShutdownPreparing and holds ApplicationShutdown until every participant has answered - or until a
// grace period expires, because a plugin that never answers must not be able to make the application unquittable.

/// Declare that this plugin has work to do before the application exits, so quitting waits for it. Declared at
/// activation, not at shutdown: by then the broadcast has already gone out.
[<Sealed>]
type ShutdownParticipant(pluginId: string) =
    member _.PluginId = pluginId

/// The application is quitting. Every participant should do what it must and answer with ShutdownPrepared.
/// Deliberately not a request/response: participants are independent, and one that fails must not prevent the
/// others from being asked.
[<Sealed>]
type ShutdownPreparing() =
    do ()

/// This participant is done and the application may exit as far as it is concerned. Sending it early is always
/// safe; not sending it only costs the grace period.
[<Sealed>]
type ShutdownPrepared(pluginId: string) =
    member _.PluginId = pluginId

/// Toggle Clavis visibility: when no application window is focused, bring them all to the foreground
/// (windows that were hidden fall in from the top); when one is focused, hide them all (they rise out
/// the top). Bound to the system-scope global hotkey, so one gesture both summons and banishes the
/// application.
[<Sealed>]
[<Description("Summon Clavis or hide it again")>]
type ToggleClavis() =
    do ()

/// Open the model selector popup for the active session (the choices come from the provider bridge's
/// AgentCapabilities). Handled by the Selection plugin.
[<Sealed>]
[<Description("Select the agent model")>]
type SelectModel() =
    do ()

/// Open the reasoning-effort selector popup for the active session (only the levels the current model
/// supports are offered). Handled by the Selection plugin.
[<Sealed>]
[<Description("Select the agent reasoning effort")>]
type SelectEffort() =
    do ()

/// Open the mode selector popup for the active session. Handled by the Selection plugin.
[<Sealed>]
[<Description("Select the agent mode")>]
type SelectMode() =
    do ()

/// Advance the active session to the next permission mode, wrapping at the end (the default Shift+Tab
/// gesture). Handled by the Selection plugin, which knows the current mode and the mode catalog and sends
/// the concrete mode switch - so the host only has to dispatch the command, learning no session vocabulary.
[<Sealed>]
[<Description("Cycle to the next agent mode")>]
type CycleSessionMode() =
    do ()

/// Open the panel selector popup: every user-openable panel kind, opening the chosen one.
/// Handled by the Selection plugin.
[<Sealed>]
[<Description("Select and open a panel")>]
type SelectPanel() =
    do ()

/// Open the workspace selector popup: every workspace, activating the chosen one. Handled by the Selection
/// plugin. It lists more than the bar does - the bar shows only what an F-key reaches, so past eleven
/// workspaces, and for an agent running outside Clavis, this is the way to one.
[<Sealed>]
[<Description("Go to a workspace")>]
type SelectWorkspace() =
    do ()

/// One choice in an agent-driven selection popup (SelectionRequested). Value is what is returned when
/// chosen; Label is the row title shown to the user; Description is optional supporting text.
[<Sealed>]
type SelectionOption(value: string, label: string, description: string) =
    member _.Value = value
    member _.Label = label
    member _.Description = description

/// Ask the user to pick from a list via the selection popup (the agent's alternative to its built-in
/// ask-user tooling). RequestId correlates the answer; Prompt is the question shown above the input;
/// AllowFreeText permits an answer that is not in the list. Answered with SelectionCompleted - also on
/// dismissal, so the requester never hangs.
///
/// SessionId is the session the question belongs to, or Guid.Empty when it is not session-bound. Today the
/// agent-driven path always sends Guid.Empty: ask_user is an MCP tool and the gateway hosts one server with
/// no notion of which session called it. The field exists because this constructor is the single choke
/// point - once the gateway is session-aware it fills in here and nothing else has to move.
[<Sealed>]
type SelectionRequested
    (sessionId: Guid,
     requestId: Guid,
     prompt: string,
     options: IReadOnlyList<SelectionOption>,
     allowFreeText: bool) =

    member _.SessionId = sessionId
    member _.RequestId = requestId
    member _.Prompt = prompt
    member _.Options = options
    member _.AllowFreeText = allowFreeText

/// The user's answer to a SelectionRequested. Accepted is false when the popup was dismissed without a
/// choice (Value is then empty). SessionId echoes the request's, so an observer sees the pair.
[<Sealed>]
type SelectionCompleted(sessionId: Guid, requestId: Guid, accepted: bool, value: string) =
    member _.SessionId = sessionId
    member _.RequestId = requestId
    member _.Accepted = accepted
    member _.Value = value
