module FabioSoft.Nucleus.Conversation.Tests.AgentValuesTests

open System
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``builds agent values from the session`` () =

    // Arrange
    let state =
        SessionState(
            Model = "opus-4.8",
            Mode = "plan",
            Effort = "high",
            Status = SessionStatus.Ready,
            ContextFilled = 128_000,
            ContextSize = 200_000)

    // Act
    let values = AgentValues.Build(state)

    // Assert
    %values["agent.model"].Should().Be("opus-4.8")
    %values["agent.mode"].Should().Be("plan")
    %values["agent.effort"].Should().Be("high")
    %values["agent.contextPercent"].Should().Be("64")
    %values["agent.contextUsedShort"].Should().Be("128k")
    %values["agent.contextWindowShort"].Should().Be("200k")

[<Fact>]
let ``builds turn values from the active turn`` () =

    // Arrange
    let turnId = Guid.NewGuid()
    let turn = Turn(Id = turnId, TotalTokens = 3400, Duration = TimeSpan.FromSeconds 8.2)

    let state =
        SessionState(
            Model = "opus-4.8",
            Status = SessionStatus.Ready,
            ContextFilled = 128_000,
            ContextSize = 200_000,
            LastTurnId = Nullable turnId,
            Turns = [| turn |])

    // Act
    let values = AgentValues.Build(state)

    // Assert
    %values["turn.tokens"].Should().Be("3400")
    %values["turn.runtime"].Should().Be("8.2s")

[<Fact>]
let ``returns no values for a missing session`` () =
    %AgentValues.Build(null).Count.Should().Be(0)
