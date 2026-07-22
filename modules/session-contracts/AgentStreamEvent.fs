namespace FabioSoft.Contracts.Session

open System
open System.Collections.Generic

/// Streaming events emitted by an LLM agent session. These are the provider-neutral facade: a concrete
/// provider bridge (e.g. ClaudeBridge) translates its native events into this family, so a future
/// provider can be swapped in by emitting the same messages. Subscribers bind to the base type and
/// match the concrete case; the bus dispatches under the base type identity (single shared identity
/// across plugin load contexts). Modelled as a class hierarchy so the C# plugin consumers keep their
/// existing `is`/`switch` patterns and named property access.
[<AbstractClass>]
type AgentStreamEvent(sessionId: Guid) =
    member _.SessionId = sessionId

[<Sealed>]
type AgentInit(sessionId: Guid, agentSessionId: string, model: string, slashCommands: IReadOnlyList<string>) =
    inherit AgentStreamEvent(sessionId)

    member _.AgentSessionId = agentSessionId
    member _.Model = model
    member _.SlashCommands = slashCommands

[<Sealed>]
type AgentCommand(name: string, description: string, argumentHint: string) =
    member _.Name = name
    member _.Description = description
    member _.ArgumentHint = argumentHint

/// The agent's available commands, reported by the provider's startup handshake (no turn required).
/// Carries name, description and argument hint so the command palette can list them with descriptions.
[<Sealed>]
type AgentCommandsAvailable(sessionId: Guid, commands: IReadOnlyList<AgentCommand>) =
    inherit AgentStreamEvent(sessionId)

    member _.Commands = commands

[<Sealed>]
type AgentSessionEnded(sessionId: Guid, exitCode: int, detail: string) =
    inherit AgentStreamEvent(sessionId)

    member _.ExitCode = exitCode
    member _.Detail = detail

[<Sealed>]
type AgentSessionAlreadyExited(sessionId: Guid) =
    inherit AgentStreamEvent(sessionId)

[<Sealed>]
type AgentLogMessage(sessionId: Guid, text: string) =
    inherit AgentStreamEvent(sessionId)

    member _.Text = text

[<Sealed>]
type AgentApiCallRetry(sessionId: Guid) =
    inherit AgentStreamEvent(sessionId)

[<Sealed>]
type AgentCompacting(sessionId: Guid) =
    inherit AgentStreamEvent(sessionId)

[<Sealed>]
type AgentThinking(sessionId: Guid, summary: string) =
    inherit AgentStreamEvent(sessionId)

    member _.Summary = summary

/// A running estimate of the reasoning tokens spent on the current thinking burst, reported by the
/// provider as it thinks. Drives a live "thinking - N tokens" indicator.
[<Sealed>]
type AgentThinkingTokens(sessionId: Guid, estimatedTokens: int) =
    inherit AgentStreamEvent(sessionId)

    member _.EstimatedTokens = estimatedTokens

/// A provider rate-limit notification (e.g. the rolling window's status and reset time). Provider-neutral
/// and account-global, but carried with a session id so the conversation can surface it inline; the usage
/// indicator reads the reset/limit signal. LimitType is the provider's window name (e.g. "five_hour").
[<Sealed>]
type AgentRateLimit
    (
        sessionId: Guid,
        limitType: string,
        status: string,
        resetsAt: DateTimeOffset,
        isUsingOverage: bool
    ) =
    inherit AgentStreamEvent(sessionId)

    member _.LimitType = limitType
    member _.Status = status
    member _.ResetsAt = resetsAt
    member _.IsUsingOverage = isUsingOverage

/// Input carries the short, relevant one-liner for the collapsed row; FullInput carries the complete,
/// untruncated tool input so the UI can offer an expand-to-full toggle (detailed-output mode).
[<Sealed>]
type AgentToolUse(sessionId: Guid, toolName: string, toolUseId: string, input: string, fullInput: string) =
    inherit AgentStreamEvent(sessionId)

    member _.ToolName = toolName
    member _.ToolUseId = toolUseId
    member _.Input = input
    member _.FullInput = fullInput

/// Summary is the collapsed one-liner; FullOutput is the complete tool result for expand-to-full.
[<Sealed>]
type AgentToolResult(sessionId: Guid, toolUseId: string, summary: string, fullOutput: string, duration: TimeSpan) =
    inherit AgentStreamEvent(sessionId)

    member _.ToolUseId = toolUseId
    member _.Summary = summary
    member _.FullOutput = fullOutput
    member _.Duration = duration

[<Sealed>]
type AgentTextDelta(sessionId: Guid, text: string) =
    inherit AgentStreamEvent(sessionId)

    member _.Text = text

