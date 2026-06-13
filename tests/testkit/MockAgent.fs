namespace FabioSoft.Clavis.TestKit

open System
open System.Collections.Concurrent
open System.Reactive
open System.Reactive.Subjects
open FabioSoft.Claude

/// The events a mocked agent emits in reply to the system's session inputs. Smart constructors over the
/// native StreamEvent DU with sensible defaults, so a test script reads as a plain list of agent actions.
[<RequireQualifiedAccess>]
module Reply =

    [<Literal>]
    let MockAgentSessionId = "mock-agent-session"

    let init model = StreamEvent.Init(SessionId MockAgentSessionId, model, [])

    let initWith agentSessionId model slashCommands =
        StreamEvent.Init(SessionId agentSessionId, model, slashCommands)

    let commands names =
        names
        |> List.map (fun name -> { Name = name; Description = ""; ArgumentHint = "" })
        |> StreamEvent.Commands

    let thinking summary = StreamEvent.Thinking summary

    let toolUse name input =
        StreamEvent.ToolUse { Name = name; ToolUseId = "tool-1"; Input = input; FullInput = input }

    let toolUseWithId toolUseId name input =
        StreamEvent.ToolUse { Name = name; ToolUseId = toolUseId; Input = input; FullInput = input }

    let toolResult toolUseId summary =
        StreamEvent.ToolResult { ToolUseId = toolUseId; Summary = summary; FullOutput = summary; Duration = TimeSpan.Zero }

    let text chunk = StreamEvent.TextDelta chunk

    let says message = StreamEvent.Assistant { Text = message; IsFinal = true; IsSynthetic = false }

    let partialSays message = StreamEvent.Assistant { Text = message; IsFinal = false; IsSynthetic = false }

    /// A locally generated (model "<synthetic>") assistant message - slash-command output, never a real
    /// answer. The bridge must drop these.
    let syntheticSays message = StreamEvent.Assistant { Text = message; IsFinal = true; IsSynthetic = true }

    let usage inputTokens outputTokens =
        StreamEvent.Usage { InputTokens = inputTokens; OutputTokens = outputTokens; CacheReadTokens = 0 }

    let result costUsd =
        StreamEvent.Result
            { SessionId = SessionId MockAgentSessionId
              CostUsd = costUsd
              Duration = TimeSpan.Zero
              Model = "mock"
              ResultText = ""
              IsError = false
              NumTurns = 1 }

    /// The provider's num_turns=0 acknowledgement of a local command (e.g. the session boot command).
    /// The bridge must drop these - publishing one as AgentResult would terminate the active turn.
    let localCommandResult =
        StreamEvent.Result
            { SessionId = SessionId MockAgentSessionId
              CostUsd = 0.0
              Duration = TimeSpan.Zero
              Model = "<synthetic>"
              ResultText = "/cost output"
              IsError = false
              NumTurns = 0 }

    let hookStart hookId hookEvent =
        StreamEvent.HookStart { HookId = hookId; HookName = $"{hookEvent}:startup"; HookEvent = hookEvent }

    let hookComplete hookId hookEvent =
        StreamEvent.HookComplete
            { HookId = hookId
              HookName = $"{hookEvent}:startup"
              HookEvent = hookEvent
              Outcome = "success"
              ExitCode = Some 0
              Stdout = ""
              Stderr = "" }

    let permissionRequest requestId toolName input =
        StreamEvent.PermissionRequest
            { RequestId = requestId
              ToolName = toolName
              ToolUseId = None
              Input = input
              DecisionReason = None
              DecisionReasonType = None }

    let ended exitCode detail = StreamEvent.SessionEnded(exitCode, detail)

    let aborted = StreamEvent.Aborted

/// A scripted, reactive stand-in for the agent (claude.exe). It records the SessionInputs the system sends
/// it and emits scripted StreamEvents in reply, so a test drives the real ClaudeBridge + Conversation loop
/// without spawning a process. Reactions are configured fluently before the session is started; `Emit`
/// pushes events manually for fine-grained timing.
type MockAgent() =

    let output = new Subject<Result<StreamEvent, ParsingError>>()
    let received = ConcurrentQueue<SessionInput>()

    let mutable onInitialize = [ Reply.init "mock-model" ]
    let mutable onPrompt: string -> StreamEvent list = fun _ -> []
    let mutable onPermissionResponse: string * PermissionDecision -> StreamEvent list = fun _ -> []
    let mutable onInterrupt = [ Reply.aborted ]

    let emit events =
        events |> List.iter (fun event -> output.OnNext(Ok event))

    let handleInput input =
        received.Enqueue input

        match input with
        | SessionInput.Initialize -> emit onInitialize
        | SessionInput.Prompt promptText -> emit (onPrompt promptText)
        | SessionInput.PermissionResponse(requestId, decision) -> emit (onPermissionResponse (requestId, decision))
        | SessionInput.Interrupt -> emit onInterrupt
        | SessionInput.Dispose -> ()

    let session: Session =
        Subject.Create<SessionInput, Result<StreamEvent, ParsingError>>(
            Observer.Create<SessionInput>(Action<SessionInput>(handleInput)),
            output)

    /// The seam to assign to `ClaudeBridgePlugin.SessionFactory`; every started session reuses this agent.
    member _.SessionFactory = Func<SessionConfig, Session>(fun _ -> session)

    member _.SetOnInitialize events = onInitialize <- events

    member _.SetOnPrompt(reaction: string -> StreamEvent list) = onPrompt <- reaction

    member _.SetOnPermissionResponse(reaction: string * PermissionDecision -> StreamEvent list) =
        onPermissionResponse <- reaction

    member _.SetOnInterrupt events = onInterrupt <- events

    member _.Emit(events: StreamEvent list) = emit events

    member _.EmitError(error: ParsingError) = output.OnNext(Error error)

    member _.Received = received |> List.ofSeq

    member _.ReceivedPrompts =
        received
        |> Seq.choose (function
            | SessionInput.Prompt promptText -> Some promptText
            | _ -> None)
        |> List.ofSeq

/// Fluent configuration helpers so a test reads top-to-bottom: `create () |> onInitialize [...] |> onPrompt (...)`.
[<RequireQualifiedAccess>]
module MockAgent =

    let create () = MockAgent()

    let onInitialize events (agent: MockAgent) =
        agent.SetOnInitialize events
        agent

    let onPrompt reaction (agent: MockAgent) =
        agent.SetOnPrompt reaction
        agent

    let onPermissionResponse reaction (agent: MockAgent) =
        agent.SetOnPermissionResponse reaction
        agent

    let onInterrupt events (agent: MockAgent) =
        agent.SetOnInterrupt events
        agent
