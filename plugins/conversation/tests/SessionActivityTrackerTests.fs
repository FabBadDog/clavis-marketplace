module FabioSoft.Nucleus.Conversation.Tests.SessionActivityTrackerTests

open System
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

let private now = DateTimeOffset(DateTime(2026, 7, 27, 8, 0, 0, DateTimeKind.Utc))
let private later = now.AddSeconds 30.0

let private sessionWith status (items: TurnItem[]) =
    let turn = Turn(Id = Guid.NewGuid(), Prompt = "p", Items = items)
    SessionState.Create().WithInitState(null).WithStatus(status).WithTurns([| turn |])

let private tool name = ToolItem(Tool(ToolUseId = name, Name = name, IsActive = true)) :> TurnItem

[<Fact>]
let ``the first look at a session always announces`` () =
    // Arrange
    let tracker = SessionActivityTracker()

    // Act
    let announced = tracker.Next(sessionWith SessionStatus.Ready [||], now)

    // Assert
    %(isNull (box announced)).Should().BeFalse()
    %announced.Activity.Should().Be(SessionActivity.Idle)
    %announced.Since.Should().Be(now)

// A streaming turn rewrites conversation state many times a second; the indicator changes far less often.
[<Fact>]
let ``an unchanged session announces nothing the second time`` () =
    // Arrange
    let tracker = SessionActivityTracker()
    let session = sessionWith SessionStatus.Thinking [||]
    tracker.Next(session, now) |> ignore

    // Act
    let again = tracker.Next(session, later)

    // Assert
    %(isNull (box again)).Should().BeTrue()

[<Fact>]
let ``a change of activity announces with a fresh timestamp`` () =
    // Arrange
    let tracker = SessionActivityTracker()
    let id = Guid.NewGuid()
    let idle = SessionState.Create().WithInitState(null).WithStatus(SessionStatus.Ready).WithTurns([||])
    let working = idle.WithStatus(SessionStatus.Thinking)
    tracker.Next(idle, now) |> ignore

    // Act
    let announced = tracker.Next(working, later)

    // Assert
    %announced.Activity.Should().Be(SessionActivity.Working)
    %announced.Since.Should().Be(later)

// Since marks when the state was entered, so an overview can render "working 4m". Moving from one tool to
// the next is still the same working stretch and must not restart that clock.
[<Fact>]
let ``a new tool within the same working stretch keeps the original timestamp`` () =
    // Arrange
    let tracker = SessionActivityTracker()
    let reading = sessionWith SessionStatus.Thinking [| tool "Read" |]
    let bashing = reading.WithTurns([| Turn(Id = Guid.NewGuid(), Prompt = "p", Items = [| tool "Bash" |]) |])
    tracker.Next(reading, now) |> ignore

    // Act
    let announced = tracker.Next(bashing, later)

    // Assert - the detail moved, so it announces, but the clock did not restart
    %announced.Detail.Should().Be("Bash")
    %announced.Since.Should().Be(now)

[<Fact>]
let ``sessions are tracked independently`` () =
    // Arrange - two sessions seen once each, then only the first changes
    let tracker = SessionActivityTracker()
    let first = sessionWith SessionStatus.Ready [||]
    let second = sessionWith SessionStatus.Ready [||]
    tracker.Next(first, now) |> ignore
    tracker.Next(second, now) |> ignore

    // Act
    let firstAgain = tracker.Next(first.WithStatus(SessionStatus.Thinking), later)
    let secondAgain = tracker.Next(second, later)

    // Assert
    %firstAgain.Activity.Should().Be(SessionActivity.Working)
    %(isNull (box secondAgain)).Should().BeTrue()

[<Fact>]
let ``a forgotten session announces again when seen next`` () =
    // Arrange
    let tracker = SessionActivityTracker()
    let session = sessionWith SessionStatus.Ready [||]
    tracker.Next(session, now) |> ignore

    // Act
    tracker.Forget session.Id
    let announced = tracker.Next(session, later)

    // Assert
    %(isNull (box announced)).Should().BeFalse()
    %announced.Since.Should().Be(later)
