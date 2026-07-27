using System;

namespace FabioSoft.Nucleus.Plugins.Conversation;

public abstract record ConversationEffect;

public sealed record SendPromptEffect(Guid SessionId, string Text) : ConversationEffect;

public sealed record SendPermissionResponseEffect(
    Guid SessionId,
    string RequestId,
    string OptionId) : ConversationEffect;

public sealed record InterruptSessionEffect(Guid SessionId) : ConversationEffect;

public sealed record DisposeSessionEffect(Guid SessionId) : ConversationEffect;

/// Start a session for a chat. Carries the chat's working directory, because it is per workspace now - the
/// plugin no longer has one global directory to fall back on.
public sealed record StartNewSessionEffect(Guid SessionId, string WorkingDirectory) : ConversationEffect;

public sealed record ScheduleInitTimeoutEffect(Guid SessionId) : ConversationEffect;

/// Dress the prompt input in the session's permission-mode accent. DisplayName is the mode's short label for
/// the input tag. The prompt lives in the chat panel, so this lands on the chat view models rather than on the
/// window host.
public sealed record PublishPromptModeEffect(string Mode, string DisplayName) : ConversationEffect;
