---
name: claude-bridge
pluginId: ClaudeBridge
version: 2.1.2
essential: true
apiVersion: 1.0.0
description: Wraps Claude sessions; maps stream events onto bus messages.
dependencies:
  - { name: session-contracts, version: 2 }
  - { name: editor-contracts, version: 1 }
  - { name: fabiosoft-claude, version: 2 }
language: csharp
assemblyName: ClaudeBridge
rootNamespace: FabioSoft.Nucleus.Plugins.ClaudeBridge
globalUsings:
  - FabioSoft.Contracts.Session
  - FabioSoft.Contracts.Editor
---

# ClaudeBridge

## Purpose

The concrete provider bridge for Claude Code. It wraps `FabioSoft.Claude` sessions (spawned `claude.exe`
processes with stream-json I/O) and maps their native `StreamEvent` DU onto the provider-neutral `Agent*`
message family, so UI plugins never name a provider. It owns the session registry, routes prompts /
permission responses / interrupts / disposal into the right session, and polls account-global usage on its
own cadence. Swapping in a different LLM means shipping another bridge that emits the same `Agent*` messages.

## Location

`src/plugins/ClaudeBridge/` - a non-UI C# plugin, compiled-on-launch. `StreamEventMapper.cs` does the
native-to-`Agent*` translation; `UsagePoller.cs` + `UsageReportMapping.cs` handle usage.

## Config (`ClaudeBridgeConfig`)

- `WorkingDirectory` (default `"."`) - default working directory (per-session directory comes from the
  `StartNewSession` message in practice).
- `Model` (default `null`) - default model; null lets the provider choose.
- `AttachClavisMcp` (default `true`) - when true, each spawned session is wired to the in-process
  AgentGateway MCP server and gets its system-prompt primer appended (see Notes).

## Messages published

- Session lifecycle: `SessionStarted`, `SessionReady`.
- Mapped stream events (the `Agent*` family, from `StreamEventMapper`): `AgentInit`,
  `AgentCommandsAvailable`, `AgentSessionEnded`, `AgentSessionAlreadyExited`, `AgentLogMessage`,
  `AgentApiCallRetry`, `AgentCompacting`, `AgentThinking`, `AgentToolUse`, `AgentToolResult`,
  `AgentTextDelta`, `AgentAssistant`, `AgentUsage`, `AgentResult`, `AgentHookStart`, `AgentHookComplete`,
  `AgentPermissionRequest`, `AgentAborted`, and `AgentParsingError` (errors).
- Usage: `AgentUsageReport` (carrying `AgentLimitWindow` entries), published by the usage poller.
- Capabilities + axis switching: `AgentCapabilities` (rich model/mode/effort catalog from `ClaudeCatalog`,
  on init and after every switch), `AgentModelChanged` / `AgentModeChanged` / `AgentEffortChanged`
  (confirmations after a `SetSession*` command was applied to the running session).
- `LogEntry` (diagnostics).

## Messages subscribed

- `StartNewSession`, `SendPrompt`, `SendPermissionResponse`, `InterruptSession`, `DisposeSession`.
- `SetSessionModel`, `SetSessionMode`, `SetSessionEffort` - runtime axis switches: validated against
  `ClaudeCatalog`, applied to the running session (`set_model` / `set_permission_mode` control requests;
  effort via the provider's non-interactive `/effort` command), then confirmed with the `Agent*Changed`
  events. A model switch coerces an effort the new model does not support onto the model's default.

## Notes

- **Rx duplex sessions.** Each session is an `ISubject` of `SessionInput` in / parsed `StreamEvent` out;
  OK results are mapped and sent, error results become `AgentParsingError`. On `AgentInit` it also emits
  `SessionReady`. `SessionFactory` and `UsageFetcher` are injectable so tests run without spawning
  processes or hitting the network.
- **Per-session resolvers.** A hook firing counter (for `AgentHook*` display names, from the user-global
  `~/.claude/settings.json` catalogue) and a working-directory-scoped permission resolver are bound into
  the mapper per session.
- **Clavis MCP attach.** When `AttachClavisMcp` is true, `ResolveClavisMcp` reads
  `~/.clavis/agent-mcp.json` (the mcp-config) and `~/.clavis/agent-primer.txt` (system-prompt primer),
  both written by AgentGateway, and attaches them to each new session's `SessionConfig`. Read per session
  (not at activation) so it never races gateway startup; absent files degrade to no attachment.
- **Usage is account-global**, independent of any session, polled on its own timer by `UsagePoller`.
