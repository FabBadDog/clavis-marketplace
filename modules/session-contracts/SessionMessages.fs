namespace FabioSoft.Contracts.Session

open System
open System.ComponentModel

[<Sealed>]
[<Description("Start a new agent session")>]
type StartNewSession(sessionId: Guid, workingDirectory: string, model: string) =
    member _.SessionId = sessionId
    member _.WorkingDirectory = workingDirectory
    member _.Model = model

[<Sealed>]
[<Description("Send a prompt to a session")>]
type SendPrompt(sessionId: Guid, text: string) =
    member _.SessionId = sessionId
    member _.Text = text

[<Sealed>]
[<Description("Answer a pending permission request")>]
type SendPermissionResponse(sessionId: Guid, requestId: string, allow: bool) =
    member _.SessionId = sessionId
    member _.RequestId = requestId
    member _.Allow = allow

[<Sealed>]
[<Description("Interrupt a running session")>]
type InterruptSession(sessionId: Guid) =
    member _.SessionId = sessionId

/// Ask the provider bridge to switch the session to another model (by the internal id from
/// AgentCapabilities.Models). The bridge applies it and confirms with AgentModelChanged plus a fresh
/// AgentCapabilities - UI plugins react to the confirmation, never to this command.
[<Sealed>]
[<Description("Switch the agent session to another model")>]
type SetSessionModel(sessionId: Guid, model: string) =
    member _.SessionId = sessionId
    member _.Model = model

/// Ask the provider bridge to switch the session's permission/operation mode (by the internal id from
/// AgentCapabilities.Modes). Confirmed with AgentModeChanged.
[<Sealed>]
[<Description("Switch the agent session's mode")>]
type SetSessionMode(sessionId: Guid, mode: string) =
    member _.SessionId = sessionId
    member _.Mode = mode

/// Ask the provider bridge to switch the session's reasoning effort (by the internal id from
/// AgentCapabilities.Efforts; must be supported by the current model). Confirmed with AgentEffortChanged.
[<Sealed>]
[<Description("Switch the agent session's reasoning effort")>]
type SetSessionEffort(sessionId: Guid, effort: string) =
    member _.SessionId = sessionId
    member _.Effort = effort

[<Sealed>]
[<Description("Dispose an agent session")>]
type DisposeSession(sessionId: Guid) =
    member _.SessionId = sessionId

[<Sealed>]
type SessionStarted(sessionId: Guid) =
    member _.SessionId = sessionId

[<Sealed>]
type SessionReady(sessionId: Guid, agentSessionId: string, model: string) =
    member _.SessionId = sessionId
    member _.AgentSessionId = agentSessionId
    member _.Model = model

[<Sealed>]
type ConversationStateChanged
    (
        model: string,
        statusText: string,
        contextSize: int,
        contextFilled: int,
        queuedCount: int,
        hasActiveTurn: bool
    ) =

    member _.Model = model
    member _.StatusText = statusText
    member _.ContextSize = contextSize
    member _.ContextFilled = contextFilled
    member _.QueuedCount = queuedCount
    member _.HasActiveTurn = hasActiveTurn

[<Sealed>]
type PermissionDecided(requestId: string, decision: string) =
    member _.RequestId = requestId
    member _.Decision = decision

[<Sealed>]
[<Description("Restart the application")>]
type FullRestartRequested() =
    do ()

[<Sealed>]
[<Description("The Clavis MCP server is available to the in-Clavis agent: the mcp-config JSON to attach and the system-prompt guide describing its tools, so ClaudeBridge attaches both inline to each session instead of reading them from disk")>]
type ClavisMcpAvailable(configJson: string, guide: string) =
    member _.ConfigJson = configJson
    member _.Guide = guide

/// A provider-agnostic one-shot summarization request behind the agent facade: summarize Text into at most
/// MaxLength characters. Sent via IBus.Request; the provider bridge answers with SummaryResult. Generic on
/// purpose so any caller (commit messages, notifications, ...) can reuse it, not just one feature.
[<Sealed>]
[<Description("Summarize text to at most a maximum character count (provider-agnostic agent one-shot)")>]
type Summarize(text: string, maxLength: int) =
    member _.Text = text
    member _.MaxLength = maxLength

/// The reply to a Summarize request. Summary is empty when the bridge could not produce one, so the caller
/// falls back to its own text.
[<Sealed>]
type SummaryResult(summary: string) =
    member _.Summary = summary
