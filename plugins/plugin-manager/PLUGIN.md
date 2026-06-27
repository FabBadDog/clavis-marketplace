---
name: plugin-manager
pluginId: PluginManager
version: 1.0.8
apiVersion: 1.0.0
description: Runtime roster of activated plugins.
language: csharp
assemblyName: PluginManager
rootNamespace: FabioSoft.Nucleus.Plugins.PluginManager
useWpf: true
---

# PluginManager

## Purpose

Tracks the live roster of loaded plugins and lets them be unloaded at runtime. It listens to the
framework lifecycle messages and maintains an observable view-model of every plugin and its state
(`Active` / `Error`); when a plugin's unload command fires it sends `UnloadPlugin` for that id. It is the
runtime control surface for the plugin set.

## Location

`src/plugins/PluginManager/` - a `UseWPF` plugin. It builds a `PluginManagerViewModel`
(`INotifyPropertyChanged`, an `ObservableCollection` of plugin entries with `ActiveCount`/`TotalCount`),
but registers **no panel kind** and contributes to **no named region** - the view-model exists in memory
without a currently wired-up view.

## Config (`PluginManagerConfig`)

Empty record - no configurable fields.

## Messages published

- `UnloadPlugin` - sent (with a plugin id) when an entry's unload command is invoked.
- `LogEntry` - one info entry on activation.

## Messages subscribed

- `PluginActivated` - adds/updates the entry as `Active`.
- `PluginDeactivated` - removes the entry.
- `PluginError` - marks the entry `Error`.

All three are framework messages from `FabioSoft.Nucleus.Contracts`. View-model mutations are marshalled
onto the WPF dispatcher.

## Notes

- No persistence; state is rebuilt from lifecycle messages each launch.
- Does not itself enumerate plugins via `ListPlugins`/`PluginList`; it derives the roster purely from the
  `PluginActivated`/`PluginDeactivated`/`PluginError` stream, so it only knows about plugins whose
  lifecycle messages it observed.
