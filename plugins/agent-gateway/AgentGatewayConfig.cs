namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// Configuration for the in-process MCP server the agent connects back to. It listens on a per-launch
/// named pipe (ACL'd to the current user) reached only through the stdio bridge, so there is no address,
/// port, or other transport knob to configure.
public sealed record AgentGatewayConfig;
