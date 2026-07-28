module FabioSoft.Nucleus.ClaudeBridge.Tests.AgentInstanceRegistryTests

open System
open FabioSoft.Nucleus.Plugins.ClaudeBridge
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``claiming an instance records it against the session`` () =

    // Arrange
    let registry = AgentInstanceRegistry()
    let sessionId = Guid.NewGuid()

    // Act
    let claimed = registry.TryClaim("instance-1", sessionId)

    // Assert
    %claimed.Should().BeTrue()
    %registry.InstanceOf(sessionId).Should().Be("instance-1")
    %registry.IsAdopted("instance-1").Should().BeTrue()

[<Fact>]
let ``a second session cannot claim an instance somebody already holds`` () =

    // Arrange - two processes on one transcript is the corruption this exists to prevent
    let registry = AgentInstanceRegistry()
    let first = Guid.NewGuid()
    let second = Guid.NewGuid()
    %(registry.TryClaim("instance-1", first)).Should().BeTrue()

    // Act
    let claimed = registry.TryClaim("instance-1", second)

    // Assert - the original owner is untouched
    %claimed.Should().BeFalse()
    %registry.InstanceOf(second).Should().BeNull()
    %registry.InstanceOf(first).Should().Be("instance-1")

[<Fact>]
let ``re-claiming the same instance for the same session is not a conflict`` () =

    // Arrange - a session confirms the id it was already given when the provider's init event arrives
    let registry = AgentInstanceRegistry()
    let sessionId = Guid.NewGuid()
    %(registry.TryClaim("instance-1", sessionId)).Should().BeTrue()

    // Act & Assert
    %(registry.TryClaim("instance-1", sessionId)).Should().BeTrue()

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
let ``an instance without an id cannot be claimed`` (instanceId: string) =

    // Arrange - an unaddressable instance would occupy the registry without ever being releasable
    let registry = AgentInstanceRegistry()

    // Act & Assert
    %(registry.TryClaim(instanceId, Guid.NewGuid())).Should().BeFalse()

[<Fact>]
let ``forgetting a session frees its instance for adoption again`` () =

    // Arrange
    let registry = AgentInstanceRegistry()
    let first = Guid.NewGuid()
    %(registry.TryClaim("instance-1", first)).Should().BeTrue()

    // Act
    let forgotten = registry.Forget(first)

    // Assert
    %forgotten.Should().Be("instance-1")
    %registry.IsAdopted("instance-1").Should().BeFalse()
    %(registry.TryClaim("instance-1", Guid.NewGuid())).Should().BeTrue()

[<Fact>]
let ``forgetting a session that holds nothing yields nothing`` () =

    // Arrange
    let registry = AgentInstanceRegistry()

    // Act & Assert
    %registry.Forget(Guid.NewGuid()).Should().BeNull()

[<Fact>]
let ``the adopted set lists every held instance`` () =

    // Arrange
    let registry = AgentInstanceRegistry()
    %(registry.TryClaim("a", Guid.NewGuid())).Should().BeTrue()
    %(registry.TryClaim("b", Guid.NewGuid())).Should().BeTrue()

    // Act & Assert
    %registry.AdoptedInstanceIds.Should().HaveLength(2)
