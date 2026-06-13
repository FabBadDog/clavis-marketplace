using System.Diagnostics.CodeAnalysis;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// Shared state the MCP tools resolve from the server's DI container: the live bus, the recent-activity
/// ring, the registry behind raw send, and the directories the read-only tools draw from. Registered as a
/// singleton so every tool invocation sees the same instance.
[ExcludeFromCodeCoverage(Justification = "Plain dependency holder wiring the integration shell together.")]
internal sealed class GatewayContext(
    IBus bus,
    ActivityRing activity,
    SendableMessages sendable,
    SelectionBroker selections,
    string pluginsDirectory,
    string logsDirectory)
{
    public IBus Bus { get; } = bus;

    public ActivityRing Activity { get; } = activity;

    public SendableMessages Sendable { get; } = sendable;

    /// Pending ask-the-user selections awaiting their SelectionCompleted answer.
    public SelectionBroker Selections { get; } = selections;

    /// <exeDir>/plugins - each subfolder ships the plugin's source including its CLAUDE.md.
    public string PluginsDirectory { get; } = pluginsDirectory;

    /// ~/.clavis/logs - per-launch log files written by the host's LogSink.
    public string LogsDirectory { get; } = logsDirectory;
}
