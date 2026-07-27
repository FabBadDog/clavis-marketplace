---
name: conversation
pluginId: Conversation
version: 8.0.0
essential: true
apiVersion: 1.0.0
description: The elm/flux conversation state, update, and view models.
dependencies:
  - { name: session-contracts, version: 3 }
  - { name: host-contracts, version: 3 }
  - { name: keymap-contracts, version: 1 }
  - { name: placeholders-contracts, version: 1 }
  - { name: services-contracts, version: 1 }
  - { name: layout-contracts, version: 2 }
  - { name: clavis-placeholders, version: 1 }
  - { name: clavis-rendering, version: 2 }
  - { name: clavis-controls, version: 1 }
  - { name: fabiosoft-common, version: ^1.0.0 }
  - { name: yamldotnet, version: 1 }
language: csharp
assemblyName: Conversation
rootNamespace: FabioSoft.Nucleus.Plugins.Conversation
useWpf: true
globalUsings:
  - FabioSoft.Contracts.Session
  - FabioSoft.Contracts.Host
  - FabioSoft.Contracts.Keymap
  - FabioSoft.Contracts.Placeholders
  - FabioSoft.Contracts.Services
  - FabioSoft.Contracts.Layout
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

`src/plugins/Conversation/` - a **UI plugin** (`UseWPF`), compiled-on-launch. It registers the `chat` panel
kind (`Views/ChatPanelView.cs`, which owns the chat output *and* the prompt input) and contributes into
WpfHost's `title-bar-left`, `title-bar-right`, and `status-bar` chrome regions.

## Config (`ConversationConfig`)

- `InitTimeoutSeconds` (default `90`) - how long to wait for a new session to initialise before treating
  init as failed.
- `WorkingDirectory` (default `null`) - working directory for Claude sessions; null uses the process
  current directory (the folder Clavis was launched in).
- `Model` (default `null`) - model override passed to `StartNewSession`; null lets the provider default.

## Messages published

- Session control (from pure effects): `SendPrompt`, `SendPermissionResponse`, `InterruptSession`,
  `DisposeSession`, `StartNewSession`.
- UI: `PanelKindRegistration` (`chat`, one-per-workspace, and `status-line-editor`),
  `UiRegionContribution` (title-bar-left, title-bar-right, status-bar), `StatusBarAvailability`.
- Permission keys (from the chat panel while a decision is pending): `UserNavigatedPermission`,
  `UserConfirmedPermission`.
- Permission relay: `PermissionDecided` (re-published from the ViewModel's permission callback).
- `LogEntry` (diagnostics).

## Messages subscribed

- Agent stream: `AgentStreamEvent` (the whole family), `AgentParsingError`.
- User input: `UserSubmittedPrompt`, `UserAborted`, `UserCancelledQueued`.
- Permission + lifecycle: `PermissionDecided`, `UserNavigatedPermission`, `UserConfirmedPermission`,
  `FullRestartRequested`.
- Panels: `PanelKindsRequested`, `PanelKindRegistration` (to learn other kinds'' chrome), `ActivePanelChanged`,
  `RequestPanelCommands`, `FocusInputRequested` (the prompt lives here, so this panel answers it).

## Notes

- **Pure core, impure shell.** Every bus handler locks shared state, runs the matching
  `ConversationUpdate.Handle*`, then applies the returned effects (each mapped 1:1 to a session bus send).
  Effect types (`SendPromptEffect`, `StartNewSessionEffect`, `ScheduleInitTimeoutEffect`, ...) are
  internal and never hit the bus directly.
- **UI-thread bound.** ViewModel creation, template loading, and a `DispatcherTimer` elapsed-time tick all
  run on `Application.Current.Dispatcher`; a failed cosmetic tick is logged, not fatal.
- Typed slash-style commands (exit, restart) are command-palette concerns, not handled here - a submitted
  prompt is always a prompt for the agent.
- **The chat is a panel, and it owns its prompt.** `ChatPanelView` is the whole `chat` kind: the turn list
  with the prompt input (`PromptInput`) floating over its bottom edge, so the input travels, closes and
  reopens with the chat instead of being window chrome. Its two pure parts are tested on their own -
  `ChatPanelState` (the `{workspaceId, chatId}` per-instance blob, so a restored panel re-attaches instead of
  being re-seeded, and an unreadable blob yields a fresh chat) and `PromptHistory` (the Up/Down recall rules).
- **The permission keys are the panel's, not the keymap's.** While a decision is pending the chat panel takes
  bare Left/Right/Enter at its own root, ahead of the prompt box's Enter-to-submit. They are deliberately not
  keymap bindings: a binding on bare Enter for kind `chat` would fire unconditionally, and the keymap has no
  scope for "only while this instance is blocked", so submitting a prompt would break.
- **Model/mode/effort indicators.** `AgentCapabilities` carries the rich axis catalogs (model display
  name/version/context size/description, color-coded efforts, modes) into the session state; the
  `Agent*Changed` confirmations update the current values. `AgentValues` projects display names onto the
  `agent.modelName`/`agent.effortName`/`agent.modeName` placeholders (raw ids stay on
  `agent.model`/`agent.mode`/`agent.effort`), and the `PlaceholderStrip` animates a segment whose value
  changed - so a confirmed switch is visibly acknowledged in the title-bar cluster and status line.
