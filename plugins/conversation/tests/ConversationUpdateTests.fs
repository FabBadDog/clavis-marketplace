module FabioSoft.Nucleus.Conversation.Tests.ConversationUpdateTests

open System
open System.Collections.Generic
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

let private session (state: ConversationState) =
    state.ActiveSession |> Option.ofObj |> Option.get

let private emptyState = ConversationState.Init()

let private readyState =
    emptyState.WithActiveSession(fun s ->
        s.WithInitState(null).WithStatus(SessionStatus.Ready).WithTurns([||]))

let private activeTurnId = Guid.NewGuid()

let private activeState =
    let turn = Turn(Id = activeTurnId, Prompt = "test")
    readyState.WithActiveSession(fun s ->
        s.WithCurrentTurnId(Nullable activeTurnId).WithTurns([| turn |]))

let private replaceSessionId sessionId (event: AgentStreamEvent) : AgentStreamEvent =
    match event with
    | :? AgentInit as e -> AgentInit(sessionId, e.AgentSessionId, e.Model, e.SlashCommands)
    | :? AgentAborted -> AgentAborted(sessionId)
    | :? AgentSessionEnded as e -> AgentSessionEnded(sessionId, e.ExitCode, e.Detail)
    | :? AgentSessionAlreadyExited -> AgentSessionAlreadyExited(sessionId)
    | :? AgentLogMessage as e -> AgentLogMessage(sessionId, e.Text)
    | :? AgentApiCallRetry -> AgentApiCallRetry(sessionId)
    | :? AgentCompacting -> AgentCompacting(sessionId)
    | :? AgentThinking as e -> AgentThinking(sessionId, e.Summary)
    | :? AgentThinkingTokens as e -> AgentThinkingTokens(sessionId, e.EstimatedTokens)
    | :? AgentRateLimit as e -> AgentRateLimit(sessionId, e.LimitType, e.Status, e.ResetsAt, e.IsUsingOverage)
    | :? AgentHookStart as e -> AgentHookStart(sessionId, e.HookId, e.HookName, e.HookEvent, e.IsSessionStart)
    | :? AgentHookComplete as e -> AgentHookComplete(sessionId, e.HookId, e.HookName, e.HookEvent, e.Outcome, e.ExitCode, e.Stdout, e.Stderr)
    | :? AgentPermissionRequest as e -> AgentPermissionRequest(sessionId, e.RequestId, e.ToolName, e.ToolUseId, e.Input, e.MatchedRulePattern, e.MatchedRuleScope, e.ReasonText)
    | :? AgentToolUse as e -> AgentToolUse(sessionId, e.ToolName, e.ToolUseId, e.Input, e.FullInput)
    | :? AgentToolResult as e -> AgentToolResult(sessionId, e.ToolUseId, e.Summary, e.FullOutput, e.Duration)
    | :? AgentTextDelta as e -> AgentTextDelta(sessionId, e.Text)
    | :? AgentAssistant as e -> AgentAssistant(sessionId, e.Text, e.IsFinal)
    | :? AgentUsage as e -> AgentUsage(sessionId, e.InputTokens, e.OutputTokens, e.CacheReadTokens)
    | :? AgentResult as e -> AgentResult(sessionId, e.AgentSessionId, e.CostUsd, e.Duration, e.Model, e.ResultText, e.IsError)
    | _ -> event

let private handle state (event: AgentStreamEvent) =
    let sessionId = (session state).Id
    let event = replaceSessionId sessionId event
    ConversationUpdate.HandleStreamEvent(state, event)

let private findTurnByPrompt prompt (state: ConversationState) =
    (session state).Turns |> Seq.find (fun t -> t.Prompt = prompt)

module InitEvent =

    [<Fact>]
    let ``sets model and completes init`` () =

        // Arrange
        let initState = ConversationState.Init()

        // Act
        let struct (newState, _) = handle initState (AgentInit(Guid.Empty, "s1", "opus-4", Array.empty<string>))

        // Assert
        %(session newState).Model.Should().Be("opus-4")
        %(session newState).Status.Should().Be(SessionStatus.Ready)
        %(session newState).IsInitActive.Should().BeTrue()

    [<Fact>]
    let ``promotes queued turn on init event`` () =

        // Arrange - a prompt was typed (and queued) while the session was still initialising
        let initState = ConversationState.Init()
        let queuedId = Guid.NewGuid()
        let queued = Turn(Id = queuedId, Prompt = "hello", Status = Queued(), EstimatedTokens = 10, TotalTokens = 10)
        let state =
            initState.WithActiveSession(fun s ->
                s.WithTurns(Seq.append s.Turns [| queued |] |> Seq.toArray)
                 .WithQueuedTurnIds([| QueuedTurn(queuedId, "hello") |]))

        // Act - the session reports ready
        let struct (newState, effects) = handle state (AgentInit(Guid.Empty, "s1", "opus-4", Array.empty<string>))

        // Assert - the held prompt is promoted to the running turn and sent
        %(session newState).CurrentTurnId.Value.Should().Be(queuedId)
        %(session newState).QueuedCount.Should().Be(0)
        let hasSend = effects |> Array.exists (fun e -> match e with :? SendPromptEffect as sp -> sp.Text = "hello" | _ -> false)
        %hasSend.Should().BeTrue()

