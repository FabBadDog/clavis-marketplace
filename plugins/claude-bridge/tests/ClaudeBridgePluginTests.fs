module FabioSoft.Nucleus.ClaudeBridge.Tests.ClaudeBridgePluginTests

open System
open System.Reactive
open System.Reactive.Subjects
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open FabioSoft.Claude
open FabioSoft.Nucleus.Bus
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.ClaudeBridge
open Xunit

let private createMockSession () =

    let output = new Subject<Result<StreamEvent, ParsingError>>()
    let sentInputs = System.Collections.Concurrent.ConcurrentBag<SessionInput>()
    let session : Session =
        Subject.Create<SessionInput, Result<StreamEvent, ParsingError>>(
            Observer.Create<SessionInput>(Action<SessionInput>(sentInputs.Add)),
            output)
    session, output, sentInputs

let private timeout = TimeSpan.FromSeconds(2.0)

[<Fact>]
let ``Plugin Id is ClaudeBridge`` () =

    %ClaudeBridgePlugin().Id.Should().Be("ClaudeBridge")

[<Fact>]
let ``Plugin DefaultConfig is not null`` () =

    %ClaudeBridgePlugin().DefaultConfig.Should().NotBeNull()

[<Fact>]
let ``StartNewSession publishes SessionStarted`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, _ = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let sessionStarted = TaskCompletionSource<SessionStarted>()
        let sub = bus.Subscribe<SessionStarted>(Func<_, _>(fun msg ->
            sessionStarted.TrySetResult(msg) |> ignore
            Task.CompletedTask))

        // Act
        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()
        let sessionId = Guid.NewGuid()
        bus.Send(StartNewSession(sessionId, ".", null))

        let! started = sessionStarted.Task.WaitAsync(timeout)

        // Assert
        %started.SessionId.Should().Be(sessionId)

        sub.Dispose()
        handle.Dispose()
    }

[<Fact>]
let ``SendPrompt forwards to session`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, sentInputs = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()

        let sessionId = Guid.NewGuid()
        bus.Send(StartNewSession(sessionId, ".", null))
        do! Task.Delay(100)

        // Act
        bus.Send(SendPrompt(sessionId, "hello"))
        do! Task.Delay(100)

        // Assert
        let prompts =
            sentInputs
            |> Seq.choose (function SessionInput.Prompt text -> Some text | _ -> None)
            |> Seq.toList
        %prompts.Should().Contain("hello")

        handle.Dispose()
    }

[<Fact>]
let ``Stream events are mapped and published on bus`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, output, _ = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let receivedEvent = TaskCompletionSource<AgentStreamEvent>()
        let eventSub = bus.Subscribe<AgentStreamEvent>(Func<_, _>(fun (msg: AgentStreamEvent) ->
            receivedEvent.TrySetResult(msg) |> ignore
            Task.CompletedTask))

        let receivedReady = TaskCompletionSource<SessionReady>()
        let readySub = bus.Subscribe<SessionReady>(Func<_, _>(fun (msg: SessionReady) ->
            receivedReady.TrySetResult(msg) |> ignore
            Task.CompletedTask))

        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()

        let sessionId = Guid.NewGuid()
        bus.Send(StartNewSession(sessionId, ".", null))
        do! Task.Delay(100)

        // Act
        output.OnNext(Ok (StreamEvent.Init(SessionId "test-sess", "opus", [])))
        let! (event: AgentStreamEvent) = receivedEvent.Task.WaitAsync(timeout)
        let! (ready: SessionReady) = receivedReady.Task.WaitAsync(timeout)

        // Assert
        let init = event :?> AgentInit
        %init.AgentSessionId.Should().Be("test-sess")
        %init.Model.Should().Be("opus")
        %ready.AgentSessionId.Should().Be("test-sess")

        eventSub.Dispose()
        readySub.Dispose()
        handle.Dispose()
    }

[<Fact>]
let ``StartNewSession sends Initialize handshake to session`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, sentInputs = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()

        // Act
        bus.Send(StartNewSession(Guid.NewGuid(), ".", null))
        do! Task.Delay(100)

        // Assert
        let hasInitialize =
            sentInputs |> Seq.exists (function SessionInput.Initialize -> true | _ -> false)
        %hasInitialize.Should().BeTrue()

        handle.Dispose()
    }

[<Fact>]
let ``InterruptSession forwards Interrupt to session`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, sentInputs = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()

        let sessionId = Guid.NewGuid()
        bus.Send(StartNewSession(sessionId, ".", null))
        do! Task.Delay(100)

        // Act
        bus.Send(InterruptSession(sessionId))
        do! Task.Delay(100)

        // Assert
        let hasInterrupt =
            sentInputs |> Seq.exists (function SessionInput.Interrupt -> true | _ -> false)
        %hasInterrupt.Should().BeTrue()

        handle.Dispose()
    }

[<Fact>]
let ``DisposeSession forwards Dispose to session`` () =

    task {
        // Arrange
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, sentInputs = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()

        let sessionId = Guid.NewGuid()
        bus.Send(StartNewSession(sessionId, ".", null))
        do! Task.Delay(100)

        // Act
        bus.Send(DisposeSession(sessionId))
        do! Task.Delay(100)

        // Assert
        let hasDispose =
            sentInputs |> Seq.exists (function SessionInput.Dispose -> true | _ -> false)
        %hasDispose.Should().BeTrue()

        handle.Dispose()
    }
