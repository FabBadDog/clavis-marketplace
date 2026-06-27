---
name: markdown-panel
pluginId: MarkdownPanel
version: 1.0.1
apiVersion: 1.0.0
description: Editable markdown note dockable panel.
dependencies:
  - { name: workspace-contracts, version: 1 }
  - { name: clavis-rendering, version: 2 }
language: csharp
assemblyName: MarkdownPanel
rootNamespace: FabioSoft.Nucleus.Plugins.MarkdownPanel
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Workspace
---

# MarkdownPanel

## Purpose

Registers the `markdown` dockable panel kind: an editable markdown note. It renders through the shared
`MarkdownPresenter` and flips to a plain-text editor on double-click or the pencil affordance; Ctrl+Enter
saves and Esc cancels. The note text is the panel's per-instance state, seeded from the saved blob and
written back through the panel context so the host persists it.

## Location

`src/plugins/MarkdownPanel/` (`UseWPF`). Registers the panel kind string **`markdown`** (title
"markdown").

## Config (`MarkdownPanelConfig`)

- `DefaultTemplate` (default `"# Notes\n"`) - seed text used for a new instance when there is no saved
  state.

## Messages published

- `PanelKindRegistration` - announces the `markdown` kind (min size, title, `IsUserOpenable=false`, and a
  deferred view factory). Sent once on activation and re-sent on every `PanelKindsRequested`.
- `LogEntry` via `bus.LogInfo` on activation.

The note text is **not** published directly: on save the view calls `context.OnStateChanged`, and the
PanelRegistry turns that into the `PanelStateChanged` bus message for the host to persist.

## Messages subscribed

- `PanelKindsRequested` - re-announces the `markdown` kind so the registry catches it regardless of
  activation order.

## Notes

- **`IsUserOpenable=false`** - a previously-docked note still restores from a saved layout, but the kind
  is not offered as something to open (no toggle command, no shortcut) until markdown-note template
  management exists. Flip the flag to `true` once that lands.
- **Per-instance state is the note text** - seeded from `PanelInstanceContext.SavedState` (falling back to
  `DefaultTemplate`), pushed back via `context.OnStateChanged` on each Ctrl+Enter save.
- Editing is in-view only: double-click or the hover pencil glyph enters edit mode; Ctrl+Enter saves and
  re-renders, Esc cancels without persisting.
