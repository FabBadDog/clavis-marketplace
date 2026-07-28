module FabioSoft.Nucleus.ClaudeBridge.Tests.TurnGateTests

open System
open System.Threading.Tasks
open FabioSoft.Nucleus.Plugins.ClaudeBridge
open Faqt
open Faqt.Operators
open Xunit

let private instant = TimeSpan.FromMilliseconds 50.0

[<Fact>]
let ``a session with no turn in flight is idle`` () =

    // Arrange
    let gate = TurnGate()

    // Act & Assert
    %gate.IsRunning(Guid.NewGuid()).Should().BeFalse()

[<Fact>]
let ``a started turn runs until it finishes`` () =

    // Arrange
    let gate = TurnGate()
    let sessionId = Guid.NewGuid()

    // Act
    gate.Started sessionId

    // Assert
    %gate.IsRunning(sessionId).Should().BeTrue()
    gate.Finished sessionId
    %gate.IsRunning(sessionId).Should().BeFalse()

[<Fact>]
let ``turns are tracked per session`` () =

    // Arrange
    let gate = TurnGate()
    let busy = Guid.NewGuid()
    let idle = Guid.NewGuid()

    // Act
    gate.Started busy

    // Assert
    %gate.IsRunning(idle).Should().BeFalse()

[<Fact>]
let ``waiting on an idle session returns at once`` () =

    // Arrange
    let gate = TurnGate()

    // Act
    let idle = gate.WaitForIdleAsync(Guid.NewGuid(), instant).Result

    // Assert
    %idle.Should().BeTrue()

[<Fact>]
let ``waiting on a running turn times out rather than blocking forever`` () =

    // Arrange - a wedged turn must not hold up shutdown
    let gate = TurnGate()
    let sessionId = Guid.NewGuid()
    gate.Started sessionId

    // Act
    let idle = gate.WaitForIdleAsync(sessionId, instant).Result

    // Assert
    %idle.Should().BeFalse()

[<Fact>]
let ``a waiter is released when the turn finishes`` () =

    // Arrange
    let gate = TurnGate()
    let sessionId = Guid.NewGuid()
    gate.Started sessionId

    // Act
    let waiting = gate.WaitForIdleAsync(sessionId, TimeSpan.FromSeconds 5.0)
    gate.Finished sessionId

    // Assert
    %waiting.Result.Should().BeTrue()

[<Fact>]
let ``a second prompt mid-turn does not signal the first turn as done`` () =

    // Arrange - replacing the completion would let an early finish report idle while the first turn runs on
    let gate = TurnGate()
    let sessionId = Guid.NewGuid()
    gate.Started sessionId

    // Act
    let waiting = gate.WaitForIdleAsync(sessionId, TimeSpan.FromSeconds 5.0)
    gate.Started sessionId

    // Assert
    %waiting.IsCompleted.Should().BeFalse()
    gate.Finished sessionId
    %waiting.Result.Should().BeTrue()
