module FabioSoft.Nucleus.Selection.Tests.SessionCapabilitiesTests

open System
open System.Collections.Generic
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.Selection
open Faqt
open Faqt.Operators
open Xunit

let private capabilities sessionId model =
    AgentCapabilities(
        sessionId, model, "", "",
        Array.empty<AgentModelInfo>, Array.empty<AgentModeInfo>, Array.empty<AgentEffortInfo>)

let private snapshots (pairs: (Guid * string) list) : IReadOnlyDictionary<Guid, AgentCapabilities> =
    let map = Dictionary<Guid, AgentCapabilities>()
    for sessionId, model in pairs do
        map[sessionId] <- capabilities sessionId model
    map :> _

[<Fact>]
let ``nothing reported yet offers nothing`` () =

    // Act & Assert
    %Object.ReferenceEquals(SessionCapabilities.Resolve(snapshots [], Guid.NewGuid()), null).Should().BeTrue()

[<Fact>]
let ``the visible session's snapshot wins`` () =

    // Arrange
    let visible = Guid.NewGuid()
    let other = Guid.NewGuid()

    // Act
    let resolved = SessionCapabilities.Resolve(snapshots [ other, "other"; visible, "mine" ], visible)

    // Assert
    %resolved.Model.Should().Be("mine")

[<Fact>]
let ``a sole snapshot is used when no session is visible yet`` () =

    // Arrange - the single-workspace case, which must behave exactly as it did before sessions were told apart
    let only = Guid.NewGuid()

    // Act
    let resolved = SessionCapabilities.Resolve(snapshots [ only, "only" ], Guid.Empty)

    // Assert
    %resolved.Model.Should().Be("only")

[<Fact>]
let ``a sole snapshot is used when the visible session has not reported`` () =

    // Arrange
    let reported = Guid.NewGuid()

    // Act
    let resolved = SessionCapabilities.Resolve(snapshots [ reported, "reported" ], Guid.NewGuid())

    // Assert
    %resolved.Model.Should().Be("reported")

[<Fact>]
let ``several snapshots and no visible session offers nothing rather than an arbitrary one`` () =

    // Arrange
    let pairs = [ Guid.NewGuid(), "a"; Guid.NewGuid(), "b" ]

    // Act & Assert
    %Object.ReferenceEquals(SessionCapabilities.Resolve(snapshots pairs, Guid.Empty), null).Should().BeTrue()

[<Fact>]
let ``a visible session with several snapshots never picks a neighbour`` () =

    // Arrange
    let visible = Guid.NewGuid()
    let pairs = [ Guid.NewGuid(), "a"; Guid.NewGuid(), "b" ]

    // Act & Assert - the visible one has not reported, and with more than one there is no honest fallback
    %Object.ReferenceEquals(SessionCapabilities.Resolve(snapshots pairs, visible), null).Should().BeTrue()
