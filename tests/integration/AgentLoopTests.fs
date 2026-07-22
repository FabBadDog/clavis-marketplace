module FabioSoft.Clavis.IntegrationTests.AgentLoopTests

open System
open System.Threading.Tasks
open Faqt
open Faqt.Operators
open FabioSoft.Claude
open FabioSoft.Clavis.TestKit
open FabioSoft.Contracts.Session
open FabioSoft.Contracts.Host
open Xunit

[<Fact>]
let ``init handshake sends Initialize to the agent and readies the session`` () =
    task {
        // Arrange
        let agent = MockAgent.create () |> MockAgent.onInitialize [ Reply.init "opus-4" ]

        // Act
        use! harness = ConversationHarness.start agent
        let! ready = harness.WaitFor<SessionReady>(fun _ -> true)

        // Assert
        %ready.Model.Should().Be("opus-4")
        %(agent.Received |> List.contains SessionInput.Initialize).Should().BeTrue()
    }

[<Fact>]
let ``a prompt is forwarded to the agent and its streamed reply is published`` () =
    task {
        // Arrange
        let agent =
            MockAgent.create ()
            |> MockAgent.onPrompt (fun _ ->
                [ Reply.text "Hello "
                  Reply.text "world"
                  Reply.says "Hello world"
                  Reply.result 0.02 ])

        use! harness = ConversationHarness.start agent
        let! _ = harness.WaitFor<SessionReady>(fun _ -> true)

        // Act
        harness.Send(UserSubmittedPrompt "hi")
        let! _ = harness.WaitFor<AgentResult>(fun _ -> true)

        // Assert
        %agent.ReceivedPrompts.Should().Contain("hi")
        %(harness.Messages<AgentTextDelta>() |> List.map _.Text).Should().Contain("Hello ")
        %(harness.Messages<AgentAssistant>() |> List.map _.Text).Should().Contain("Hello world")
    }

[<Fact>]
let ``tool use and result are mapped through to the bus`` () =
    task {
        // Arrange
        let agent =
            MockAgent.create ()
            |> MockAgent.onPrompt (fun _ ->
                [ Reply.toolUse "Read" """{"file":"a.txt"}"""
                  Reply.toolResult "tool-1" "file contents"
                  Reply.says "done"
                  Reply.result 0.0 ])

        use! harness = ConversationHarness.start agent
        let! _ = harness.WaitFor<AgentInit>(fun _ -> true)

        // Act
        harness.Send(UserSubmittedPrompt "read it")
        let! _ = harness.WaitFor<AgentResult>(fun _ -> true)

        // Assert
        %(harness.Messages<AgentToolUse>() |> List.map _.ToolName).Should().Contain("Read")
        %(harness.Messages<AgentToolResult>() |> List.map _.Summary).Should().Contain("file contents")
    }

[<Fact>]
let ``a permission request is surfaced and the decision is relayed to the agent`` () =
    task {
        // Arrange
        let agent =
            MockAgent.create ()
            |> MockAgent.onPrompt (fun _ -> [ Reply.permissionRequest "req-1" "Bash" """{"cmd":"ls"}""" ])
            |> MockAgent.onPermissionResponse (fun _ -> [ Reply.says "ran ls"; Reply.result 0.0 ])

        use! harness = ConversationHarness.start agent
        let! _ = harness.WaitFor<AgentInit>(fun _ -> true)
        harness.Send(UserSubmittedPrompt "run ls")
        let! request = harness.WaitFor<AgentPermissionRequest>(fun _ -> true)

        // The permission request reaches Conversation on the AgentStreamEvent channel; give that channel a
        // moment to record it before the decision arrives on its own channel (no cross-channel ordering).
        do! Task.Delay 100

        // Act
        harness.Send(PermissionDecided(request.RequestId, "allow"))
        let! _ = harness.WaitFor<AgentAssistant>(fun message -> message.Text = "ran ls")

        // Assert
        %request.ToolName.Should().Be("Bash")
        %request.RequestId.Should().Be("req-1")

        let answered =
            agent.Received
            |> List.exists (function
                | SessionInput.PermissionResponse(requestId, PermissionDecision.Allow _) -> requestId = "req-1"
                | _ -> false)

        %answered.Should().BeTrue()
    }

[<Fact>]
let ``aborting a running turn interrupts the agent`` () =
    task {
        // Arrange: the agent starts the turn but never finishes it, so the turn stays running until aborted.
        let agent = MockAgent.create () |> MockAgent.onPrompt (fun _ -> [ Reply.text "working..." ])

        use! harness = ConversationHarness.start agent
        let! _ = harness.WaitFor<AgentInit>(fun _ -> true)
        harness.Send(UserSubmittedPrompt "long task")
        // The SendPrompt effect proves Conversation processed the submit and a turn is now running.
        let! _ = harness.WaitFor<SendPrompt>(fun message -> message.Text = "long task")

        // Act
        harness.Send(UserAborted())
        // AgentAborted is the mock's reply to receiving Interrupt, so it happens-after the forward we assert.
        let! _ = harness.WaitFor<AgentAborted>(fun _ -> true)

        // Assert
        %(agent.Received |> List.contains SessionInput.Interrupt).Should().BeTrue()
    }

