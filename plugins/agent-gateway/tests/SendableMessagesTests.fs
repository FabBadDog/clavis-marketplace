module FabioSoft.Nucleus.AgentGateway.Tests.SendableMessagesTests

open System
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Bus
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Plugins.AgentGateway
open Xunit

let private timeout = TimeSpan.FromSeconds(2.0)

[<Fact>]
let ``a registered message with a field is constructed and dispatched by its concrete type`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let received = TaskCompletionSource<OpenPanel>()
        use _ = bus.Subscribe<OpenPanel>(Func<_, _>(fun message ->
            received.TrySetResult(message) |> ignore
            Task.CompletedTask))
        bus.FlushBootstrapBuffer()

        // Act
        let result = SendableMessages().Send(bus, "OpenPanel", """{"kind":"git-log"}""")
        let! delivered = received.Task.WaitAsync(timeout)

        // Assert
        %result.Ok.Should().BeTrue()
        %delivered.Kind.Should().Be("git-log")
    }

[<Fact>]
let ``a registered parameterless message is dispatched`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let received = TaskCompletionSource<CloseActivePanel>()
        use _ = bus.Subscribe<CloseActivePanel>(Func<_, _>(fun message ->
            received.TrySetResult(message) |> ignore
            Task.CompletedTask))
        bus.FlushBootstrapBuffer()

        // Act
        let result = SendableMessages().Send(bus, "CloseActivePanel", "{}")
        let! _ = received.Task.WaitAsync(timeout)

        // Assert
        %result.Ok.Should().BeTrue()
    }

[<Fact>]
let ``a denied lifecycle message is rejected with a denied reason`` () =

    // Arrange
    use bus = new Bus(BusConfig.defaultConfig)

    // Act
    let result = SendableMessages().Send(bus, "ApplicationShutdown", "{}")

    // Assert
    %result.Ok.Should().BeFalse()
    %result.Message.Should().Contain("denied")

[<Fact>]
let ``an unregistered message is rejected as unsupported`` () =

    // Arrange
    use bus = new Bus(BusConfig.defaultConfig)

    // Act
    let result = SendableMessages().Send(bus, "TotallyMadeUpMessage", "{}")

    // Assert
    %result.Ok.Should().BeFalse()
    %result.Message.Should().Contain("not a supported")

[<Fact>]
let ``invalid JSON is rejected`` () =

    // Arrange
    use bus = new Bus(BusConfig.defaultConfig)

    // Act
    let result = SendableMessages().Send(bus, "OpenPanel", "{not valid json")

    // Assert
    %result.Ok.Should().BeFalse()
    %result.Message.Should().Contain("not valid JSON")

[<Fact>]
let ``a missing required string field is rejected`` () =

    // Arrange
    use bus = new Bus(BusConfig.defaultConfig)

    // Act
    let result = SendableMessages().Send(bus, "OpenPanel", "{}")

    // Assert
    %result.Ok.Should().BeFalse()
    %result.Message.Should().Contain("kind")

[<Fact>]
let ``a malformed GUID field is rejected`` () =

    // Arrange
    use bus = new Bus(BusConfig.defaultConfig)

    // Act
    let result = SendableMessages().Send(bus, "ClosePanel", """{"instanceId":"not-a-guid"}""")

    // Assert
    %result.Ok.Should().BeFalse()
    %result.Message.Should().Contain("instanceId")

[<Fact>]
let ``SupportedTypes lists navigation messages and excludes denied ones`` () =

    // Arrange
    let supported = SendableMessages().SupportedTypes |> Seq.toList

    // Assert
    %supported.Should().Contain("OpenPanel")
    %supported.Should().Contain("UserSubmittedPrompt")
    %supported.Should().NotContain("ApplicationShutdown")
    %supported.Should().NotContain("UnloadPlugin")