module AbortedEvent =

    [<Fact>]
    let ``sets aborting status`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentAborted(Guid.Empty))

        // Assert
        %(session newState).Status.Should().Be(SessionStatus.Aborting)

module SessionEndedEvent =

    [<Fact>]
    let ``triggers full restart when aborting`` () =

        // Arrange
        let state = activeState.WithActiveSession(fun s -> s.WithStatus(SessionStatus.Aborting))

        // Act
        let struct (newState, effects) = handle state (AgentSessionEnded(Guid.Empty, 0, ""))

        // Assert
        %newState.Sessions.Count.Should().Be(2)
        let hasStartEffect = effects |> Array.exists (fun e -> e :? StartNewSessionEffect)
        %hasStartEffect.Should().BeTrue()

    [<Fact>]
    let ``sets ended state on clean exit`` () =

        // Act
        let struct (newState, _) = handle readyState (AgentSessionEnded(Guid.Empty, 0, ""))

        // Assert
        %(session newState).Status.Should().Be(SessionStatus.Ended)

    [<Fact>]
    let ``shows error on non-zero exit`` () =

        // Act
        let struct (newState, _) = handle readyState (AgentSessionEnded(Guid.Empty, 1, "crash"))

        // Assert
        %(session newState).Status.Should().Be(SessionStatus.Ended)

module LogMessageEvent =

    [<Fact>]
    let ``during init sets init turn output`` () =

        // Arrange
        let initState = ConversationState.Init()

        // Act
        let struct (newState, _) = handle initState (AgentLogMessage(Guid.Empty, "loading..."))

        // Assert
        let initTurnId = (session newState).InitTurnId
        %initTurnId.HasValue.Should().BeTrue()
        let initTurn = (session newState).Turns |> Seq.find (fun t -> Nullable t.Id = initTurnId)
        %initTurn.StatusText.Should().Be("loading...")

    [<Fact>]
    let ``after init does nothing`` () =

        // Act
        let struct (newState, _) = handle readyState (AgentLogMessage(Guid.Empty, "something"))

        // Assert
        %(session newState).Turns.Count.Should().Be(0)

module ThinkingEvent =

    [<Fact>]
    let ``sets status to thinking`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentThinking(Guid.Empty, "summary"))

        // Assert
        %(session newState).Status.Should().Be(SessionStatus.Thinking)

module HookStartEvent =

    [<Fact>]
    let ``during init adds hook item to init turn`` () =

        // Arrange
        let initState = ConversationState.Init()

        // Act
        let struct (newState, _) = handle initState (AgentHookStart(Guid.Empty, "h1", "hook", "SessionStart", true))

        // Assert
        let initTurnId = (session newState).InitTurnId
        let initTurn = (session newState).Turns |> Seq.find (fun t -> Nullable t.Id = initTurnId)
        let hasHook = initTurn.Items |> Seq.exists (fun i -> match i with :? HookItem as hi -> hi.Hook.HookId = "h1" | _ -> false)
        %hasHook.Should().BeTrue()

    [<Fact>]
    let ``after init does nothing`` () =

        // Act
        let struct (newState, _) = handle readyState (AgentHookStart(Guid.Empty, "h1", "hook", "PreToolUse", false))

        // Assert
        %(session newState).Turns.Count.Should().Be(0)

module HookCompleteEvent =

    [<Fact>]
    let ``updates hook outcome`` () =

        // Arrange
        let initState = ConversationState.Init()
        let struct (stateWithHook, _) = handle initState (AgentHookStart(Guid.Empty, "h1", "hook", "SessionStart", true))

        // Act
        let struct (newState, _) = handle stateWithHook (AgentHookComplete(Guid.Empty, "h1", "hook", "SessionStart", "success", Nullable 0, "", ""))

        // Assert
        let initTurnId = (session newState).InitTurnId
        let initTurn = (session newState).Turns |> Seq.find (fun t -> Nullable t.Id = initTurnId)
        let hookItem =
            initTurn.Items
            |> Seq.tryPick (fun i -> match i with :? HookItem as hi when hi.Hook.HookId = "h1" -> Some hi.Hook | _ -> None)
        %hookItem.IsSome.Should().BeTrue()
        %hookItem.Value.HasSucceeded.Value.Should().BeTrue()

