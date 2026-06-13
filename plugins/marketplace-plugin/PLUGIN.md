---
name: marketplace-plugin
pluginId: MarketplacePlugin
version: 1.0.63
apiVersion: 1.0.0
description: Interactive marketplace surface - register marketplaces, search, and install/update/uninstall plugins.
projectFile: ./MarketplacePlugin.csproj
dependencies:
  - { name: session-contracts, version: 2 }
  - { name: marketplace-contracts, version: 1 }
---

# MarketplacePlugin

The marketplace client's post-boot interactive surface. It handles the `FabioSoft.Contracts.Marketplace`
bus messages and performs the operations against `~/.clavis` via the F# `Installer`/`Catalog` engine, driving
the framework's `LoadPlugin`/`UnloadPlugin` to bring installed plugins up (or take them down) live.

## Messages

- `ListMarketplaces` -> `MarketplaceList`, `SearchMarketplace` -> `MarketplaceSearchResult`
- `AddMarketplace` / `RemoveMarketplace`
- `InstallPlugin` / `UpdatePlugin` / `UninstallPlugin`
- progress via `MarketplaceProgress` / `MarketplaceCompleted` / `MarketplaceFailed`; a module
  install emits `RestartRequired` (modules enter the Default ALC only at boot).

## Lifecycle pipeline (live development)

A debounced `WorkingCopyWatcher` (`FileSystemWatcher`) over each working copy's `plugins/` and `shared/`
groups runs `LifecyclePipeline` whenever an item changes on disk - the single mechanism behind both
triggers, since a developer edit and a Clavis self-edit are both on-disk writes. Per change, in order, each
step gating the next:

1. recompile the item (gate; a failure leaves the running plugin untouched),
2. run its unit tests then the integration tests (`TestRunner`; gate only - missing tests warn, failing
   tests stop the run),
3. update `PLUGIN.md` from the new public surface (`LifecycleMetadata`: reflect, diff, semver bump),
4. reload in place via `ReloadPlugin` (a source plugin) or emit `RestartRequired` (a module),
5. commit (`GitSource.commitAll`, always local),
6. push best-effort (`AutoPush`; a rejected push keeps the local commit - "the marketplace doesn't allow it").

Runs are serialized through one gate, and an item is suppressed while its pipeline runs (and briefly after)
so the pipeline's own `PLUGIN.md`/`surface.json` writes do not retrigger it. Config: `WatchForChanges`,
`AutoPush` (both default true). The in-place reload (`ReloadPlugin`, handled by the Kernel) recompiles into
a staging dir and loads from there, so it works even when a realized WPF plugin's collectible context
cannot be collected; that old context then lingers, deactivated, until the next restart.

## How it's built

Host infrastructure, not marketplace content: it ships in the bundle and compiles on launch against the
host-provided assemblies beside the exe (the framework contracts, the Marketplace contract group, and the
`Marketplace.Io`/`Core` engine), so it declares no marketplace dependencies.
