using System;

namespace FabioSoft.Nucleus.Plugins.Conversation;

public abstract record ConversationEffect;

public sealed record SendPromptEffect(Guid SessionId, string Text) : ConversationEffect;

public sealed record SendPermissionResponseEffect(
    Guid SessionId,
    string RequestId,
    bool Allow) : ConversationEffect;

public sealed record InterruptSessionEffect(Guid SessionId) : ConversationEffect;

public sealed record DisposeSessionEffect(Guid SessionId) : ConversationEffect;

public sealed record StartNewSessionEffect(Guid SessionId) : ConversationEffect;

public sealed record ScheduleInitTimeoutEffect(Guid SessionId) : ConversationEffect;
