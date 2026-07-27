namespace FabioSoft.Nucleus.Plugins.Conversation;

public sealed record ConversationConfig
{
    // Generous: the provider's boot (session-start hooks + every configured MCP server) has been
    // observed to take well over 90 seconds on an MCP-heavy setup, and a premature timeout closes the
    // init turn before its progress rows arrive.
    public int InitTimeoutSeconds { get; init; } = 240;

    // WorkingDirectory and Model used to live here. They are per workspace now: the Workspaces plugin owns
    // session creation, because the working directory is a property of the workspace, and each chat carries the
    // directory its workspace gave it.
}
