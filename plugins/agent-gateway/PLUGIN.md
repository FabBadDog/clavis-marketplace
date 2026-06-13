---
name: agent-gateway
pluginId: AgentGateway
version: 1.1.140
apiVersion: 1.0.0
description: In-process MCP server over a named pipe for the agent.
dependencies:
  - { name: workspace-contracts, version: 1 }
  - { name: host-contracts, version: 1 }
  - { name: session-contracts, version: 2 }
---

# AgentGateway

## Purpose

Gives the in-Clavis agent (the child `claude.exe` spawned by ClaudeBridge) live awareness and control of
the Clavis environment it runs in. It hosts an in-process **MCP server** over a **named pipe** and exposes
tools the agent calls to introspect (loaded plugins, the log, the bus firehose, what is on screen) and to
operate the UI (open panels, submit prompts, send whitelisted messages). On activation it also publishes
its mcp-config and a system-prompt guide on the bus (`ClavisMcpAvailable`) that ClaudeBridge attaches
inline to every session, so this is on by default.

## Location

`src/plugins/AgentGateway/` - a **source** plugin like every other: shipped as source and compiled on
launch. Its MCP/DI NuGet deps (`ModelContextProtocol`/`.Core`, `Microsoft.Extensions.DependencyInjection`)
are private to the plugin - restored by the compile-on-launch build into its own output and resolved by
its collectible `AssemblyLoadContext`, not shipped by the host. Hosts the MCP server over a named pipe via
`StreamServerTransport` - no Kestrel, no ASP.NET.

## Transport (named pipe + stdio bridge)

The MCP server listens on a per-launch named pipe (`clavis-mcp-<guid>`) whose DACL grants only the current
user (no other user or lower-integrity process can open it; not a TCP port, so invisible to browser/web
content). The agent reaches it through `FabioSoft.NamedPipeStdioBridge` - a generic, app-agnostic
stdin/stdout-to-named-pipe byte pump shipped beside the exe. The generated mcp-config is a `stdio` server
(`{"command": "<exeDir>/FabioSoft.NamedPipeStdioBridge.exe", "args": ["<pipe>"]}`), so the agent spawns the
bridge as its own MCP child and speaks MCP over its stdio; the bridge pumps bytes to the pipe. One
pipe-server instance is created per connection, so multiple windows/sessions each get their own.

ClaudeBridge pre-allows the `mcp__clavis` server (`SessionConfig.AllowedTools`) for every attached session,
so the agent never has to approve introspecting/driving its own host. This is safe precisely because the
transport is a user-scoped pipe, not an open loopback port.

## Config (`AgentGatewayConfig`)

Empty - the transport is a per-launch, user-ACL'd named pipe, so there is no address, port, or other knob.

## Messages published

- On tool calls (control): `UserSubmittedPrompt`, `OpenPanel`, `TogglePanel`, `CloseActivePanel`,
  `FocusInputRequested`, `SelectionRequested` (the `ask_user` tool's popup, answered by the Selection
  plugin), plus any whitelisted type sent via the `send_message` tool (see `SendableMessages`).
- Requests (request/response): `ListPlugins` (-> `PluginList`) and `WorkspaceSnapshotRequested`
  (-> `WorkspaceSnapshot`).
- `ClavisMcpAvailable` (`FabioSoft.Contracts.Session`) once on activation: the mcp-config JSON and
  the system-prompt guide ClaudeBridge attaches inline to each session.
- `LogEntry` via `bus.LogInfo`/`LogError`.

## Messages subscribed

- Observes the whole `IBus.Activity` stream into a bounded ring for the `recent_activity` tool.
- Transient response subscriptions to `PluginList` and `WorkspaceSnapshot` (created by `IBus.Request`).
- `SelectionCompleted` - resolves the `ask_user` tool's pending question via the `SelectionBroker`
  (manual correlation rather than `IBus.Request`, because a human answer easily outlives the bus's
  default request timeout).

## Tools exposed (MCP)

`clavis_architecture`, `list_plugins`, `describe_plugin`, `workspace_snapshot`, `read_log`,
`recent_activity`, `submit_prompt`, `open_panel`, `toggle_panel`, `close_active_panel`, `focus_input`,
`ask_user`, `send_message`. `describe_plugin` reads each plugin's deployed `CLAUDE.md`; `read_log` tails
the newest `~/.clavis/logs/clavis-*.log`; `ask_user` shows the Selection plugin's popup and waits (up to
10 minutes) for the user's pick - the system-prompt guide steers the agent to prefer it over its built-in
AskUserQuestion tool.

## Notes

- **Normal, unloadable plugin.** `System.IO.Pipes` roots no statics (unlike Kestrel/ASP.NET), so the
  plugin's collectible `AssemblyLoadContext` unloads cleanly. Activation starts the pipe accept-loop;
  disposal cancels it and tears down the DI provider. `AgentGateway` and `UnloadPlugin` remain on the
  `send_message` deny-list so the agent cannot unload its own gateway mid-call.
- **`send_message` is gated.** Only navigation-level messages are registered; lifecycle/teardown messages
  (`ApplicationShutdown`, `CloseWindow`, `DisposeSession`, `UnloadPlugin`, ...) are denied. `IBus.Send`
  dispatches by the compile-time type, so each registry entry constructs its concrete type.
- Publishes `ClavisMcpAvailable` (the mcp-config JSON + system-prompt guide) on the bus during
  `ActivateAsync`, before any session starts; ClaudeBridge caches it and attaches both inline (no file on
  disk) when `AttachClavisMcp` is true, and pre-allows `mcp__clavis`. The bus bootstrap buffer replays the
  announcement to ClaudeBridge regardless of activation order.
- The agent-facing architecture text and MCP guide live in `ClavisDocs.cs` (not the repo `CLAUDE.md`, which
  is human build/style guidance).
