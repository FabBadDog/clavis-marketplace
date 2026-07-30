namespace FabioSoft.Contracts.Session

open System
open System.Collections.Generic
open System.ComponentModel

[<Sealed>]
[<Description("Start a new agent session")>]
type StartNewSession(sessionId: Guid, workingDirectory: string, model: string, name: string) =

    new(sessionId, workingDirectory, model) = StartNewSession(sessionId, workingDirectory, model, null)

    member _.SessionId = sessionId
    member _.WorkingDirectory = workingDirectory
    member _.Model = model

    /// A human label for the session (a workspace name), or null to let the bridge derive one from the working
    /// directory. It reaches the provider as the session's display name, which is what makes the agent
    /// recognisable in a listing when Clavis later looks for its own instances to reclaim.
    member _.Name = name

[<Sealed>]
[<Description("Send a prompt to a session")>]
type SendPrompt(sessionId: Guid, text: string) =
    member _.SessionId = sessionId
    member _.Text = text

/// Answer a pending permission request by the id of the chosen option. "allow" = allow once, "deny" =
/// deny; any other id names one of the request's AgentPermissionOption suggestions (an "always" choice),
/// which the bridge translates into the provider's updatedPermissions.
[<Sealed>]
[<Description("Answer a pending permission request")>]
type SendPermissionResponse(sessionId: Guid, requestId: string, optionId: string) =
    member _.SessionId = sessionId
    member _.RequestId = requestId
    member _.OptionId = optionId

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

/// The session never came up: it ended before reporting itself ready, so nothing can be sent to it.
///
/// Worth a message of its own because the failure is otherwise silent and total. A session that dies at launch
/// publishes no SessionReady, so nothing makes the prompt input appear and nothing renders an error - the user
/// is left with a chat that looks fine and cannot be typed into. Resuming a conversation the provider no longer
/// has is the way this actually happens: parking a session that never ran a turn leaves an id with no transcript
/// behind it, and resuming that id fails outright.
///
/// A consumer that asked for a specific conversation should stop asking for it and start a fresh one.
[<Sealed>]
type SessionStartFailed(sessionId: Guid, reason: string) =
    member _.SessionId = sessionId
    member _.Reason = reason

/// A permission prompt was answered. SessionId says which session it belongs to, so an observer can react
/// without assuming there is only one; the requester correlates by RequestId as before.
[<Sealed>]
type PermissionDecided(sessionId: Guid, requestId: string, decision: string) =
    member _.SessionId = sessionId
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

// --- Agent instances: discover, adopt, hand off ---
//
// A workspace's agent should outlive Clavis. Today the bridge spawns a child process per session and closing
// Clavis kills the work; the goal is that an agent keeps running while Clavis is shut and is picked back up on
// the next launch.
//
// Provider-neutral by design: no "claude", no "--bg", no pid appears in any of these. The facade stops assuming
// Clavis spawns and owns a process - obtaining an instance is one way among several, and lifecycle is the
// provider's business.
//
// The shape is constrained by what the CLI actually offers, verified rather than assumed: `--resume` does not
// join a session - it starts a new process over the persisted transcript, and the CLI refuses it outright
// while a background agent still holds that session. So resume is *take over*, never *join*: the agent is
// asked to stop, Clavis owns the stream while it is open, and hands the session back when it closes. The
// conversation survives that round trip; only one owner exists at a time.
//
// Two further verified facts shape the messages below, and both cost more than they look:
//
// A session Clavis holds is listed by the provider but cannot be taken over *from* the other side. Clavis
// streams over a print-mode session, which the listing reports as interactive and - decisively - without the
// short handle every take-over command needs. So visibility is symmetric while ownership is not: the other side
// can see a Clavis session but can only take it over after Clavis has handed it back.
//
// Handing a session back does not preserve its identity: the parked agent comes up under a *new* session id.
// Nothing may therefore persist an instance id expecting to find that same agent later, which is why reclaiming
// matches on what Clavis does control - the name it wrote and the directory it ran in - rather than on an id.

/// One agent instance the provider knows about, whether or not Clavis started it. InstanceId is the provider's
/// own identifier for it (opaque here). IsAdopted is true when this Clavis has taken it over. IsOwned is true
/// when Clavis started it in the first place - taking over an agent somebody started elsewhere is allowed, but
/// it stops a session they may still be watching, so the distinction is worth showing them.
[<Sealed>]
type AgentInstance
    (instanceId: string,
     name: string,
     workingDirectory: string,
     status: string,
     startedAt: DateTimeOffset,
     isAdopted: bool,
     isOwned: bool) =

    new(instanceId, name, workingDirectory, status, startedAt, isAdopted) =
        AgentInstance(instanceId, name, workingDirectory, status, startedAt, isAdopted, false)

    member _.InstanceId = instanceId
    member _.Name = name
    member _.WorkingDirectory = workingDirectory
    member _.Status = status
    member _.StartedAt = startedAt
    member _.IsAdopted = isAdopted
    member _.IsOwned = isOwned

