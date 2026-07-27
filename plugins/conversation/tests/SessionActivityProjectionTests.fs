module FabioSoft.Nucleus.Conversation.Tests.SessionActivityProjectionTests

open System
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

let private sessionWith status (items: TurnItem[]) =
    let turn = Turn(Id = Guid.NewGuid(), Prompt = "p", Items = items)
    SessionState.Create().WithInitState(null).WithStatus(status).WithTurns([| turn |])

let private plain status = sessionWith status [||]

let private tool name isActive startedAt =
    ToolItem(Tool(ToolUseId = name, Name = name, IsActive = isActive, StartedAt = startedAt)) :> TurnItem

let private permission isResolved toolUseId =
    PermissionItem(Permission(RequestId = "r1", IsResolved = isResolved, ToolUseId = toolUseId)) :> TurnItem

[<Theory>]
[<InlineData(SessionStatus.Idle)>]
[<InlineData(SessionStatus.Ready)>]
let ``a session with nothing running is idle`` (status: SessionStatus) =
    // Act & Assert
    %SessionActivityProjection.ActivityOf(plain status).Should().Be(SessionActivity.Idle)

[<Theory>]
[<InlineData(SessionStatus.Thinking)>]
[<InlineData(SessionStatus.Retrying)>]
[<InlineData(SessionStatus.Compacting)>]
let ``a session mid-turn is working`` (status: SessionStatus) =
    // Act & Assert
    %SessionActivityProjection.ActivityOf(plain status).Should().Be(SessionActivity.Working)

[<Theory>]
[<InlineData(SessionStatus.Ended)>]
[<InlineData(SessionStatus.Aborted)>]
[<InlineData(SessionStatus.Aborting)>]
let ``a finished session is idle, not working`` (status: SessionStatus) =
    // Act & Assert
    %SessionActivityProjection.ActivityOf(plain status).Should().Be(SessionActivity.Idle)

// The case the whole projection exists for: the turn is still "Thinking", but the agent cannot proceed
// until the user answers, and that has to read differently from ordinary work.
[<Fact>]
let ``an unresolved permission outranks a running turn`` () =
    // Arrange
    let session = sessionWith SessionStatus.Thinking [| permission false "tu1" |]

    // Act & Assert
    %SessionActivityProjection.ActivityOf(session).Should().Be(SessionActivity.Waiting)

[<Fact>]
let ``a resolved permission leaves the session working`` () =
    // Arrange
    let session = sessionWith SessionStatus.Thinking [| permission true "tu1" |]

    // Act & Assert
    %SessionActivityProjection.ActivityOf(session).Should().Be(SessionActivity.Working)

// An unanswered prompt on a session that has ended is not waiting for anyone.
[<Fact>]
let ``a terminal status outranks an unresolved permission`` () =
    // Arrange
    let session = sessionWith SessionStatus.Ended [| permission false "tu1" |]

    // Act & Assert
    %SessionActivityProjection.ActivityOf(session).Should().Be(SessionActivity.Idle)

[<Fact>]
let ``a running turn counts as working even when the status has not caught up`` () =
    // Arrange
    let turnId = Guid.NewGuid()
    let turn = Turn(Id = turnId, Prompt = "p", Status = Running())
    let session =
        SessionState.Create().WithInitState(null).WithStatus(SessionStatus.Ready)
            .WithTurns([| turn |]).WithCurrentTurnId(Nullable turnId)

    // Act & Assert
    %SessionActivityProjection.ActivityOf(session).Should().Be(SessionActivity.Working)

[<Fact>]
let ``the waiting detail names the tool the permission is for`` () =
    // Arrange
    let session = sessionWith SessionStatus.Thinking [| tool "Write" false DateTime.UtcNow; permission false "Write" |]

    // Act & Assert
    %SessionActivityProjection.ActivityDetailOf(session).Should().Be("permission: Write")

[<Fact>]
let ``the waiting detail falls back when the permission names no tool`` () =
    // Arrange
    let session = sessionWith SessionStatus.Thinking [| permission false null |]

    // Act & Assert
    %SessionActivityProjection.ActivityDetailOf(session).Should().Be("permission")

[<Fact>]
let ``the working detail is the active tool`` () =
    // Arrange
    let session = sessionWith SessionStatus.Thinking [| tool "Bash" true DateTime.UtcNow |]

    // Act & Assert
    %SessionActivityProjection.ActivityDetailOf(session).Should().Be("Bash")

// A turn accumulates tool rows; the one that started last is the one actually occupying the agent.
[<Fact>]
let ``the working detail picks the newest active tool`` () =
    // Arrange
    let earlier = DateTime.UtcNow.AddSeconds -30.0
    let session =
        sessionWith SessionStatus.Thinking
            [| tool "Read" true earlier; tool "Bash" true (DateTime.UtcNow) |]

    // Act & Assert
    %SessionActivityProjection.ActivityDetailOf(session).Should().Be("Bash")

[<Fact>]
let ``a finished tool is not reported as the active one`` () =
    // Arrange
    let session = sessionWith SessionStatus.Thinking [| tool "Read" false DateTime.UtcNow |]

    // Act & Assert
    %SessionActivityProjection.ActivityDetailOf(session).Should().Be("thinking")

[<Theory>]
[<InlineData(SessionStatus.Thinking, "thinking")>]
[<InlineData(SessionStatus.Retrying, "retrying")>]
[<InlineData(SessionStatus.Compacting, "compacting")>]
let ``the working detail falls back to the phase word with no active tool``
    (status: SessionStatus) (expected: string) =

    // Act & Assert
    %SessionActivityProjection.ActivityDetailOf(plain status).Should().Be(expected)

[<Fact>]
let ``an idle session has no detail`` () =
    // Act & Assert
    %SessionActivityProjection.ActivityDetailOf(plain SessionStatus.Ready).Should().Be("")
