namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

/// AttachClavisMcp wires every spawned session to the in-process AgentGateway MCP server (and appends its
/// system-prompt primer) when ~/.clavis/agent-mcp.json exists, so the agent can introspect and operate the
/// Clavis environment it runs in. Set false to run sessions without that self-awareness.
/// DiscoveryTimeoutSeconds bounds the "what is running" query, which answers a request and so must not hang the
/// caller. HandOffTurnWaitSeconds is how long releasing an agent waits for a running turn to finish before
/// handing it back anyway - handing back restarts the process over the transcript, so an unfinished turn is
/// lost, but a wedged one must not block shutdown forever.
public sealed record ClaudeBridgeConfig(
    string WorkingDirectory = ".",
    string? Model = null,
    bool AttachClavisMcp = true,
    int DiscoveryTimeoutSeconds = 15,
    int HandOffTurnWaitSeconds = 30);