[<Fact>]
let ``a queued prompt is held while a turn runs and dispatched after it completes`` () =
    task {
        // Arrange: "first" only streams text (a turn completes on the result message, never on assistant
        // text alone), so it stays running until we end it; "second" completes itself the moment it runs.
        let agent =
            MockAgent.create ()
            |> MockAgent.onPrompt (fun text ->
                match text with
                | "second" -> [ Reply.says "second done"; Reply.result 0.0 ]
                | _ -> [ Reply.text "working on first" ])

        use! harness = ConversationHarness.start agent
        let! _ = harness.WaitFor<AgentInit>(fun _ -> true)

        harness.Send(UserSubmittedPrompt "first")
        let! _ = harness.WaitFor<SendPrompt>(fun message -> message.Text = "first")

        // Act: submit the second while the first turn is still running - it must be queued, not forwarded.
        harness.Send(UserSubmittedPrompt "second")

        let! heldWhileRunning =
            task {
                try
                    let! _ = harness.WaitFor<SendPrompt>((fun message -> message.Text = "second"), TimeSpan.FromMilliseconds 250.0)
                    return false
                with :? TimeoutException ->
                    return true
            }

        // End the first turn with the result message; the queued prompt is now released to the agent.
        agent.Emit [ Reply.says "first done"; Reply.result 0.0 ]
        let! _ = harness.WaitFor<SendPrompt>(fun message -> message.Text = "second")
        // Wait until the agent has actually run "second" (it replies "second done") before asserting the
        // received-prompt order, so the check does not race the bridge forwarding the promoted prompt.
        let! _ = harness.WaitFor<AgentAssistant>(fun message -> message.Text = "second done")

        // Assert
        %heldWhileRunning.Should().BeTrue()
        %agent.ReceivedPrompts.Should().SequenceEqual([ "first"; "second" ])
    }

[<Fact>]
let ``an ignorable parsing error is mapped and published`` () =
    task {
        // Arrange
        let agent = MockAgent.create ()
        use! harness = ConversationHarness.start agent
        let! _ = harness.WaitFor<AgentInit>(fun _ -> true)

        // Act
        agent.EmitError(ParsingError.UnknownMessageType "weird_type")
        let! error = harness.WaitFor<AgentParsingError>(fun _ -> true)

        // Assert
        %error.IsIgnorable.Should().BeTrue()
    }

[<Fact>]
let ``a session-ended event is mapped and published`` () =
    task {
        // Arrange
        let agent = MockAgent.create ()
        use! harness = ConversationHarness.start agent
        let! _ = harness.WaitFor<AgentInit>(fun _ -> true)

        // Act
        agent.Emit [ Reply.ended 0 "goodbye" ]
        let! ended = harness.WaitFor<AgentSessionEnded>(fun _ -> true)

        // Assert
        %ended.ExitCode.Should().Be(0)
        %ended.Detail.Should().Be("goodbye")
    }

[<Fact>]
let ``a full restart disposes the old session and starts a new one`` () =
    task {
        // Arrange
        let agent = MockAgent.create ()
        use! harness = ConversationHarness.start agent
        let! firstStart = harness.WaitFor<StartNewSession>(fun _ -> true)
        let! _ = harness.WaitFor<AgentInit>(fun _ -> true)

        // Act
        harness.Send(FullRestartRequested())
        let! disposed = harness.WaitFor<DisposeSession>(fun message -> message.SessionId = firstStart.SessionId)
        let! restarted = harness.WaitFor<StartNewSession>(fun message -> message.SessionId <> firstStart.SessionId)

        // Assert
        %disposed.SessionId.Should().Be(firstStart.SessionId)
        %restarted.SessionId.Should().NotBe(firstStart.SessionId)
    }

[<Fact>]
let ``account usage is polled and published as a usage report`` () =
    task {
        // Arrange: a non-empty usage fetcher; the poller's first tick is ~2s in, so allow a generous wait.
        let windows =
            [| { Name = "5-Hour"
                 Utilization = 42.0
                 WindowStart = DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero)
                 ResetsAt = DateTimeOffset(2026, 6, 4, 5, 0, 0, TimeSpan.Zero) } |]

        let agent = MockAgent.create ()
        use! harness = ConversationHarness.startWithUsage windows agent

        // Act
        let! report = harness.WaitFor<AgentUsageReport>((fun _ -> true), TimeSpan.FromSeconds 6.0)

        // Assert
        %report.Windows.Count.Should().BeGreaterThan(0)
    }
