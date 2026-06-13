namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

/// AttachClavisMcp wires every spawned session to the in-process AgentGateway MCP server (and appends its
/// system-prompt primer) when ~/.clavis/agent-mcp.json exists, so the agent can introspect and operate the
/// Clavis environment it runs in. Set false to run sessions without that self-awareness.
public sealed record ClaudeBridgeConfig(
    string WorkingDirectory = ".",
    string? Model = null,
    bool AttachClavisMcp = true);
