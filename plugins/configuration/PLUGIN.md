---
name: configuration
pluginId: Configuration
version: 1.0.138
essential: true
apiVersion: 1.0.0
description: Sectioned YAML config and state stores under ~/.clavis.
projectFile: ./Configuration.csproj
dependencies:
  - { name: services-contracts, version: 1 }
---

# Configuration

## Purpose

The per-plugin config and state store. It answers `GetConfig`/`GetState` requests by loading a plugin's
section and applies `SaveConfig`/`SaveState` requests by writing that section back (broadcasting that a
config changed). There are two backing files, each a single YAML mapping of named sections (one section per
plugin, keyed by plugin id):

- **`configuration.yaml`** - durable user configuration (each plugin's settings, plus the host-owned
  `marketplace` and `logging` sections).
- **`state.yaml`** - disposable runtime state the app writes (window bounds, docking layout, per-panel
  state). Deleting it loses no configuration, only the restored layout.

Each plugin still owns the YAML *inside* its section: the contract carries it as a `RawConfig`/`RawState`
string and the owning plugin (de)serializes it (via YamlDotNet). The store only splices that document in and
out of the section keyed by plugin id - it does not interpret the section's contents.

## Location

`plugins/configuration/` - a non-WPF plugin with no UI (no panel kind, no region).

## Config (`ConfigurationConfig`)

- `ConfigurationFilePath` (default `~/.clavis/configuration.yaml`) - the sectioned configuration store.
- `StateFilePath` (default `~/.clavis/state.yaml`) - the sectioned state store.
- Both must be non-empty (`ConfigInvalid` otherwise); their directory is created on write.

## Messages published

- `ConfigFound` / `ConfigNotFound` (both `ConfigResult`) - reply to `GetConfig`, depending on whether the
  plugin's section is present in `configuration.yaml`.
- `StateFound` / `StateNotFound` (both `StateResult`) - reply to `GetState` against `state.yaml`.
- `ConfigSaved` - acknowledgement after a successful `SaveConfig`.
- `ConfigChanged` (with the new `RawConfig`) - broadcast after a config save so interested plugins re-read.
- `LogEntry` - one info entry on activation.

## Messages subscribed

- `GetConfig` / `SaveConfig` (`FabioSoft.Contracts.Services`) - load/persist the `PluginId` section
  of `configuration.yaml`, then emit `ConfigSaved` + `ConfigChanged` on save.
- `GetState` / `SaveState` - load/persist the `PluginId` section of `state.yaml`. No change broadcast: a
  plugin's state is owned by that plugin alone.

## Notes

- **Sectioned, not raw passthrough.** Unlike the old per-file layout, the store parses the file as a YAML
  mapping and reads/writes a single section, so the whole configuration (or all state) lives in one file.
  A plugin's section value round-trips through YamlDotNet's representation model (comments are not
  preserved).
- **Shared writer with the host.** The host writes the `marketplace` and `logging` sections of the same
  `configuration.yaml` directly (the bootstrap runs before the bus exists). Writes open the file
  exclusively with retry and read-merge-write under that lock, so the two writers never clobber each
  other's sections.
- Missing file or missing section -> `ConfigNotFound` / `StateNotFound` (not an error); the caller then
  uses its `DefaultConfig` or starts with a default layout.