[<Sealed>]
type AgentAssistant(sessionId: Guid, text: string, isFinal: bool) =
    inherit AgentStreamEvent(sessionId)

    member _.Text = text
    member _.IsFinal = isFinal

[<Sealed>]
type AgentUsage(sessionId: Guid, inputTokens: int, outputTokens: int, cacheReadTokens: int) =
    inherit AgentStreamEvent(sessionId)

    member _.InputTokens = inputTokens
    member _.OutputTokens = outputTokens
    member _.CacheReadTokens = cacheReadTokens

/// The authoritative end-of-turn signal. ResultText is the provider's final answer/summary for the turn
/// (empty when the provider emitted none); IsError marks a turn that ended in failure. Subscribers should
/// treat this - not any heuristic over assistant messages - as "the turn is finished".
[<Sealed>]
type AgentResult
    (
        sessionId: Guid,
        agentSessionId: string,
        costUsd: double,
        duration: TimeSpan,
        model: string,
        resultText: string,
        isError: bool
    ) =
    inherit AgentStreamEvent(sessionId)

    member _.AgentSessionId = agentSessionId
    member _.CostUsd = costUsd
    member _.Duration = duration
    member _.Model = model
    member _.ResultText = resultText
    member _.IsError = isError

[<Sealed>]
type AgentHookStart(sessionId: Guid, hookId: string, hookName: string, hookEvent: string, isSessionStart: bool) =
    inherit AgentStreamEvent(sessionId)

    member _.HookId = hookId
    member _.HookName = hookName
    member _.HookEvent = hookEvent
    member _.IsSessionStart = isSessionStart

[<Sealed>]
type AgentHookComplete
    (
        sessionId: Guid,
        hookId: string,
        hookName: string,
        hookEvent: string,
        outcome: string,
        exitCode: Nullable<int>,
        stdout: string,
        stderr: string
    ) =
    inherit AgentStreamEvent(sessionId)

    member _.HookId = hookId
    member _.HookName = hookName
    member _.HookEvent = hookEvent
    member _.Outcome = outcome
    member _.ExitCode = exitCode
    member _.Stdout = stdout
    member _.Stderr = stderr

/// One "always" option the provider suggests for a permission prompt (e.g. add an allow rule for a tool,
/// or switch mode). Id is echoed back verbatim on SendPermissionResponse to identify the pick; Label is
/// the display text the bridge already built from the provider's suggestion. The fixed "allow once" and
/// "deny" choices are not options - the UI frames these suggestions between a leading allow and a trailing
/// deny, so an empty Options array simply yields the plain allow/deny prompt.
[<Sealed>]
type AgentPermissionOption(id: string, label: string) =
    member _.Id = id
    member _.Label = label

/// A pending permission request, already resolved to neutral terms by the provider bridge. Either a
/// matched permission rule (MatchedRulePattern/MatchedRuleScope set) or a human-readable ReasonText.
/// Empty string means absent; the provider's own decision-reason vocabulary never reaches subscribers.
/// Options are the provider's suggested "always" choices (may be empty) - the varying middle of the prompt.
[<Sealed>]
type AgentPermissionRequest
    (
        sessionId: Guid,
        requestId: string,
        toolName: string,
        toolUseId: string,
        input: string,
        matchedRulePattern: string,
        matchedRuleScope: string,
        reasonText: string,
        options: AgentPermissionOption[]
    ) =
    inherit AgentStreamEvent(sessionId)

    member _.RequestId = requestId
    member _.ToolName = toolName
    member _.ToolUseId = toolUseId
    member _.Input = input
    member _.MatchedRulePattern = matchedRulePattern
    member _.MatchedRuleScope = matchedRuleScope
    member _.ReasonText = reasonText
    member _.Options = options

/// One usage window reported by the agent (e.g. a rolling 5-hour budget or a weekly budget). Used and
/// Total are abstract budget units in the provider's own metric - the UI never interprets the unit
/// beyond display, it only ever needs the ratio Used/Total and the window's time span. WindowStart and
/// ResetsAt bound the window so the UI can derive how far through it we are; the provider bridge fills
/// WindowStart from the plan length it knows. A standalone item (like AgentCommand), carried by
/// AgentUsageReport.
[<Sealed>]
type AgentLimitWindow
    (
        name: string,
        used: float,
        total: float,
        unit: string,
        windowStart: DateTimeOffset,
        resetsAt: DateTimeOffset
    ) =
    member _.Name = name
    member _.Used = used
    member _.Total = total
    member _.Unit = unit
    member _.WindowStart = windowStart
    member _.ResetsAt = resetsAt

