---
name: settings
pluginId: Settings
version: 3.0.0
apiVersion: 1.0.0
description: Settings surface.
projectFile: ./Settings.csproj
---

# Settings

## Purpose

Intended as the per-plugin settings UI/store: reflect each plugin's config type into editable
properties (name, type, description, default) and present them grouped by plugin. Currently a
**scaffold/stub** - the view-model and config reflector exist and are unit-testable, but the plugin
shell is not yet wired to any config message flow, so at runtime it only logs activation and does not
surface or persist settings.

## Location

`src/plugins/Settings/` - UI plugin (`UseWPF`). Registers no panel kind (no UI is contributed into a
region or panel yet).

## Config (`SettingsConfig`)

Empty record - no configurable fields.

## Messages published

- `LogEntry` (constructed directly and `bus.Send`) - the activation notice.

## Messages subscribed

- `PluginActivated` - subscribed but the handler is currently a no-op (the intended hook for collecting
  each plugin's config type into the view-model).

## Notes

- `ConfigReflector.Reflect(configType)` reads public readable properties via reflection, pulling the
  `[Description]` attribute text and the default value (instantiates the type with its parameterless
  constructor). It maps common CLR types to friendly names (`string`, `int`, `double`, `bool`,
  `TimeSpan`).
- `SettingsViewModel` holds an `ObservableCollection<PluginConfigViewModel>` (per-plugin, expandable);
  `AddPluginConfig` is the entry point a finished shell would call from the `PluginActivated` handler.
- No persistence, no config read/write messages, and no XAML view are wired yet. Treat this plugin as
  incomplete when reasoning about live settings behaviour.
