---
name: panel-registry
pluginId: PanelRegistry
version: 1.1.0
essential: true
apiVersion: 1.0.0
description: Catalogs panel kinds and routes open/restore into panel instances.
dependencies:
  - { name: workspace-contracts, version: 1 }
language: csharp
assemblyName: PanelRegistry
rootNamespace: FabioSoft.Nucleus.Plugins.PanelRegistry
globalUsings:
  - FabioSoft.Contracts.Workspace
---

# PanelRegistry

## Purpose

Catalogs the dockable panel kinds that panel plugins announce, and routes open/restore requests into
ready-to-place panel instances for the host. It is a pure router: it owns no windows and no persistence.
On activation it broadcasts a request so panel plugins announce their kinds regardless of activation
order, then resolves each `OpenPanel`/`RestorePanel` into a concrete instance the host places.

## Location

`src/plugins/PanelRegistry/` - a non-UI plugin (no `UseWPF`). Registers no panel kind of its own; it is
the registry the other panel plugins announce into. View construction is deferred into a `Func` the host
invokes on its UI thread, so this plugin carries no WPF dependency.

## Config (`PanelRegistryConfig`)

Empty - `PanelRegistryConfig` is a parameterless record with no fields. Always validates as
`ConfigValid`.

## Messages published

- `PanelKindsRequested` - broadcast once on activation so panel plugins (re-)announce their kinds.
- `PanelInstanceReady` - sent for each successfully resolved `OpenPanel`/`RestorePanel`, carrying the
  instance id, kind, title, min size, and a deferred view factory for the host to place.
- `PanelStateChanged` - relayed when a placed panel reports new per-instance state (via the state
  callback wired into each instance), so the host can persist it.
- `LogEntry` via `bus.LogInfo`/`bus.LogWarn` (registration confirmations; a warning when an unknown kind
  is requested).

## Messages subscribed

- `PanelKindRegistration` - a panel plugin announcing a kind; added to the in-memory catalog.
- `OpenPanel` - open a fresh instance of a kind (new `Guid`, empty saved state).
- `RestorePanel` - re-materialise a saved instance (carries `InstanceId` and `SavedState`).
- `PanelClosed` - drop the instance from the open-instance map.

## Notes

- The catalog is an **instance** field, never a static registry, so the plugin's collectible
  AssemblyLoadContext can unload.
- No persistence and no windows - the host owns layout persistence and placement; this plugin only maps
  kind -> ready instance.
- `OpenPanel` mints a new `Guid` per open; `RestorePanel` reuses the saved id and seeds saved state.
- An `OpenPanel`/`RestorePanel` for an unregistered kind is dropped with a logged warning (no message
  sent).
