namespace FabioSoft.Contracts.Keymap

open System.Collections.Generic
open System.ComponentModel

/// The keymap scope a binding belongs to. A focused panel's bindings shadow application bindings, which
/// shadow system bindings (most-specific wins). Modelled as string constants so they round-trip cleanly
/// through YAML and cross AssemblyLoadContext boundaries without enum-identity concerns.
[<RequireQualifiedAccess>]
module KeymapScope =

    [<Literal>]
    let System = "system"

    [<Literal>]
    let Application = "application"

    [<Literal>]
    let Panel = "panel"

/// A routable command the keymap and palette know about. Name is the full command string the palette
/// router accepts (a message type name, an alias, or a slash command). IsBindable is true when the
/// command needs no mandatory arguments, so a bare gesture can invoke it.
[<Sealed>]
type CommandDescriptor
    (name: string, displayName: string, kind: string, source: string, description: string, isBindable: bool) =

    member _.Name = name
    member _.DisplayName = displayName
    member _.Kind = kind
    member _.Source = source
    member _.Description = description
    member _.IsBindable = isBindable

/// Broadcast by the command palette whenever its catalog changes (aliases load, agent commands arrive).
/// The keymap, the help overlay, and the management panel consume it to know which commands exist and
/// which are bindable.
[<Sealed>]
type CommandsAvailable(commands: IReadOnlyList<CommandDescriptor>) =
    member _.Commands = commands

/// Asks the command palette to re-broadcast its catalog. Lets consumers that activate after the palette
/// still receive the current command list (mirrors PanelKindsRequested).
[<Sealed>]
type RequestCommands() =
    do ()

/// One key binding. Gesture is a canonical chord string (e.g. "Ctrl+Shift+P"); Scope is a KeymapScope
/// value; PanelKind names the panel a panel-scoped binding applies to (empty for system/application).
[<Sealed>]
type KeyBinding(gesture: string, command: string, scope: string, panelKind: string) =
    member _.Gesture = gesture
    member _.Command = command
    member _.Scope = scope
    member _.PanelKind = panelKind

/// Broadcast by the KeyMap plugin with the full resolved binding set on load and after every change.
/// The host resolves gestures against this snapshot; the overlay and palette read it to display bindings.
[<Sealed>]
type KeymapChanged(bindings: IReadOnlyList<KeyBinding>) =
    member _.Bindings = bindings

/// Asks the KeyMap plugin to re-broadcast its bindings (late-subscriber pattern).
[<Sealed>]
type RequestKeymap() =
    do ()

/// Assign or change a binding. The KeyMap plugin validates (warning on same-scope duplicates), persists,
/// and re-broadcasts KeymapChanged.
[<Sealed>]
type SetKeyBinding(command: string, scope: string, panelKind: string, gesture: string) =
    member _.Command = command
    member _.Scope = scope
    member _.PanelKind = panelKind
    member _.Gesture = gesture

/// Remove the binding identified by its gesture within a scope (and panel kind for panel scope).
[<Sealed>]
type RemoveKeyBinding(gesture: string, scope: string, panelKind: string) =
    member _.Gesture = gesture
    member _.Scope = scope
    member _.PanelKind = panelKind

/// Execute a command-string. The command palette handles it through its existing router, giving the
/// keymap a single execution path that covers messages, aliases, and agent commands alike.
[<Sealed>]
[<Description("Run a palette command by its command string")>]
type RunCommand(commandLine: string) =
    member _.CommandLine = commandLine

/// Execute a panel-local command on a specific panel instance (the focused panel the host resolved a
/// panel-scoped gesture against). The owning panel instance handles it; others ignore it.
[<Sealed>]
type RunPanelCommand(instanceId: System.Guid, command: string) =
    member _.InstanceId = instanceId
    member _.Command = command

/// A panel plugin registers the panel-scoped commands it can execute, so the help overlay and the
/// shortcut-management panel can describe and offer them. The command palette aggregates these into
/// CommandsAvailable. They are not palette-executable (only RunPanelCommand runs them).
[<Sealed>]
type PanelCommandsRegistered(commands: IReadOnlyList<CommandDescriptor>) =
    member _.Commands = commands

/// Asks panel plugins to (re-)register their panel-scoped commands (late-subscriber pattern).
[<Sealed>]
type RequestPanelCommands() =
    do ()
