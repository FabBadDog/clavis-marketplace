---
name: selection
pluginId: Selection
version: 2.1.0
apiVersion: 1.0.0
description: Selection popups on the shared SelectorWindow: model, effort, mode, panel and workspace pickers, and the agent-driven ask-the-user selection.
dependencies:
  - { name: host-contracts, version: 3 }
  - { name: session-contracts, version: 3 }
  - { name: layout-contracts, version: 2 }
  - { name: workspace-contracts, version: 2 }
  - { name: clavis-rendering, version: 3 }
language: csharp
assemblyName: Selection
rootNamespace: FabioSoft.Nucleus.Plugins.Selection
useWpf: true
globalUsings:
  - FabioSoft.Nucleus.Contracts
  - FabioSoft.Contracts.Host
  - FabioSoft.Contracts.Session
  - FabioSoft.Contracts.Layout
  - FabioSoft.Contracts.Workspace
resources:
  - Views/SelectionTemplates.xaml
---

# Selection

## Purpose

Owns the selection popups built on the shared `SelectorWindow` (clavis-rendering): the **model**,
**effort**, and **mode** pickers for the active agent session, the **panel** picker, and the agent-driven
**ask-the-user** selection. Provider-neutral by construction: every choice list comes from the provider
bridge's `AgentCapabilities` broadcast (rich `AgentModelInfo`/`AgentEffortInfo`/`AgentModeInfo` entries),
so a different agent bridge automatically populates different choices. A pick emits a bus message and
nothing else - the visible indicators update only when the bridge confirms the change
(`AgentModelChanged`/`AgentModeChanged`/`AgentEffortChanged` feed the Conversation placeholders, which the
`PlaceholderStrip` animates).

- The **model picker** shows display name, version, context size, and description - never the internal id.
- The **effort picker** offers only the levels the current model supports, color coded with a short
  description ("xhigh" surfaces as "Extra High").
- The **mode picker** offers the provider's modes (for Claude: Plan, Auto, Edit, None).
- The **panel picker** lists every user-openable panel kind and opens the chosen one.
- The **workspace picker** lists every workspace and activates the chosen one - including the ones the bar
  leaves off, which is why it exists.
- The **ask-the-user selection** (`SelectionRequested`, used by the AgentGateway's `ask_user` MCP tool)
  shows the agent's question and options, optionally allowing a free-text answer, and always answers with
  `SelectionCompleted` - also on dismissal, so the requester never hangs.

## Location

`plugins/selection/` - a UI plugin (`UseWPF`). Owns no panel kind; it shows transient popups.

## Config (`SelectionConfig`)

- `SelectorWidth` (default `720`) - popup width in DIPs. Must be between 200 and 1200.

## Messages published

- `SetSessionModel`, `SetSessionMode`, `SetSessionEffort` - when a model/effort/mode pick differs from the
  current value.
- `OpenPanel` - when a panel is picked.
- `ActivateWorkspace` - when a workspace is picked.
- `RequestWorkspaces` - on activation, so the picker is populated even if the list was broadcast first.
- `SelectionCompleted` - the answer to a `SelectionRequested` (accepted or dismissed).
- `PanelKindsRequested` - on activation, so panel kinds re-announce regardless of order.
- `LogEntry` via `bus.LogInfo`.

## Messages subscribed

- `SelectModel`, `SelectEffort`, `SelectMode`, `SelectPanel`, `SelectWorkspace` - open the respective picker (these are
  string-constructible host-contract messages, so the command palette and keymap offer them for free).
- `SelectionRequested` - the agent-driven ask-the-user popup.
- `AgentStreamEvent` - matches `AgentCapabilities` to cache the active session's axes and choice catalogs.
- `PanelKindRegistration` - caches user-openable kinds for the panel picker.
- `WorkspaceListChanged` - the whole list, replaced wholesale, for the workspace picker.

## Notes

- Popups are created per invocation on the WPF dispatcher; the pure row building/filtering lives in
  `SelectionRows` (unit-tested), the popup interaction in the shared `SelectorWindow`.
- **The workspace picker (`SelectWorkspace`, Ctrl+Shift+O) lists more than the bar does**, which is the point
  of it: the bar shows only what an F-key reaches, so a workspace past the eleven slots would otherwise have no
  visible way in at all. Rows keep the bar's own order (slot order, slotless last) so it reads as a superset of
  the strip rather than a differently-sorted second list, and an agent running outside Clavis says so instead
  of showing a working directory. A pick only sends `ActivateWorkspace` - promoting a fleet agent, obtaining
  its session and everything else is the owner's business, exactly as for a click on a tab.
- The effort rows resolve the provider's neutral color hint ("green", "accent", ...) onto host theme brush
  keys (`SelectionRows.BrushKeyFor`).
