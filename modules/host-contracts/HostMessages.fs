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

/// The Conversation plugin announces whether a permission prompt is awaiting a decision, so the host can
/// route Left/Right/Enter to it - and only then - without knowing anything about permission internals.
[<Sealed>]
type PermissionPending(pending: bool) =
    member _.Pending = pending

/// The conversation owner announces whether prompts can be accepted (the agent session is up). The window
/// host keeps the prompt input collapsed until the first availability, so the user is never offered an
/// input that leads nowhere while the agent infrastructure is still coming up - and it stays a host/
/// conversation concern: the host learns no session vocabulary.
[<Sealed>]
type PromptInputAvailability(available: bool) =
    member _.Available = available

/// A panel asks the host to return keyboard focus to the prompt input (keyboard-first navigation).
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

/// Open the panel selector popup: every user-openable panel kind, opening the chosen one.
/// Handled by the Selection plugin.
[<Sealed>]
[<Description("Select and open a panel")>]
type SelectPanel() =
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
[<Sealed>]
type SelectionRequested
    (requestId: Guid, prompt: string, options: IReadOnlyList<SelectionOption>, allowFreeText: bool) =

    member _.RequestId = requestId
    member _.Prompt = prompt
    member _.Options = options
    member _.AllowFreeText = allowFreeText

/// The user's answer to a SelectionRequested. Accepted is false when the popup was dismissed without a
/// choice (Value is then empty).
[<Sealed>]
type SelectionCompleted(requestId: Guid, accepted: bool, value: string) =
    member _.RequestId = requestId
    member _.Accepted = accepted
    member _.Value = value