// The init turn's full real-world boot sequence, as the provider actually streams it (the boot is lazy
// and slow: SessionStart hooks fire first, then init; on a loaded machine everything may arrive only
// AFTER the init timeout already closed the turn). This is the regression guard for the recurring
// "init turn shows only Starting Claude" bug - the hook rows and the MCP-loading phase must render in
// both the timely and the late arrival orders.
module InitTurnBootSequence =

    let private initTurn (state: ConversationState) =
        let initTurnId = (session state).InitTurnId
        (session state).Turns |> Seq.find (fun t -> Nullable t.Id = initTurnId)

    let private hookItems (turn: Turn) =
        turn.Items |> Seq.choose (fun i -> match i with :? HookItem as hi -> Some hi.Hook | _ -> None) |> List.ofSeq

    let private phaseItems (turn: Turn) =
        turn.Items |> Seq.choose (fun i -> match i with :? PhaseItem as pi -> Some pi.Phase | _ -> None) |> List.ofSeq

    let private runBootSequence state =
        let struct (afterHookStart, _) = handle state (AgentHookStart(Guid.Empty, "h1", "SessionStart:startup", "SessionStart", true))
        let struct (afterHookComplete, _) = handle afterHookStart (AgentHookComplete(Guid.Empty, "h1", "SessionStart:startup", "SessionStart", "success", Nullable 0, "", ""))
        let struct (afterInit, _) = handle afterHookComplete (AgentInit(Guid.Empty, "s1", "opus-4", Array.empty<string>))
        afterInit

    [<Fact>]
    let ``boot sequence renders hook rows and the MCP-loading phase on the init turn`` () =

        // Act
        let finalState = runBootSequence (ConversationState.Init())

        // Assert
        let turn = initTurn finalState
        let hooks = hookItems turn
        %(hooks |> List.exists _.IsHeader).Should().BeTrue()
        %(hooks |> List.exists (fun h -> h.HookId = "h1" && h.HasSucceeded = Nullable true)).Should().BeTrue()
        let phases = phaseItems turn
        %(phases |> List.exists (fun p -> p.DisplayName = "Loading MCPs and plugins" && not p.IsActive && p.HasSucceeded)).Should().BeTrue()
        %(phases |> List.forall (fun p -> not p.IsActive)).Should().BeTrue()
        %(turn.Status :? Succeeded).Should().BeTrue()

    [<Fact>]
    let ``hooks arriving after the init timeout revive the init turn`` () =

        // Arrange - the provider booted so slowly that the init timeout closed the turn first
        let state = ConversationState.Init()
        let sessionId = (session state).Id
        let struct (timedOut, _) = ConversationUpdate.HandleInitTimedOut(state, sessionId)
        %(session timedOut).IsInitActive.Should().BeFalse()

        // Act - the boot events still arrive afterwards
        let finalState = runBootSequence timedOut

        // Assert - the late hook rows and MCP phase render on the revived init turn
        let turn = initTurn finalState
        %(hookItems turn |> List.exists (fun h -> h.HookId = "h1")).Should().BeTrue()
        %(phaseItems turn |> List.exists (fun p -> p.DisplayName = "Loading MCPs and plugins")).Should().BeTrue()
        %(turn.Status :? Succeeded).Should().BeTrue()

    [<Fact>]
    let ``a late non-session-start hook does not revive the init turn`` () =

        // Arrange
        let state = ConversationState.Init()
        let sessionId = (session state).Id
        let struct (timedOut, _) = ConversationUpdate.HandleInitTimedOut(state, sessionId)

        // Act
        let struct (newState, _) = handle timedOut (AgentHookStart(Guid.Empty, "h2", "Stop", "Stop", false))

        // Assert
        %(session newState).IsInitActive.Should().BeFalse()
        %(hookItems (initTurn newState)).Should().BeEmpty()

module Tick =

    let private fixedStart = DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)

    let private runningTurnState turnId (items: TurnItem[]) =
        let turn = Turn(Id = turnId, Status = Running(), StartedAt = fixedStart, Items = items)
        readyState.WithActiveSession(fun s ->
            s.WithCurrentTurnId(Nullable turnId).WithTurns([| turn |]))

    [<Fact>]
    let ``advances a running turn duration`` () =

        // Arrange
        let turnId = Guid.NewGuid()
        let state = runningTurnState turnId [||]

        // Act
        let struct (newState, _) = ConversationUpdate.HandleTick(state, fixedStart.AddSeconds 5.0)

        // Assert
        let updated = (session newState).Turns |> Seq.find (fun t -> t.Id = turnId)
        %updated.Duration.Should().Be(TimeSpan.FromSeconds 5.0)

    [<Fact>]
    let ``advances an active item duration inside a running turn`` () =

        // Arrange
        let turnId = Guid.NewGuid()
        let tool = ToolItem(Tool(ToolUseId = "t1", Name = "Write", IsActive = true, StartedAt = fixedStart))
        let state = runningTurnState turnId [| tool :> TurnItem |]

        // Act
        let struct (newState, _) = ConversationUpdate.HandleTick(state, fixedStart.AddSeconds 3.0)

        // Assert
        let updated = (session newState).Turns |> Seq.find (fun t -> t.Id = turnId)
        let toolDuration =
            updated.Items |> Seq.pick (fun i -> match i with :? ToolItem as ti -> Some ti.Tool.Duration | _ -> None)
        %toolDuration.Should().Be(TimeSpan.FromSeconds 3.0)

    [<Fact>]
    let ``leaves a finished turn and its stale-active items untouched`` () =

        // Arrange
        let turnId = Guid.NewGuid()
        let staleHook = HookItem(Hook(HookId = "h1", IsActive = true, StartedAt = fixedStart))
        let turn =
            Turn(Id = turnId, Status = Succeeded(), StartedAt = fixedStart,
                 Duration = TimeSpan.FromSeconds 2.0, Items = [| staleHook :> TurnItem |])
        let state = readyState.WithActiveSession(fun s -> s.WithTurns([| turn |]))

        // Act
        let struct (newState, _) = ConversationUpdate.HandleTick(state, fixedStart.AddSeconds 99.0)

        // Assert
        let updated = (session newState).Turns |> Seq.find (fun t -> t.Id = turnId)
        %updated.Duration.Should().Be(TimeSpan.FromSeconds 2.0)
        let hookDuration =
            updated.Items |> Seq.pick (fun i -> match i with :? HookItem as hi -> Some hi.Hook.Duration | _ -> None)
        %hookDuration.Should().Be(TimeSpan.Zero)

    [<Fact>]
    let ``is a no-op when there is no active session`` () =

        // Act
        let struct (_, effects) = ConversationUpdate.HandleTick(ConversationState(), DateTime.UtcNow)

        // Assert
        %effects.Length.Should().Be(0)

