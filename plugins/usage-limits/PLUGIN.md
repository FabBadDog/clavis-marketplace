---
name: usage-limits
pluginId: UsageLimits
version: 1.0.1
apiVersion: 1.0.0
description: Token usage / pacing indicator.
dependencies:
  - { name: session-contracts, version: 2 }
  - { name: host-contracts, version: 1 }
  - { name: workspace-contracts, version: 1 }
  - { name: clavis-rendering, version: 2 }
language: csharp
assemblyName: UsageLimits
rootNamespace: FabioSoft.Nucleus.Plugins.UsageLimits
useWpf: true
---

# UsagePace

## Purpose

Surfaces the agent's usage-limit pace: a glyph contributed to the status bar (click to open the detail
panel) plus a dockable "usage-pace" panel. It reflects whatever account-global limit windows arrive on
`AgentUsageReport` - provider-neutral, never naming a provider or assuming a fixed window count. Between
reports a refresh tick keeps the "resets in" countdown and the time-elapsed axis moving against the wall
clock.

## Location

`src/plugins/UsagePace/` - a UI plugin (`UseWPF`). Registers the dockable panel kind `"usage-pace"` and
contributes a glyph into the `"status-bar-right"` UI region.

## Config (`UsagePaceConfig`)

- `RefreshSeconds` (default `30`) - how often the glyph and panel re-evaluate the countdown/pace between
  reports. The reported utilization only changes when a fresh `AgentUsageReport` arrives; this tick just
  animates the countdown. Must be >= 1.

## Messages published

- `UiRegionContribution` - contributes the pace glyph into the `"status-bar-right"` region.
- `PanelKindRegistration` - announces the `"usage-pace"` panel kind (re-announced on demand).
- `TogglePanel` - sent when the status-bar glyph is clicked, to open/close the detail panel.
- `LogEntry` via `bus.LogInfo`.

## Messages subscribed

- `AgentUsageReport` - the account-global usage windows; feeds the indicator's utilization/countdown state.
- `PanelKindsRequested` - re-announces the panel kind so activation order vs. the registry never matters.

## Notes

- Threading: the region contribution and the refresh timer are set up on the WPF dispatcher; report
  subscription runs on a bus thread and updates the shared `UsageIndicator`.
- No persistence - state is derived entirely from the latest `AgentUsageReport` plus the wall clock.

