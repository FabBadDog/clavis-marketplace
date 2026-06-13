---
name: conversation
pluginId: Conversation
version: 7.1.0
essential: true
apiVersion: 1.0.0
description: The elm/flux conversation state, update, and view models.
projectFile: ./Conversation.csproj
dependencies:
  - { name: session-contracts, version: 2 }
  - { name: host-contracts, version: 1 }
  - { name: keymap-contracts, version: 1 }
  - { name: placeholders-contracts, version: 1 }
  - { name: services-contracts, version: 1 }
  - { name: workspace-contracts, version: 1 }
  - { name: clavis-placeholders, version: 1 }
  - { name: clavis-rendering, version: 2 }
  - { name: clavis-controls, version: 1 }
  - { name: fabiosoft-common, version: ^1.0.0 }
  - { name: yamldotnet, version: 1 }
---

# Conversation

## Purpose

The elm/flux conversation: it is the chat itself. A pure core (`ConversationState` + `ConversationUpdate`
returning `(state, effects[])`) holds all turn/timing/permission logic with no side effects, and an impure
shell (`ConversationPlugin`) subscribes to bus messages, calls the pure update, executes the resulting
effects as bus sends, and projects state onto WPF ViewModels. It translates a user prompt into a session
`SendPrompt`, drives session lifecycle, and renders the provider-neutral `AgentStreamEvent` family into the
conversation view via the shared `MarkdownPresenter`.

## Location

`src/plugins/Conversation/` - a **UI plugin** (`UseWPF`), compiled-on-launch. It contributes into
WpfHost's `main-content`, `title-bar-right`, and `status-bar` regions.

## Config (`ConversationConfig`)

- `InitTimeoutSeconds` (default `90`) - how long to wait for a new session to initialise before treating
  init as failed.
- `WorkingDirectory` (default `null`) - working directory for Claude sessions; null uses the process
  current directory (the folder Clavis was launched in).
- `Model` (default `null`) - model override passed to `StartNewSession`; null lets the provider default.

## Messages published

- Session control (from pure effects): `SendPrompt`, `SendPermissionResponse`, `InterruptSession`,
  `DisposeSession`, `StartNewSession`.
- UI: `UiRegionContribution` (main-content, title-bar-right, status-bar).
- Permission relay: `PermissionDecided` (re-published from the ViewModel's permission callback).
- `LogEntry` (diagnostics).

## Messages subscribed

- Agent stream: `AgentStreamEvent` (the whole family), `AgentParsingError`.
- User input: `UserSubmittedPrompt`, `UserAborted`, `UserCancelledQueued`.
- Permission + lifecycle: `PermissionDecided`, `FullRestartRequested`.

## Notes

- **Pure core, impure shell.** Every bus handler locks shared state, runs the matching
  `ConversationUpdate.Handle*`, then applies the returned effects (each mapped 1:1 to a session bus send).
  Effect types (`SendPromptEffect`, `StartNewSessionEffect`, `ScheduleInitTimeoutEffect`, ...) are
  internal and never hit the bus directly.
- **UI-thread bound.** ViewModel creation, template loading, and a `DispatcherTimer` elapsed-time tick all
  run on `Application.Current.Dispatcher`; a failed cosmetic tick is logged, not fatal.
- Typed slash-style commands (exit, restart) are command-palette concerns, not handled here - a submitted
  prompt is always a prompt for the agent.
- **Model/mode/effort indicators.** `AgentCapabilities` carries the rich axis catalogs (model display
  name/version/context size/description, color-coded efforts, modes) into the session state; the
  `Agent*Changed` confirmations update the current values. `AgentValues` projects display names onto the
  `agent.modelName`/`agent.effortName`/`agent.modeName` placeholders (raw ids stay on
  `agent.model`/`agent.mode`/`agent.effort`), and the `PlaceholderStrip` animates a segment whose value
  changed - so a confirmed switch is visibly acknowledged in the title-bar cluster and status line.
