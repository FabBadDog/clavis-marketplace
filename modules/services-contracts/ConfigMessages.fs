namespace FabioSoft.Contracts.Services

open System.ComponentModel

[<Sealed>]
[<Description("Request a plugin's stored configuration")>]
type GetConfig(pluginId: string) =
    member _.PluginId = pluginId

[<AbstractClass>]
type ConfigResult() = class end

[<Sealed>]
type ConfigFound(pluginId: string, rawConfig: string) =
    inherit ConfigResult()

    member _.PluginId = pluginId
    member _.RawConfig = rawConfig

[<Sealed>]
type ConfigNotFound(pluginId: string) =
    inherit ConfigResult()

    member _.PluginId = pluginId

[<Sealed>]
[<Description("Save a plugin's configuration")>]
type SaveConfig(pluginId: string, rawConfig: string) =
    member _.PluginId = pluginId
    member _.RawConfig = rawConfig

[<Sealed>]
type ConfigSaved(pluginId: string) =
    member _.PluginId = pluginId

[<Sealed>]
type ConfigChanged(pluginId: string, rawConfig: string) =
    member _.PluginId = pluginId
    member _.RawConfig = rawConfig
