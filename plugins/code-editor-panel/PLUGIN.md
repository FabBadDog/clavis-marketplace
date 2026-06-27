---
name: code-editor-panel
pluginId: CodeEditorPanel
version: 2.0.0
apiVersion: 1.0.0
description: Code editor panel with file browser and editor context.
dependencies:
  - { name: workspace-contracts, version: 1 }
  - { name: editor-contracts, version: 1 }
  - { name: fabiosoft-editor, version: 1 }
  - { name: clavis-controls, version: 1 }
language: csharp
assemblyName: CodeEditorPanel
rootNamespace: FabioSoft.Nucleus.Plugins.CodeEditorPanel
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Workspace
---

# CodeEditorPanel

## Purpose

Registers the `code-editor` dockable panel kind: a file browser (left) paired with a full code editor
(right, the shared `CodeEditor` / AvalonEdit control). Files are opened from the tree, edited with syntax
highlighting, and saved with Ctrl+S (direct `System.IO`, off the UI thread). The open file, caret, and
selection are published as `EditorStateChanged` so the IdeBridge can relay them to the agent and any UI
can show the open file. The panel also opens files on request via `OpenFileInEditor`.

## Location

`src/plugins/CodeEditorPanel/` (`UseWPF`). Registers the panel kind string **`code-editor`** (title
"code"), `IsUserOpenable=true` (gets a `toggle-code-editor` palette alias and shortcut).

## Config (`CodeEditorPanelConfig`)

- `RootPath` (default empty -> the directory Clavis was launched in) - root of the file browser.
- `ShowHiddenFiles` (default `false`) - include hidden/system entries in the tree.

## Per-instance state

The open file path, persisted as a small JSON blob through `PanelInstanceContext.OnStateChanged` and
restored on launch (the file reopens if it still exists).

## Messages published

- `PanelKindRegistration` - announces the `code-editor` kind. Sent on activation and on every
  `PanelKindsRequested`.
- `EditorStateChanged` - on open and (debounced ~150ms) on caret/selection/text change.
- `LogEntry` via `bus.LogInfo` on activation.

## Messages subscribed

- `PanelKindsRequested` - re-announces the kind regardless of activation order.
- `OpenFileInEditor` - opens the given file (and optionally reveals a line), scoped to the view's
  load lifetime.

## Notes

- **Widget hosting:** the editor control lives in `FabioSoft.Editor` (a standalone, application-neutral
  library loaded into the Default ALC from `<exeDir>/shared`) because AvalonEdit is
  DependencyProperty/static-registry heavy; the plugin references only the facade and so stays
  collectible/reloadable.
- **Ctrl+S** saves; **Esc** is swallowed only while there are unsaved changes, so the panel-scoped close
  gesture cannot discard edits mid-flight.
- File reads/writes and per-directory enumeration run off the UI thread; the continuation marshals back to
  the dispatcher to touch the editor and tree.
- **Pure seam:** `FileTree.Order` / `FileTree.IsHidden` hold the tree's ordering and visibility logic with
  no IO, and are unit-tested; the view models and view are WPF/IO and `[ExcludeFromCodeCoverage]`.