module ToolUseEvent =

    [<Fact>]
    let ``adds tool item and tracks tool use id`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentToolUse(Guid.Empty, "Write", "tu1", "content", ""))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        let hasTool = turn.Items |> Seq.exists (fun i -> match i with :? ToolItem as ti -> ti.Tool.ToolUseId = "tu1" | _ -> false)
        %hasTool.Should().BeTrue()
        %(session newState).KnownToolUseIds.Contains("tu1").Should().BeTrue()

module ToolResultEvent =

    [<Fact>]
    let ``marks tool completed for known tool`` () =

        // Arrange
        let struct (stateWithTool, _) = handle activeState (AgentToolUse(Guid.Empty, "Write", "tu1", "content", ""))

        // Act
        let struct (newState, _) = handle stateWithTool (AgentToolResult(Guid.Empty, "tu1", "wrote file", "", TimeSpan.Zero))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        let toolItem = turn.Items |> Seq.tryPick (fun i -> match i with :? ToolItem as ti when ti.Tool.ToolUseId = "tu1" -> Some ti.Tool | _ -> None)
        %toolItem.IsSome.Should().BeTrue()
        %toolItem.Value.IsActive.Should().BeFalse()

    [<Fact>]
    let ``marks tool denied for denied result`` () =

        // Arrange
        let struct (stateWithTool, _) = handle activeState (AgentToolUse(Guid.Empty, "Write", "tu1", "content", ""))

        // Act
        let struct (newState, _) = handle stateWithTool (AgentToolResult(Guid.Empty, "tu1", "Tool denied by user", "", TimeSpan.Zero))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        let toolItem = turn.Items |> Seq.tryPick (fun i -> match i with :? ToolItem as ti when ti.Tool.ToolUseId = "tu1" -> Some ti.Tool | _ -> None)
        %toolItem.IsSome.Should().BeTrue()
        %toolItem.Value.IsDenied.Should().BeTrue()

    [<Fact>]
    let ``ignores unknown tool use id`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentToolResult(Guid.Empty, "unknown", "result", "", TimeSpan.Zero))

        // Assert
        %(session newState).Turns.Count.Should().Be(1)

