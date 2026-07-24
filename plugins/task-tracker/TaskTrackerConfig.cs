namespace FabioSoft.Nucleus.Plugins.TaskTracker;

/// The tracker carries no user-facing settings - it reacts to the task stream and renders. An empty
/// config keeps the plugin shape uniform with the rest of the catalog (a record so equality is free).
public sealed record TaskTrackerConfig;
