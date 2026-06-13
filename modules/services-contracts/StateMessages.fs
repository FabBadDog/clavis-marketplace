namespace FabioSoft.Contracts.Services

open System.ComponentModel

// Runtime state is the sibling of configuration: the same per-plugin request/reply shape, but the store
// behind it is disposable. A plugin saves restorable runtime state (window bounds, docking layout, panel
// state) under its plugin id; deleting the state store loses no configuration, only the restored layout,
// so a plugin must start cleanly on StateNotFound exactly as it does on first run.

[<Sealed>]
[<Description("Request a plugin's stored runtime state")>]
type GetState(pluginId: string) =
    member _.PluginId = pluginId

[<AbstractClass>]
type StateResult() = class end

[<Sealed>]
type StateFound(pluginId: string, rawState: string) =
    inherit StateResult()

    member _.PluginId = pluginId
    member _.RawState = rawState

[<Sealed>]
type StateNotFound(pluginId: string) =
    inherit StateResult()

    member _.PluginId = pluginId

[<Sealed>]
[<Description("Save a plugin's runtime state")>]
type SaveState(pluginId: string, rawState: string) =
    member _.PluginId = pluginId
    member _.RawState = rawState
