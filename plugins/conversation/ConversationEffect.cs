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

public sealed record StartNewSessionEffect(Guid SessionId) : ConversationEffect;

public sealed record ScheduleInitTimeoutEffect(Guid SessionId) : ConversationEffect;

/// Relay the active session's permission mode to the host (as PromptModeChanged) so it can dress the
/// prompt input in the mode's ambient accent. DisplayName is the mode's short label for the input tag.
public sealed record PublishPromptModeEffect(string Mode, string DisplayName) : ConversationEffect;
