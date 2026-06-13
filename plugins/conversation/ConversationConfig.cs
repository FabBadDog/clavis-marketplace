namespace FabioSoft.Nucleus.Plugins.Conversation;

public sealed record ConversationConfig
{
    // Generous: the provider's boot (session-start hooks + every configured MCP server) has been
    // observed to take well over 90 seconds on an MCP-heavy setup, and a premature timeout closes the
    // init turn before its progress rows arrive.
    public int InitTimeoutSeconds { get; init; } = 240;

    /// Working directory for Claude sessions. Null means use the process's current directory
    /// (the directory Clavis was launched in), matching the old Shell behaviour.
    public string? WorkingDirectory { get; init; }

    public string? Model { get; init; }
}