module TextDeltaEvent =

    [<Fact>]
    let ``sets turn output when turn active`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentTextDelta(Guid.Empty, "hello"))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.StatusText.Should().Be("hello")

    [<Fact>]
    let ``does nothing when no turn active`` () =

        // Act
        let struct (newState, _) = handle readyState (AgentTextDelta(Guid.Empty, "hello"))

        // Assert
        %(session newState).Turns.Count.Should().Be(0)

module AssistantEvent =

    // Assistant text streams the response but never ends the turn (only AgentResult does). This is the fix
    // for premature turn completion: in stream-json a narration message has no tool call and no stop_reason,
    // so the old "final == no tool use" heuristic closed the turn early and dropped everything after it.

    [<Fact>]
    let ``sets response on active turn without finalizing`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentAssistant(Guid.Empty, "response", true))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.Response.Should().Be("response") |> ignore
        %(turn.Status :? Running).Should().BeTrue() |> ignore
        %(session newState).CurrentTurnId.HasValue.Should().BeTrue()

    [<Fact>]
    let ``latest assistant text wins`` () =

        // Act
        let struct (state1, _) = handle activeState (AgentAssistant(Guid.Empty, "first narration", false))
        let struct (state2, _) = handle state1 (AgentAssistant(Guid.Empty, "the real answer", true))

        // Assert
        let turn = (session state2).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.Response.Should().Be("the real answer")

    [<Fact>]
    let ``does not promote queued turn`` () =

        // Arrange
        let queuedId = Guid.NewGuid()
        let queued = Turn(Id = queuedId, Prompt = "queued", Status = Queued())
        let state =
            activeState.WithActiveSession(fun s ->
                s.WithQueuedTurnIds([| QueuedTurn(queuedId, "queued") |])
                 .WithTurns(Seq.append s.Turns [| queued |] |> Seq.toArray))

        // Act
        let struct (newState, _) = handle state (AgentAssistant(Guid.Empty, "done", true))

        // Assert
        %(session newState).CurrentTurnId.Value.Should().Be(activeTurnId) |> ignore
        %(session newState).QueuedCount.Should().Be(1)

    [<Fact>]
    let ``ignores empty text`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentAssistant(Guid.Empty, "", true))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.Response.Should().Be("")

module DeferredToolResolution =

    // The first MCP tool call in a session resolves the deferred tool via a ToolSearch step before the real
    // call. Reproduces the live sequence to ensure the final answer still lands in Response (not left as the
    // streaming preamble).
    [<Fact>]
    let ``renders final answer after ToolSearch deferred-tool dance`` () =

        // Arrange
        let events: AgentStreamEvent list =
            [ AgentTextDelta(Guid.Empty, "I'll query the live Clavis host for its loaded plugins.")
              AgentAssistant(Guid.Empty, "I'll query the live Clavis host for its loaded plugins.", false)
              AgentToolUse(Guid.Empty, "ToolSearch", "ts1", "select:mcp__clavis__list_plugins", "")
              AgentToolResult(Guid.Empty, "ts1", "mcp__clavis__list_plugins", "", TimeSpan.Zero)
              AgentToolUse(Guid.Empty, "mcp__clavis__list_plugins", "lp1", "", "")
              AgentToolResult(Guid.Empty, "lp1", "[18 plugins]", "", TimeSpan.Zero)
              AgentThinking(Guid.Empty, "counting")
              AgentTextDelta(Guid.Empty, "There are 18 plugins loaded.")
              AgentAssistant(Guid.Empty, "There are 18 plugins loaded.", true) ]

        // Act
        let finalState =
            events |> List.fold (fun st ev -> let struct (ns, _) = handle st ev in ns) activeState

        // Assert
        let turn = (session finalState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.Response.Should().Be("There are 18 plugins loaded.")

module UsageEvent =

    [<Fact>]
    let ``updates context filled`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentUsage(Guid.Empty, 100_000, 500, 0))

        // Assert
        %(session newState).ContextSize.Should().Be(200_000)
        %(session newState).ContextFilled.Should().Be(100_000)

    [<Fact>]
    let ``adds tokens to turn when active`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentUsage(Guid.Empty, 100, 500, 0))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.TotalTokens.Should().Be(500)

module ResultEvent =

    [<Fact>]
    let ``updates model when non-empty`` () =

        // Act
        let struct (newState, _) = handle readyState (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "sonnet-4", "", false))

        // Assert
        %(session newState).Model.Should().Be("sonnet-4")

    [<Fact>]
    let ``sets idle status`` () =

        // Act
        let struct (newState, _) = handle readyState (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "", "", false))

        // Assert
        %(session newState).Status.Should().Be(SessionStatus.Idle)

    [<Fact>]
    let ``finishes init if still active`` () =

        // Arrange
        let initState = ConversationState.Init()

        // Act
        let struct (newState, _) = handle initState (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "opus-4", "", false))

        // Assert
        %(session newState).IsInitActive.Should().BeFalse()

    [<Fact>]
    let ``completes active turn with result summary`` () =

        // Act
        let struct (newState, _) =
            handle activeState (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "opus-4", "final summary", false))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.Response.Should().Be("final summary") |> ignore
        %(turn.Status :? Succeeded).Should().BeTrue() |> ignore
        %(session newState).CurrentTurnId.HasValue.Should().BeFalse() |> ignore
        %(session newState).Status.Should().Be(SessionStatus.Ready)

    [<Fact>]
    let ``falls back to streamed response when result text empty`` () =

        // Arrange
        let struct (streamed, _) = handle activeState (AgentAssistant(Guid.Empty, "streamed answer", true))

        // Act
        let struct (newState, _) = handle streamed (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "opus-4", "", false))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %turn.Response.Should().Be("streamed answer") |> ignore
        %(turn.Status :? Succeeded).Should().BeTrue()

    [<Fact>]
    let ``marks turn failed on is_error`` () =

        // Act
        let struct (newState, _) =
            handle activeState (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "opus-4", "ran out of budget", true))

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        %(turn.Status :? Failed).Should().BeTrue()

    [<Fact>]
    let ``promotes queued turn on result`` () =

        // Arrange
        let queuedId = Guid.NewGuid()
        let queued = Turn(Id = queuedId, Prompt = "second", Status = Queued())
        let stateWithQueued =
            activeState.WithActiveSession(fun s ->
                s.WithQueuedTurnIds([| QueuedTurn(queuedId, "second") |])
                 .WithTurns(Seq.append s.Turns [| queued |] |> Seq.toArray))
        let struct (stateAfterAssistant, _) = handle stateWithQueued (AgentAssistant(Guid.Empty, "first answer", true))

        // Act
        let struct (stateAfterResult, effects) =
            handle stateAfterAssistant (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "opus-4", "first answer", false))

        // Assert
        %(session stateAfterResult).CurrentTurnId.HasValue.Should().BeTrue() |> ignore
        %(session stateAfterResult).CurrentTurnId.Value.Should().Be(queuedId) |> ignore
        %(session stateAfterResult).IsCurrentTurnActive.Should().BeTrue() |> ignore
        let hasSendSecond = effects |> Array.exists (fun e -> match e with :? SendPromptEffect as sp -> sp.Text = "second" | _ -> false)
        %hasSendSecond.Should().BeTrue()

module UserSubmitted =

    [<Fact>]
    let ``activates turn when idle`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandleUserSubmitted(readyState, "hello")

        // Assert
        %(session newState).IsCurrentTurnActive.Should().BeTrue()
        let turn = findTurnByPrompt "hello" newState
        %(turn.Status :? Running).Should().BeTrue()

    [<Fact>]
    let ``sends prompt to process`` () =

        // Act
        let struct (_, effects) = ConversationUpdate.HandleUserSubmitted(readyState, "hello")

        // Assert
        let hasSendPrompt = effects |> Array.exists (fun e -> match e with :? SendPromptEffect as sp -> sp.Text = "hello" | _ -> false)
        %hasSendPrompt.Should().BeTrue()

    [<Fact>]
    let ``queues turn when turn already active`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandleUserSubmitted(activeState, "queued")

        // Assert
        %(session newState).QueuedCount.Should().Be(1)
        let turn = findTurnByPrompt "queued" newState
        %(turn.Status :? Queued).Should().BeTrue()

    [<Fact>]
    let ``queues first prompt during init`` () =

        // Act - a prompt typed while the session is still initialising is held, not sent
        let struct (newState, effects) = ConversationUpdate.HandleUserSubmitted(emptyState, "hello")

        // Assert
        %(session newState).QueuedCount.Should().Be(1)
        let turn = findTurnByPrompt "hello" newState
        %(turn.Status :? Queued).Should().BeTrue()
        %(effects |> Array.isEmpty).Should().BeTrue()

module UserAborted =

    [<Fact>]
    let ``aborts active turn`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandleUserAborted(activeState)

        // Assert
        %(session newState).IsCurrentTurnActive.Should().BeFalse()
        %(session newState).IsProcessing.Should().BeFalse()
        %(session newState).Status.Should().Be(SessionStatus.Aborted)

    [<Fact>]
    let ``sends interrupt effect`` () =

        // Act
        let struct (_, effects) = ConversationUpdate.HandleUserAborted(activeState)

        // Assert
        let hasInterrupt = effects |> Array.exists (fun e -> e :? InterruptSessionEffect)
        %hasInterrupt.Should().BeTrue()

    [<Fact>]
    let ``cancels newest queued turn before aborting the active turn`` () =

        // Arrange
        let queuedId = Guid.NewGuid()
        let state =
            activeState.WithActiveSession(fun s ->
                s.WithQueuedTurnIds([| QueuedTurn(queuedId, "q") |]))

        // Act
        let struct (newState, _) = ConversationUpdate.HandleUserAborted(state)

        // Assert
        %(session newState).QueuedCount.Should().Be(0)
        %(session newState).IsCurrentTurnActive.Should().BeTrue()

    [<Fact>]
    let ``does nothing when no turn active`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandleUserAborted(readyState)

        // Assert
        %(session newState).Status.Should().Be((session readyState).Status)

module UserCancelledQueued =

    [<Fact>]
    let ``removes newest queued turn`` () =

        // Arrange
        let queuedId = Guid.NewGuid()
        let queued = Turn(Id = queuedId, Prompt = "q", Status = Queued())
        let state =
            activeState.WithActiveSession(fun s ->
                s.WithQueuedTurnIds([| QueuedTurn(queuedId, "q") |])
                 .WithTurns(Seq.append s.Turns [| queued |] |> Seq.toArray))

        // Act
        let struct (newState, _) = ConversationUpdate.HandleUserCancelledQueued(state)

        // Assert
        %(session newState).QueuedCount.Should().Be(0)

    [<Fact>]
    let ``does nothing when no queued turns`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandleUserCancelledQueued(activeState)

        // Assert
        %(session newState).QueuedCount.Should().Be(0)

