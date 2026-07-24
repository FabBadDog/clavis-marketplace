---
name: task-tracker
pluginId: TaskTracker
version: 1.0.0
apiVersion: 1.0.0
description: Background-task tracker in the status bar - a live count and list of the agent's subagent/background tasks.
dependencies:
  - { name: session-contracts, version: 2 }
  - { name: host-contracts, version: 1 }
language: csharp
assemblyName: TaskTracker
rootNamespace: FabioSoft.Nucleus.Plugins.TaskTracker
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Session
  - FabioSoft.Contracts.Host
---

# TaskTracker

## Purpose

Surfaces the agent's background tasks - the subagents and backgrounded commands it spawns - which Clavis
otherwise has no way to show. Renders a borderless count line and a live task list into the
`status-bar-right` region: a running task shows a breathing outlined ring, and flips to a green check with
its status when it finishes, lingering briefly before it fades out. When no tasks are active the whole
strip collapses to nothing.

## Location

`plugins/task-tracker/` (`UseWPF`). Contributes into the host region **`status-bar-right`** (no dockable
panel kind, no persistence).

## Messages subscribed

- `AgentStreamEvent` - matches the two task leaves: `AgentTaskStarted` adds/updates a running entry,
  `AgentTaskCompleted` flips the matching entry (by `TaskId`) to done and shows its summary.

## Messages published

- `UiRegionContribution` - contributes the tracker control into `status-bar-right` on activation.
- `LogEntry` via `bus.LogInfo` on activation.

## Notes

- **Pure core**: `TaskTrackerModel` holds the task-list transitions (started / completed / remove) as pure
  functions over an immutable `TaskEntry` list; the plugin and view are the impure shell.
- **Correlation** is by `TaskId` (the provider's stream carries it on both the start and the notification).
  A notification with no prior start still surfaces as a done entry.
- **Linger**: a completed task stays visible for a few seconds so its result is readable, then is removed
  and the strip re-renders (collapsing when the last task clears).
