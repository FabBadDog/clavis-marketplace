---
name: events-panel
pluginId: EventsPanel
version: 1.0.2
apiVersion: 1.0.0
description: Raw bus-activity firehose with keyboard-first filters.
projectFile: ./EventsPanel.csproj
dependencies:
  - { name: session-contracts, version: 2 }
  - { name: host-contracts, version: 1 }
  - { name: keymap-contracts, version: 1 }
  - { name: workspace-contracts, version: 1 }
  - { name: clavis-rendering, version: 2 }
---

# EventsPanel

## Purpose

A dockable panel that shows the raw bus-activity firehose - every message that flows across the bus,
including deliberate `LogEntry` logs. Filtering is keyboard-first and minimal: a single severity floor
(`ALL` / `TRACE` / `DEBUG` / `INFO` / `WARN` / `ERROR`) chosen with a shared `SegmentedSelector`
(Left/Right), and a search that starts by simply typing - there is no search box. The search matches the
whole row (source, category, level, message, and continuation lines), so a source name or "error"/"warn"
filters without dedicated badges. The current query shows in the footer next to the `X of Y` count.
Rows are read-only: no selection, and content wraps in full rather than truncating. The list sticks to the
newest entry unless the user scrolls up (Shift+Up/Down scroll). One long-lived view model per window keeps
the accumulated history across closing and re-opening the panel.

## Location

`src/plugins/EventsPanel/` - a UI plugin (`UseWPF`). Registers the dockable panel kind `"events"`.

## Config (`EventsPanelConfig`)

- `MaxEntries` (default `10000`) - ring capacity; oldest entries are trimmed past this. Must be >= 100.
- `DefaultMinLevel` (default `Trace`) - initial severity floor. `Trace` shows as `ALL` (nothing hidden),
  so an unfiltered panel reads `N of N`; raise the floor to hide the high-frequency firehose.

## Messages published

- `PanelKindRegistration` - announces the `"events"` panel kind (re-announced on demand).
- `PanelCommandsRegistered` - registers its panel-scoped commands (severity left/right, scroll up/down)
  with the command palette; re-sent on request. Default bindings: Left/Right move the severity floor,
  Shift+Up/Down scroll. Typing drives search directly (no command), so the panel claims no character keys.
- `LogEntry` via `bus.LogInfo`.

## Messages subscribed

- `IBus.Activity` (the `ReplaySubject` firehose of all `BusActivity`) - subscribed via an `IObserver`, not a
  typed `Subscribe<T>`. This is the panel's data source.
- `PanelKindsRequested` - re-announces the panel kind so activation order vs. the registry never matters.
- `RequestPanelCommands` - re-sends `PanelCommandsRegistered`.

## Notes

- Threading: activity arrives on the publishing thread and is enqueued to a `ConcurrentQueue`; a 100 ms
  dispatcher timer drains it onto the UI thread in batches, so the firehose never floods the UI one
  `InvokeAsync` at a time.
- History lives in memory only and is bounded by `MaxEntries`. Closing/re-opening the panel reuses the same
  view model, so history survives within a window's lifetime.
- The filter state (severity floor + search text) IS persisted: the view restores it from the panel's
  per-instance saved-state blob on create and writes it back through `PanelInstanceContext.OnStateChanged`
  whenever it changes, so filters survive a restart (the host folds the blob into `workspace-layout.json`).