module PermissionRequestEvent =

    let private permission (state: ConversationState) =
        (session state).Turns
        |> Seq.collect (fun turn -> turn.Items)
        |> Seq.pick (fun item ->
            match item with
            | :? PermissionItem as p -> Some p.Permission
            | _ -> None)

    [<Fact>]
    let ``matched ask rule populates rule pattern and scope`` () =

        // Act
        let struct (newState, _) =
            handle activeState (AgentPermissionRequest(Guid.Empty, "r1", "Bash", "tu1", "ls", "Bash(*)", "user", ""))

        // Assert
        let perm = permission newState
        %perm.MatchedRulePattern.Should().Be("Bash(*)")
        %perm.MatchedRuleScope.Should().Be("user")
        %perm.ReasonText.Should().BeNull()

    [<Fact>]
    let ``reason text is kept when no ask rule matched`` () =

        // Act
        let struct (newState, _) =
            handle activeState (AgentPermissionRequest(Guid.Empty, "r1", "Bash", null, "ls", "", "", "No matching permission rule"))

        // Assert
        let perm = permission newState
        %perm.ReasonText.Should().Be("No matching permission rule")
        %perm.MatchedRulePattern.Should().BeNull()

    [<Fact>]
    let ``attaches to last turn when no turn is current`` () =

        // Arrange
        let lastTurnId = Guid.NewGuid()
        let lastTurn = Turn(Id = lastTurnId, Prompt = "earlier")
        let state =
            readyState.WithActiveSession(fun s ->
                s.WithCurrentTurnId(Nullable()).WithLastTurnId(Nullable lastTurnId).WithTurns([| lastTurn |]))

        // Act
        let struct (newState, _) =
            handle state (AgentPermissionRequest(Guid.Empty, "r1", "mcp__clavis__list_plugins", null, "", "", "", "No matching permission rule"))

        // Assert: the request must surface a row rather than vanish, or the agent blocks forever
        let perm = permission newState
        %perm.RequestId.Should().Be("r1")

module PermissionDecided =

    [<Fact>]
    let ``sends permission response effect`` () =

        // Arrange
        let permItem = PermissionItem(Permission(RequestId = "r1", ToolUseId = null))
        let turnWithPerm = Turn(Id = activeTurnId, Prompt = "test", Items = [| permItem |])
        let state =
            readyState.WithActiveSession(fun s ->
                s.WithCurrentTurnId(Nullable activeTurnId).WithTurns([| turnWithPerm |]))

        // Act
        let struct (_, effects) = ConversationUpdate.HandlePermissionDecided(state, "r1", "allow")

        // Assert
        let hasResponse = effects |> Array.exists (fun e -> match e with :? SendPermissionResponseEffect as r -> r.RequestId = "r1" && r.Allow | _ -> false)
        %hasResponse.Should().BeTrue()

module ParsingError =

    [<Fact>]
    let ``adds error item for structural parsing errors`` () =

        // Arrange
        let sessionId = (session activeState).Id

        // Act
        let struct (newState, _) = ConversationUpdate.HandleParsingError(activeState, sessionId, "Missing field: test.field", false)

        // Assert
        let turn = (session newState).Turns |> Seq.find (fun t -> t.Id = activeTurnId)
        let hasError = turn.Items |> Seq.exists (fun i -> i :? ErrorItem)
        %hasError.Should().BeTrue()

    [<Fact>]
    let ``silently ignores ignorable errors`` () =

        // Arrange
        let sessionId = (session emptyState).Id

        // Act
        let struct (newState, _) = ConversationUpdate.HandleParsingError(emptyState, sessionId, "UnknownMessageType: set_tool_event", true)

        // Assert
        %(session newState).Turns.Count.Should().Be(1)

