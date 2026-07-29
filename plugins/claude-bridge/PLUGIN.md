---
name: claude-bridge
pluginId: ClaudeBridge
version: 2.4.0
essential: true
apiVersion: 1.0.0
description: Wraps Claude sessions; maps stream events onto bus messages.
dependencies:
  - { name: session-contracts, version: 3 }
  - { name: editor-contracts, version: 1 }
  - { name: fabiosoft-claude, version: 5 }
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
- `DiscoveryTimeoutSeconds` (default `15`) - bounds the "what is running" query, which answers a request
  and so must not hang the caller.
- `HandOffTurnWaitSeconds` (default `30`) - how long releasing an agent waits for a running turn before
  handing it back anyway.
- `AdoptStopTimeoutSeconds` (default `20`) - how long adopting an agent waits for it to stop. The CLI
  refuses to resume a session while its agent still holds it, so adoption cannot proceed until it has.

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
- Agent instances: `AgentInstancesAvailable` (answering a request), `AgentInstanceAdopted`,
  `AgentInstanceAdoptionFailed` (the agent would not let go, so no session was started),
  `AgentInstanceReleased`.

## Messages subscribed

- `StartNewSession`, `SendPrompt`, `SendPermissionResponse`, `InterruptSession`, `DisposeSession`.
- `AgentInstancesRequested`, `AdoptAgentInstance`, `ReleaseAgentInstance` - the instance lifecycle
  (see Notes).
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
- **Agent instances outlive Clavis.** Every session Clavis starts is named `clavis/<label>` (the workspace
  name, else the working directory's last segment). The marker is a *label*, surfaced as `IsOwned` so the UI
  can say which agents are Clavis's own - it is not a permission. An agent started in the CLI's own agent
  view can be taken over too.
- **Adoption is a hand-over, and the gate is `kind`.** `--resume` starts a second process over a transcript
  rather than joining the first, and the CLI refuses it outright while a background agent still holds the
  session. So adoption first runs `claude stop <agent-id>`, and only resumes once the agent has let go; the
  conversation survives the stop. That is why *background* agents are safe targets and interactive sessions
  are filtered out of the listing entirely: stopping somebody's terminal is not a hand-over. A listing that
  stops reporting `kind` yields no targets rather than treating every terminal as one.
- **Adoption is exclusive**, enforced by `AgentInstanceRegistry` and claimed *before* the spawn: a refused
  claim costs nothing, two owners corrupt the transcript. This guards one Clavis home; two homes on one
  machine share the provider's session store and would need out-of-band state to coordinate.
- **Releasing waits for the turn.** `TurnGate` tracks whether a turn is in flight (started on `SendPrompt`,
  cleared on `AgentResult`). Handing back restarts the process over the persisted transcript, so an
  unfinished turn is lost; the release waits `HandOffTurnWaitSeconds` for it, then proceeds anyway and logs
  the loss. The owned stream is always disposed *before* the background agent is spawned - the two must
  never overlap on one session id.
- **Nothing releases automatically.** `DisposeSession` still ends an agent outright; keeping one alive
  requires an explicit `ReleaseAgentInstance(keep-running)`. Making shutdown hand every session back would
  leave detached agents on the machine that nobody is tracking, so that policy is deliberately not the
  default and belongs to whoever owns the workspace lifecycle.
