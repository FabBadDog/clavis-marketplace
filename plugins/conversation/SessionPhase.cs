namespace FabioSoft.Nucleus.Plugins.Conversation;

/// The lowercase phase word shown as the active turn's rail whisper, mapped from the session status Clavis
/// already tracks. Only the live "working" phases have a word; every other status (idle, ready, ended,
/// aborting, ...) maps to empty so the whisper simply hides. The animated ellipsis is added by the view -
/// this returns the bare word ("thinking"), never the dots.
public static class SessionPhase
{
    public static string Whisper(SessionStatus status) => status switch
    {
        SessionStatus.Thinking => "thinking",
        SessionStatus.Retrying => "retrying",
        SessionStatus.Compacting => "compacting",
        _ => ""
    };
}
