---
name: markdown-panel
pluginId: MarkdownPanel
version: 1.1.0
apiVersion: 1.0.0
description: User-authored markdown panels with live placeholders, plus a manager to create and edit them.
dependencies:
  - { name: workspace-contracts, version: 1 }
  - { name: services-contracts, version: 1 }
  - { name: placeholders-contracts, version: 1 }
  - { name: clavis-placeholders, version: 1 }
  - { name: clavis-rendering, version: 2 }
  - { name: clavis-controls, version: 1 }
  - { name: yamldotnet, version: 1 }
language: csharp
assemblyName: MarkdownPanel
rootNamespace: FabioSoft.Nucleus.Plugins.MarkdownPanel
useWpf: true
globalUsings:
  - FabioSoft.Nucleus.Contracts
  - FabioSoft.Contracts.Workspace
  - FabioSoft.Contracts.Services
  - FabioSoft.Contracts.Placeholders
---

# MarkdownPanel

## Purpose

Lets the user define lightweight custom panels that display information. Each **definition** is a title +
a markdown body that may embed placeholder tokens (`{git.branch}`, `{agent.name:uppercase}`,
`{bar:agent.contextPercent}`, ...). Every definition is its own dockable panel kind (`markdown:{id}`) that
renders its body with placeholders resolved **live** - the rendered output updates as values change while
the panel is shown. Definitions are created, edited, renamed, and deleted from the **Markdown Panels**
manager (kind `markdown-panels`).

Two panel kinds:

- **`markdown-panels`** (manager, `IsUserOpenable=true`): a list of definitions with New / Save / Open /
  Delete, a body editor with placeholder IntelliSense (the shared `PlaceholderEditor`), and a live resolved
  preview. Being user-openable it gets a `toggle-markdown-panels` palette command and a default shortcut.
- **`markdown:{id}`** (display, `IsUserOpenable=false`): a display-only panel for one definition, opened
  from the manager. Not offered in the generic panel picker.

Per-definition kinds fit the host's singleton-per-kind model: several definitions can be docked at once,
re-opening one focuses its existing panel, and the layout persists *which* definition is docked where via
the slot's kind.

## Storage

Definitions are durable config in the plugin's `configuration.yaml` section (a `panels:` list of
`{ id, title, body }`), loaded and saved through the Configuration round-trip and (de)serialized with
YamlDotNet (`MarkdownPanelFile`). First run seeds one example definition. The body is the single source of
truth - editing it updates every open panel bound to that definition.

## Messages published

- `PanelKindRegistration` - the manager kind and one display kind per definition. Re-sent on every
  `PanelKindsRequested` and after the catalog changes.
- `GetConfig` / `SaveConfig` - load on activation; persist on seed and on every create/edit/delete.
- `PlaceholdersRequested` - on activation, to fill placeholder values immediately.
- `OpenPanel` - opens a definition's display panel (from the manager's Open action).
- `ClosePanel` - closes a deleted definition's open display panels.
- `SetPanelTitle` - retitles a renamed definition's open display tabs.
- `LogEntry` via `bus.LogInfo` / `bus.LogWarn`.

## Messages subscribed

- `ConfigResult` / `ConfigChanged` - load the definitions (seed the starter on `ConfigNotFound`).
- `PanelKindsRequested` - re-announce all kinds (activation-order independent).
- `PlaceholderSnapshot` - merge live values and re-render open panels + the manager preview.
- `RegisterPlaceholderProvider` - collect descriptors for the manager's IntelliSense.
- `PanelClosed` - drop the closed instance from the live-view maps.

## Notes

- Display panels set `MarkdownPresenter.Animate = false`: the body re-renders as values tick, and an
  entrance animation on every render would flicker.
- Bus callbacks marshal WPF updates to the dispatcher; the live-view and value maps are concurrent.
- A stale saved layout that references a deleted definition's kind restores a closeable "loading..."
  placeholder (the kind never registers); normal deletes close their open panels first, so this is rare.
