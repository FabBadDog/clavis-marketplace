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

[<Fact>]
let ``ResumeSession starts a session over the given transcript`` () =

    task {
        // Arrange - a workspace picking its own conversation back up on the next launch. Nothing holds the
        // session, so unlike adoption there is nothing to stop first.
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, _ = createMockSession ()
        let launched = TaskCompletionSource<SessionConfig>()
        plugin.SessionFactory <- Func<_, _>(fun config ->
            launched.TrySetResult(config) |> ignore
            mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        // Act
        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()
        bus.Send(ResumeSession(Guid.NewGuid(), ".", "provider-session-7", "Reviews"))

        let! config = launched.Task.WaitAsync(timeout)

        // Assert - resumed by the provider's own session id, and still named so it stays reclaimable
        %config.ResumeSessionId.Should().BeSome().WhoseValue.Should().Be("provider-session-7")
        %config.Name.Should().BeSome().WhoseValue.Should().Be("clavis/Reviews")

        handle.Dispose()
    }

[<Fact>]
let ``ResumeSession without a provider session id fails rather than starting a fresh conversation`` () =

    task {
        // Arrange - resuming nothing would silently give the user an empty chat where their history should be,
        // which is worse than saying it could not be done
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, _ = createMockSession ()
        let mutable launches = 0
        plugin.SessionFactory <- Func<_, _>(fun _ ->
            launches <- launches + 1
            mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let failed = TaskCompletionSource<AgentInstanceAdoptionFailed>()
        let sub = bus.Subscribe<AgentInstanceAdoptionFailed>(Func<_, _>(fun msg ->
            failed.TrySetResult(msg) |> ignore
            Task.CompletedTask))

        // Act
        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()
        let sessionId = Guid.NewGuid()
        bus.Send(ResumeSession(sessionId, ".", "", "Reviews"))

        let! reported = failed.Task.WaitAsync(timeout)

        // Assert
        %reported.SessionId.Should().Be(sessionId)
        %launches.Should().Be(0)

        sub.Dispose()
        handle.Dispose()
    }

[<Fact>]
let ``adopting an instance nothing discovered fails and releases the claim`` () =

    task {
        // Arrange - the short handle needed to stop an agent is only ever learned from a discovery pass, so an
        // instance that was never discovered cannot be stopped and therefore cannot be taken over. Force skips
        // the wait for a busy turn, not the stop - there is no way to resume a session another process holds.
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, _, _ = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let failed = TaskCompletionSource<AgentInstanceAdoptionFailed>()
        let sub = bus.Subscribe<AgentInstanceAdoptionFailed>(Func<_, _>(fun msg ->
            failed.TrySetResult(msg) |> ignore
            Task.CompletedTask))

        // Act
        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()
        let sessionId = Guid.NewGuid()
        bus.Send(AdoptAgentInstance("never-seen", sessionId, true))

        let! reported = failed.Task.WaitAsync(TimeSpan.FromSeconds(30.0))

        // Assert
        %reported.InstanceId.Should().Be("never-seen")
        %reported.SessionId.Should().Be(sessionId)

        sub.Dispose()
        handle.Dispose()
    }

[<Fact>]
let ``a session that fails before reporting ready is surfaced, not swallowed`` () =

    task {
        // Arrange - resuming a conversation the provider no longer has ends exactly this way. Without a message
        // the failure is total and silent: no SessionReady means no prompt input, and the user is left with a
        // chat that cannot be typed into and nothing explaining why.
        use bus = new Bus(BusConfig.defaultConfig)
        let plugin = ClaudeBridgePlugin()
        let mockSession, output, _ = createMockSession ()
        plugin.SessionFactory <- Func<_, _>(fun _ -> mockSession)
        plugin.UsageFetcher <- Func<_>(fun () -> Task.FromResult Array.empty<UsageWindow>)

        let failed = TaskCompletionSource<SessionStartFailed>()
        let sub = bus.Subscribe<SessionStartFailed>(Func<_, _>(fun msg ->
            failed.TrySetResult(msg) |> ignore
            Task.CompletedTask))

        let started = TaskCompletionSource<SessionStarted>()
        let startedSub = bus.Subscribe<SessionStarted>(Func<_, _>(fun msg ->
            started.TrySetResult(msg) |> ignore
            Task.CompletedTask))

        let! handle = plugin.ActivateAsync(bus, ClaudeBridgeConfig())
        bus.FlushBootstrapBuffer()
        let sessionId = Guid.NewGuid()
        bus.Send(ResumeSession(sessionId, ".", "gone-forever", "Reviews"))

        // The bus delivers asynchronously, so wait until the session actually exists before feeding its stream
        let! _ = started.Task.WaitAsync(timeout)

        // Act - an error result arrives having never reported the session ready
        let resultEvent =
            StreamEvent.Result
                { SessionId = SessionId "gone-forever"
                  CostUsd = 0.0
                  Duration = TimeSpan.Zero
                  Model = ""
                  ResultText = "No conversation found with session ID: gone-forever"
                  IsError = true
                  NumTurns = 0 }
        output.OnNext(Ok resultEvent)

        let! reported = failed.Task.WaitAsync(timeout)

        // Assert
        %reported.SessionId.Should().Be(sessionId)
        %reported.Reason.Should().Contain("No conversation found")

        startedSub.Dispose()
        sub.Dispose()
        handle.Dispose()
    }
