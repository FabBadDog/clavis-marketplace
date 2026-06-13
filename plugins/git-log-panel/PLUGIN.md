---
name: git-log-panel
pluginId: GitLogPanel
version: 1.0.1
apiVersion: 1.0.0
description: Live git log dockable panel, refreshed on a timer.
projectFile: ./GitLogPanel.csproj
dependencies:
  - { name: workspace-contracts, version: 1 }
  - { name: clavis-controls, version: 1 }
---

# GitLogPanel

## Purpose

Registers the `git-log` dockable panel kind: a live `git log` view that lists recent commits and refreshes
on a timer. The panel runs `git` against the current working directory off the UI thread and re-renders
only when the top commit hash changes. It owns no message protocol beyond announcing its kind.

## Location

`src/plugins/GitLogPanel/` (`UseWPF`). Registers the panel kind string **`git-log`** (title "git log").

## Config (`GitLogPanelConfig`)

- `MaxCommits` (default `10`) - number of commits requested per `git log`; validated to be 1-100.
- `RefreshSeconds` (default `5`) - timer interval in seconds between refreshes; validated to be at least 1.

## Messages published

- `PanelKindRegistration` - announces the `git-log` kind (min size, title, `IsUserOpenable=true`, and a
  deferred view factory). Sent once on activation and re-sent on every `PanelKindsRequested`.
- `LogEntry` via `bus.LogInfo` on activation.

## Messages subscribed

- `PanelKindsRequested` - re-announces the `git-log` kind so the registry catches it regardless of
  activation order.

## Notes

- **`IsUserOpenable=true`** - offered as a user-openable panel (gets a `toggle-git-log` palette alias and
  shortcut).
- **Stateless** - the panel carries no per-instance state, so the `PanelInstanceContext` is unused and it
  never emits state to persist.
- **Refresh timer** is tied to the view's `Loaded`/`Unloaded`, so it survives docking-surface rebuilds
  (re-parenting) but stops when the panel is truly removed. A re-entrancy guard skips a tick while a
  refresh is still in flight; a faulted `git` call leaves the last good list in place.
- Runs `git` in `Directory.GetCurrentDirectory()`, off the UI thread; the continuation marshals back to
  the dispatcher to touch the visual tree.
