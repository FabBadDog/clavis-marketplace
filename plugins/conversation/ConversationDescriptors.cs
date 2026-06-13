namespace FabioSoft.Nucleus.Plugins.Conversation;

/// The placeholder catalog the Conversation plugin announces (the `agent.*` and `turn.*` namespaces).
public static class ConversationDescriptors
{
    public static IReadOnlyList<PlaceholderDescriptor> All { get; } =
    [
        new("agent.model", "value", "claude-opus-4-8", "Current model (internal id)"),
        new("agent.modelName", "value", "Opus 4.8", "Current model display name"),
        new("agent.mode", "value", "plan", "Permission mode internal id (default/plan/auto/...)"),
        new("agent.modeName", "value", "Plan", "Permission mode display name"),
        new("agent.effort", "value", "xhigh", "Reasoning effort internal id"),
        new("agent.effortName", "value", "Extra High", "Reasoning effort display name"),
        new("agent.status", "value", "Ready", "Session status"),
        new("agent.contextUsed", "value", "128000", "Context tokens used"),
        new("agent.contextWindow", "value", "200000", "Context window size in tokens"),
        new("agent.contextUsedShort", "value", "128k", "Context tokens used, short form"),
        new("agent.contextWindowShort", "value", "200k", "Context window, short form"),
        new("agent.contextPercent", "value", "64", "Context used as a percent (0-100)"),
        new("agent.queued", "value", "0", "Queued prompts"),
        new("agent.thinkingTokens", "value", "0", "Live reasoning-token estimate"),
        new("turn.runtime", "value", "8.2s", "Duration of the current or last turn"),
        new("turn.tokens", "value", "3400", "Token count of the current or last turn"),
        new("turn.status", "value", "Running", "Status of the current or last turn"),
    ];
}
