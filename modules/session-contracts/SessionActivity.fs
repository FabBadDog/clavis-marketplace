namespace FabioSoft.Contracts.Session

open System

/// The three activity states a session can be in, as seen from outside: is it doing anything, and does it
/// want something from the user? String literals rather than an enum so they cross load contexts and
/// round-trip through YAML without enum-identity concerns (the same pattern as KeymapScope).
[<RequireQualifiedAccess>]
module SessionActivity =

    /// No turn running and nothing wanted from the user.
    [<Literal>]
    let Idle = "idle"

    /// A turn is running: thinking, a tool, a hook, compacting, or retrying.
    [<Literal>]
    let Working = "working"

    /// Blocked on a human - a permission prompt or an ask-user selection is awaiting an answer.
    [<Literal>]
    let Waiting = "waiting"

/// A session's activity changed. Published on transitions only, so a consumer can hold the current state
/// per session rather than recomputing it. Detail is a short word for an overview ("thinking", a tool name,
/// "permission: Write"), empty when there is nothing useful to say; Since is when the state was entered, so
/// elapsed time renders without polling.
[<Sealed>]
type SessionActivityChanged(sessionId: Guid, activity: string, detail: string, since: DateTimeOffset) =
    member _.SessionId = sessionId
    member _.Activity = activity
    member _.Detail = detail
    member _.Since = since