module InitTimedOut =

    [<Fact>]
    let ``finishes init when active`` () =

        // Arrange
        let initState = ConversationState.Init()
        let sessionId = (session initState).Id

        // Act
        let struct (newState, _) = ConversationUpdate.HandleInitTimedOut(initState, sessionId)

        // Assert
        %(session newState).IsInitActive.Should().BeFalse()

    [<Fact>]
    let ``sets status to Ready`` () =

        // Arrange
        let initState = ConversationState.Init()
        let sessionId = (session initState).Id

        // Act
        let struct (newState, _) = ConversationUpdate.HandleInitTimedOut(initState, sessionId)

        // Assert
        %(session newState).Status.Should().Be(SessionStatus.Ready)

    [<Fact>]
    let ``does nothing when init already done`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandleInitTimedOut(readyState, (session readyState).Id)

        // Assert
        %(session newState).Turns.Count.Should().Be(0)

module TwoQueuedPrompts =

    let private resultEvent model =
        AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, model, "", false)

    [<Fact>]
    let ``full flow from init through both prompts`` () =

        // Arrange
        let state = ConversationState.Init()
        let sessionId = (session state).Id

        let handleStream st (event: AgentStreamEvent) =
            let event = replaceSessionId sessionId event
            let struct (newSt, effects) = ConversationUpdate.HandleStreamEvent(st, event)
            newSt

        // Both prompts are queued during init (the session cannot receive them yet)
        let struct (state, effects1) = ConversationUpdate.HandleUserSubmitted(state, "first")
        %(session state).QueuedCount.Should().Be(1)
        %(effects1 |> Array.isEmpty).Should().BeTrue()
        let firstTurnId = (findTurnByPrompt "first" state).Id

        let struct (state, _) = ConversationUpdate.HandleUserSubmitted(state, "second")
        %(session state).QueuedCount.Should().Be(2)
        let secondTurnId = (findTurnByPrompt "second" state).Id

        // Init event promotes the first queued prompt (sends it); the second stays queued
        let state = handleStream state (AgentInit(Guid.Empty, "s1", "opus-4", Array.empty<string>))
        %(session state).CurrentTurnId.Value.Should().Be(firstTurnId)
        %(session state).IsCurrentTurnActive.Should().BeTrue()
        %(session state).QueuedCount.Should().Be(1)

        // First prompt receives text
        let state = handleStream state (AgentTextDelta(Guid.Empty, "first answer streaming"))
        let firstTurn = (session state).Turns |> Seq.find (fun t -> t.Id = firstTurnId)
        %firstTurn.StatusText.Should().Be("first answer streaming")

        // First prompt's assistant message streams the response but does NOT end the turn
        let state = handleStream state (AgentAssistant(Guid.Empty, "first answer", true))
        %(session state).CurrentTurnId.Value.Should().Be(firstTurnId)
        %(session state).QueuedCount.Should().Be(1)
        let firstTurn = (session state).Turns |> Seq.find (fun t -> t.Id = firstTurnId)
        %firstTurn.Response.Should().Be("first answer")
        %(firstTurn.Status :? Running).Should().BeTrue()

        // First prompt's result ends the first turn (finishing init too) and promotes the second
        let state = handleStream state (resultEvent "opus-4")
        %(session state).CurrentTurnId.Value.Should().Be(secondTurnId)
        %(session state).IsCurrentTurnActive.Should().BeTrue()
        %(session state).IsInitActive.Should().BeFalse()
        let firstTurn = (session state).Turns |> Seq.find (fun t -> t.Id = firstTurnId)
        %firstTurn.Response.Should().Be("first answer")
        %(firstTurn.Status :? Succeeded).Should().BeTrue()

        // Second prompt receives text
        let state = handleStream state (AgentTextDelta(Guid.Empty, "second answer streaming"))
        let secondTurn = (session state).Turns |> Seq.find (fun t -> t.Id = secondTurnId)
        %secondTurn.StatusText.Should().Be("second answer streaming")

        // Second prompt's assistant message streams the response, still not final
        let state = handleStream state (AgentAssistant(Guid.Empty, "second answer", true))
        %(session state).IsCurrentTurnActive.Should().BeTrue()
        let secondTurn = (session state).Turns |> Seq.find (fun t -> t.Id = secondTurnId)
        %secondTurn.Response.Should().Be("second answer")

        // Second prompt's result ends the turn for good
        let state = handleStream state (resultEvent "opus-4")
        %(session state).IsCurrentTurnActive.Should().BeFalse()
        %(session state).CurrentTurnId.HasValue.Should().BeFalse()
        let secondTurn = (session state).Turns |> Seq.find (fun t -> t.Id = secondTurnId)
        %secondTurn.Response.Should().Be("second answer")
        %(secondTurn.Status :? Succeeded).Should().BeTrue()
        %(session state).IsProcessing.Should().BeFalse()

module DetailedOutput =

    let private items (state: ConversationState) =
        ((session state).Turns |> Seq.find (fun t -> t.Id = activeTurnId)).Items

    [<Fact>]
    let ``assistant text is appended as an interleaved text item`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentAssistant(Guid.Empty, "narration", false))

        // Assert
        let textItems = items newState |> Seq.choose (function :? TextItem as t -> Some t | _ -> None) |> Seq.toList
        %textItems.Length.Should().Be(1) |> ignore
        %textItems.[0].Markdown.Should().Be("narration")

    [<Fact>]
    let ``each assistant block becomes its own text item in order`` () =

        // Act
        let struct (s1, _) = handle activeState (AgentAssistant(Guid.Empty, "first", false))
        let struct (s2, _) = handle s1 (AgentAssistant(Guid.Empty, "second", true))

        // Assert
        let texts =
            items s2
            |> Seq.choose (function :? TextItem as t -> Some t.Markdown | _ -> None)
            |> Seq.toList
        %texts.Should().SequenceEqual(["first"; "second"])

    [<Fact>]
    let ``thinking is appended as an interleaved thinking item`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentThinking(Guid.Empty, "reasoning step"))

        // Assert
        let thinkingItems = items newState |> Seq.choose (function :? ThinkingItem as t -> Some t | _ -> None) |> Seq.toList
        %thinkingItems.Length.Should().Be(1) |> ignore
        %thinkingItems.[0].Text.Should().Be("reasoning step")

    [<Fact>]
    let ``tool use carries full input and tool result carries full output`` () =

        // Act
        let struct (s1, _) = handle activeState (AgentToolUse(Guid.Empty, "Bash", "tu1", "echo hi", "{\"command\":\"echo hi\"}"))
        let struct (s2, _) = handle s1 (AgentToolResult(Guid.Empty, "tu1", "hi", "hi\nfull output", TimeSpan.Zero))

        // Assert
        let tool =
            items s2
            |> Seq.pick (function :? ToolItem as t when t.Tool.ToolUseId = "tu1" -> Some t.Tool | _ -> None)
        %tool.FullArguments.Should().Be("{\"command\":\"echo hi\"}") |> ignore
        %tool.FullOutput.Should().Be("hi\nfull output")

    [<Fact>]
    let ``thinking tokens are recorded on the session`` () =

        // Act
        let struct (newState, _) = handle activeState (AgentThinkingTokens(Guid.Empty, 1234))

        // Assert
        %(session newState).ThinkingTokens.Should().Be(1234)

    [<Fact>]
    let ``result clears the thinking token estimate`` () =

        // Arrange
        let struct (thinking, _) = handle activeState (AgentThinkingTokens(Guid.Empty, 1234))

        // Act
        let struct (newState, _) = handle thinking (AgentResult(Guid.Empty, "s1", 0.0, TimeSpan.Zero, "opus", "done", false))

        // Assert
        %(session newState).ThinkingTokens.Should().Be(0)

    [<Fact>]
    let ``rate limit event leaves turn state untouched`` () =

        // Act
        let resetsAt = DateTimeOffset.FromUnixTimeSeconds(1780935600L)
        let struct (newState, effects) = handle activeState (AgentRateLimit(Guid.Empty, "five_hour", "allowed", resetsAt, false))

        // Assert
        %(session newState).CurrentTurnId.Value.Should().Be(activeTurnId) |> ignore
        %effects.Length.Should().Be(0)

module FullRestartRequested =

    [<Fact>]
    let ``creates new session and ends old`` () =

        // Arrange
        let oldSessionId = (session readyState).Id

        // Act
        let struct (newState, _) = ConversationUpdate.HandleFullRestart(readyState)

        // Assert
        %newState.Sessions.Count.Should().Be(2)
        let oldSession = newState.Sessions |> Seq.find (fun s -> s.Id = oldSessionId)
        %oldSession.Status.Should().Be(SessionStatus.Ended)

    [<Fact>]
    let ``sets new session as active`` () =

        // Arrange
        let oldSessionId = (session readyState).Id

        // Act
        let struct (newState, _) = ConversationUpdate.HandleFullRestart(readyState)

        // Assert
        %newState.ActiveSessionId.HasValue.Should().BeTrue()
        %(newState.ActiveSessionId.Value <> oldSessionId).Should().BeTrue()
        %(session newState).IsInitActive.Should().BeTrue()

    [<Fact>]
    let ``produces dispose and start effects`` () =

        // Act
        let struct (_, effects) = ConversationUpdate.HandleFullRestart(readyState)

        // Assert
        let hasDispose = effects |> Array.exists (fun e -> e :? DisposeSessionEffect)
        %hasDispose.Should().BeTrue()
        let hasStart = effects |> Array.exists (fun e -> e :? StartNewSessionEffect)
        %hasStart.Should().BeTrue()
        let hasTimeout = effects |> Array.exists (fun e -> e :? ScheduleInitTimeoutEffect)
        %hasTimeout.Should().BeTrue()

module EmptyState =

    [<Fact>]
    let ``has expected initial values`` () =

        // Assert
        %(session emptyState).Model.Should().BeNull()
        %(session emptyState).Status.Should().Be(SessionStatus.Idle)
        %(session emptyState).IsProcessing.Should().BeFalse()
        %(session emptyState).ContextSize.Should().Be(200_000)
        %(session emptyState).ContextFilled.Should().Be(0)
        %(session emptyState).IsInitActive.Should().BeTrue()
        %(session emptyState).IsCurrentTurnActive.Should().BeFalse()

module PureHelpers =

    [<Theory>]
    [<InlineData("Tool denied by user", true)>]
    [<InlineData("not permitted", true)>]
    [<InlineData("rejected", true)>]
    [<InlineData("cancelled", true)>]
    [<InlineData("success", false)>]
    [<InlineData("wrote file", false)>]
    let ``IsToolResultDenied detects denied summaries`` (summary: string, expected: bool) =

        // Act / Assert
        %ConversationUpdate.IsToolResultDenied(summary).Should().Be(expected)

    [<Fact>]
    let ``EstimateTokens returns quarter of text length`` () =

        // Act / Assert
        %PromptAnalysis.EstimateTokens("hello world!").Should().Be(3)
        %PromptAnalysis.EstimateTokens("").Should().Be(0)
        %PromptAnalysis.EstimateTokens("ab").Should().Be(1)

module StartupNarration =

    let private initTurnItems (state: ConversationState) =
        let activeSession = session state
        let initTurnId = activeSession.InitTurnId.Value
        (activeSession.Turns |> Seq.find (fun turn -> turn.Id = initTurnId)).Items

    [<Fact>]
    let ``plugin failure lands as an error row in the init turn`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandlePluginFailure(emptyState, "ClaudeBridge", "compile failed")

        // Assert
        let error =
            initTurnItems newState
            |> Seq.pick (fun item ->
                match item with
                | :? ErrorItem as errorItem -> Some errorItem
                | _ -> None)
        %error.Message.Should().Contain("ClaudeBridge")
        %error.Message.Should().Contain("compile failed")

    [<Fact>]
    let ``plugin failure after init leaves the state unchanged`` () =

        // Act
        let struct (newState, _) = ConversationUpdate.HandlePluginFailure(readyState, "X", "y")

        // Assert
        %(obj.ReferenceEquals(newState, readyState)).Should().BeTrue()