/// Ask the provider bridge which agent instances exist. Answered with AgentInstancesAvailable, so a caller uses
/// IBus.Request.
[<Sealed>]
[<Description("List the agent instances that are running, including ones this Clavis did not start")>]
type AgentInstancesRequested() =
    do ()

[<Sealed>]
type AgentInstancesAvailable(instances: IReadOnlyList<AgentInstance>) =
    member _.Instances = instances

/// Take over an existing instance: the session continues in a Clavis-owned stream. Adoption is exclusive - two
/// Clavis homes adopting one instance would give two windows onto one transcript.
[<Sealed>]
[<Description("Take over an existing agent instance")>]
type AdoptAgentInstance(instanceId: string, sessionId: Guid, force: bool) =

    new(instanceId, sessionId) = AdoptAgentInstance(instanceId, sessionId, false)

    member _.InstanceId = instanceId
    member _.SessionId = sessionId

    /// Take the instance over even while it is mid-turn. Adoption stops the agent before resuming it, so taking
    /// over a working agent throws its unfinished turn away; by default the bridge therefore waits for the turn
    /// to end and reports AgentInstanceAdoptionWaiting meanwhile. Force is the user overriding that wait, which
    /// only they can decide - so it is never the bridge's own default.
    member _.Force = force

/// The instance cannot be taken over yet because it is still working, and Clavis is waiting for it to finish.
/// Published repeatedly while the wait lasts, so a consumer can show what it is waiting for without polling the
/// provider itself. Adoption still ends in AgentInstanceAdopted or AgentInstanceAdoptionFailed.
[<Sealed>]
type AgentInstanceAdoptionWaiting(sessionId: Guid, instanceId: string, status: string, waitedFor: TimeSpan) =
    member _.SessionId = sessionId
    member _.InstanceId = instanceId

    /// What the provider reports the instance is doing, so the wait can say why it is waiting.
    member _.Status = status

    /// How long the wait has lasted so far, so a consumer renders elapsed time without keeping its own clock.
    member _.WaitedFor = waitedFor

/// Resume a conversation that no live agent holds - the workspace's own session from a previous run, picked back
/// up from its persisted transcript. Distinct from AdoptAgentInstance, which takes over a *running* agent and so
/// has to stop it first: there is nothing here to stop, and requiring one would fail every resume of a session
/// whose agent is simply gone.
[<Sealed>]
[<Description("Resume a session from its persisted transcript")>]
type ResumeSession(sessionId: Guid, workingDirectory: string, agentSessionId: string, name: string) =
    member _.SessionId = sessionId
    member _.WorkingDirectory = workingDirectory

    /// The provider's own session id, which is what identifies the transcript to resume.
    member _.AgentSessionId = agentSessionId

    /// A human label for the session, as StartNewSession takes one, so a resumed agent keeps the name it is
    /// recognised by rather than reverting to a directory-derived one.
    member _.Name = name

/// Give an instance back. Mode is a ReleaseMode literal: hand it to the background so it keeps running, or stop
/// it outright.
[<Sealed>]
type ReleaseAgentInstance(sessionId: Guid, mode: string) =
    member _.SessionId = sessionId
    member _.Mode = mode

/// How a released instance should be left. String literals so they cross load contexts (same pattern as
/// SessionActivity).
[<RequireQualifiedAccess>]
module ReleaseMode =

    /// Hand the session back to a background agent, which carries on without Clavis.
    [<Literal>]
    let KeepRunning = "keep-running"

    /// End the session for good.
    [<Literal>]
    let Stop = "stop"

[<Sealed>]
type AgentInstanceAdopted(sessionId: Guid, instanceId: string) =
    member _.SessionId = sessionId
    member _.InstanceId = instanceId

/// Adoption did not happen and no session was started. Adoption is a hand-over - the running agent has to let go
/// of the conversation before Clavis can pick it up - so it can fail without anything being wrong, and a caller
/// waiting on AgentInstanceAdopted would otherwise wait forever.
[<Sealed>]
type AgentInstanceAdoptionFailed(sessionId: Guid, instanceId: string) =
    member _.SessionId = sessionId
    member _.InstanceId = instanceId

[<Sealed>]
type AgentInstanceReleased(instanceId: string, keptRunning: bool) =
    member _.InstanceId = instanceId
    member _.KeptRunning = keptRunning
