namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// This plugin's own activation config. The *workspace list* is not here - it is durable user data in the
/// `Workspaces` section of `configuration.yaml` (see WorkspaceFile), which the plugin reads over the bus.
public sealed record WorkspacesConfig(
    /// Working directory for a workspace that names none; empty uses the directory Clavis was launched in.
    string DefaultWorkingDirectory = "");
