namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// This plugin's own activation config. The *workspace list* is not here - it is durable user data in the
/// `Workspaces` section of `configuration.yaml` (see WorkspaceFile), which the plugin reads over the bus.
public sealed record WorkspacesConfig(
    /// Working directory for a workspace that names none; empty uses the directory Clavis was launched in.
    string DefaultWorkingDirectory = "",

    /// How often to ask what agents are running, which is what keeps the tabs for agents started outside Clavis
    /// current. Each poll costs a provider process, so this is a deliberate trade between freshness and cost
    /// rather than something to set as low as it will go. 0 disables discovery entirely: no fleet tabs appear,
    /// and a workspace whose agent was parked reopens its transcript instead of taking the agent over.
    int FleetPollSeconds = 15);
