module FabioSoft.Nucleus.WpfHost.Tests.ShutdownBarrierTests

open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``with nothing declared the quit is not held at all`` () =

    // Arrange - the common case: no plugin needs a moment on the way out
    let barrier = ShutdownBarrier()

    // Act & Assert - satisfied from the outset, so quitting stays as immediate as it was before the barrier
    %barrier.BeginPreparing().Should().BeTrue()
    %barrier.IsSatisfied.Should().BeTrue()

[<Fact>]
let ``a declared participant holds the quit until it answers`` () =

    // Arrange
    let barrier = ShutdownBarrier()
    barrier.Declare "Workspaces"

    // Act & Assert
    %barrier.BeginPreparing().Should().BeTrue()
    %barrier.IsSatisfied.Should().BeFalse()
    %barrier.Ready("Workspaces").Should().BeTrue()
    %barrier.IsSatisfied.Should().BeTrue()

[<Fact>]
let ``every participant has to answer, not just one`` () =

    // Arrange
    let barrier = ShutdownBarrier()
    barrier.Declare "Workspaces"
    barrier.Declare "Configuration"

    // Act & Assert - the first answer must not open the barrier for the other
    %barrier.Ready("Workspaces").Should().BeFalse()
    %barrier.Ready("Configuration").Should().BeTrue()

[<Fact>]
let ``an answer from something that never declared itself is harmless`` () =

    // Arrange - a plugin may answer defensively, or declare in a later version than it answers in
    let barrier = ShutdownBarrier()
    barrier.Declare "Workspaces"

    // Act & Assert
    %barrier.Ready("Stranger").Should().BeFalse()
    %barrier.Ready("Workspaces").Should().BeTrue()

[<Fact>]
let ``declaring the same participant twice does not double the wait`` () =

    // Arrange - a plugin reloaded at runtime declares itself again
    let barrier = ShutdownBarrier()
    barrier.Declare "Workspaces"
    barrier.Declare "Workspaces"

    // Act & Assert
    %barrier.Ready("Workspaces").Should().BeTrue()

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
let ``a nameless participant is ignored rather than becoming an unanswerable wait`` (pluginId: string) =

    // Arrange - nothing could ever answer for it, so accepting it would make the application unquittable except
    // by timeout
    let barrier = ShutdownBarrier()

    // Act
    barrier.Declare pluginId

    // Assert
    %barrier.IsSatisfied.Should().BeTrue()

[<Fact>]
let ``quitting twice does not restart the barrier`` () =

    // Arrange - closing the window while a quit is already under way, or closing it and then using the palette
    let barrier = ShutdownBarrier()
    barrier.Declare "Workspaces"

    // Act & Assert
    %barrier.BeginPreparing().Should().BeTrue()
    %barrier.BeginPreparing().Should().BeFalse()

[<Fact>]
let ``the application is only ever told to exit once`` () =

    // Arrange - the grace period and the last answer can land together; a second ApplicationShutdown after the
    // dispatcher has begun shutting down is at best noise
    let barrier = ShutdownBarrier()

    // Act & Assert
    %barrier.TryExit().Should().BeTrue()
    %barrier.TryExit().Should().BeFalse()

[<Fact>]
let ``the outstanding participants are named for the log`` () =

    // Arrange - the difference between a diagnosable pause and an unexplained one
    let barrier = ShutdownBarrier()
    barrier.Declare "Workspaces"
    barrier.Declare "Configuration"
    barrier.Ready "Configuration" |> ignore

    // Act & Assert
    %barrier.Outstanding.Should().SequenceEqual([ "Workspaces" ])