/// The agent's current usage against its limit windows. Provider-neutral: a bridge reports however many
/// windows its plan exposes (one daily cap, the Claude 5-hour + weekly pair, three tiered windows, ...)
/// and the UI plots each as a point - it never assumes a window count, unit, or provider. Usage is
/// account-global, so this is not part of the AgentStreamEvent hierarchy (no session id) and - like
/// AgentParsingError - is dispatched on its own type, straight to the usage indicator rather than the
/// per-session conversation stream.
[<Sealed>]
type AgentUsageReport(windows: IReadOnlyList<AgentLimitWindow>) =
    member _.Windows = windows

[<Sealed>]
type AgentAborted(sessionId: Guid) =
    inherit AgentStreamEvent(sessionId)

/// One selectable model offered by the provider bridge. Id is the internal name used on the wire (e.g. a
/// provider model id); DisplayName/Version/Description are what pickers show instead of the internal name.
/// ContextSize is the context window in tokens. SupportedEfforts lists the effort ids this model accepts -
/// empty means the effort axis does not apply to it. A standalone item (like AgentCommand), carried by
/// AgentCapabilities.
[<Sealed>]
type AgentModelInfo
    (
        id: string,
        displayName: string,
        version: string,
        contextSize: int,
        description: string,
        supportedEfforts: IReadOnlyList<string>
    ) =
    member _.Id = id
    member _.DisplayName = displayName
    member _.Version = version
    member _.ContextSize = contextSize
    member _.Description = description
    member _.SupportedEfforts = supportedEfforts

/// One reasoning-effort level offered by the provider bridge. Id is the internal value sent back to the
/// provider; DisplayName is what pickers show (e.g. "Extra High" for an internal "xhigh"). Color is a
/// neutral palette hint ("green", "yellow", "accent", "purple", "red", "dim", "text") the UI maps onto its
/// theme - never a concrete brush, so the contract stays UI-framework-free.
[<Sealed>]
type AgentEffortInfo(id: string, displayName: string, description: string, color: string) =
    member _.Id = id
    member _.DisplayName = displayName
    member _.Description = description
    member _.Color = color

/// One permission/operation mode offered by the provider bridge. Id is the internal value sent back to
/// the provider; DisplayName is what pickers show (e.g. "None" for an internal "default").
[<Sealed>]
type AgentModeInfo(id: string, displayName: string, description: string) =
    member _.Id = id
    member _.DisplayName = displayName
    member _.Description = description

/// The session's current model, permission mode and reasoning effort (by internal id), plus the rich
/// choices available for each axis. Clavis sets these when launching the session, so the provider bridge
/// is their source of truth and reports them here - the agent itself need not. Empty current values or
/// empty lists mean the provider does not expose that axis. UI plugins read these for display and for the
/// selection popups without ever naming a provider. Re-published whenever the choice lists change (e.g. a
/// model switch changes which efforts are supported).
[<Sealed>]
type AgentCapabilities
    (
        sessionId: Guid,
        model: string,
        mode: string,
        effort: string,
        models: IReadOnlyList<AgentModelInfo>,
        modes: IReadOnlyList<AgentModeInfo>,
        efforts: IReadOnlyList<AgentEffortInfo>
    ) =
    inherit AgentStreamEvent(sessionId)

    member _.Model = model
    member _.Mode = mode
    member _.Effort = effort
    member _.Models = models
    member _.Modes = modes
    member _.Efforts = efforts

/// The provider bridge confirms the session now runs on a different model (after a SetSessionModel).
/// Model is the internal id; display data comes from the AgentCapabilities lists. UI plugins update
/// their model indicator on this event, not on the optimistic command.
[<Sealed>]
type AgentModelChanged(sessionId: Guid, model: string) =
    inherit AgentStreamEvent(sessionId)

    member _.Model = model

/// The provider bridge confirms the session's permission/operation mode changed (after a SetSessionMode).
[<Sealed>]
type AgentModeChanged(sessionId: Guid, mode: string) =
    inherit AgentStreamEvent(sessionId)

    member _.Mode = mode

/// The provider bridge confirms the session's reasoning effort changed (after a SetSessionEffort, or
/// coerced by the bridge when a model switch drops the previous level).
[<Sealed>]
type AgentEffortChanged(sessionId: Guid, effort: string) =
    inherit AgentStreamEvent(sessionId)

    member _.Effort = effort

/// Parsing failure of a stream line. Not part of the AgentStreamEvent hierarchy - dispatched on its
/// own type so subscribers can handle it separately.
[<Sealed>]
type AgentParsingError(sessionId: Guid, message: string, isIgnorable: bool) =
    member _.SessionId = sessionId
    member _.Message = message
    member _.IsIgnorable = isIgnorable
