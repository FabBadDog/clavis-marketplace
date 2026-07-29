module FabioSoft.Claude.Tests.SessionTests

open System
open System.Threading
open System.Threading.Tasks
open FabioSoft.Claude
open FabioSoft.Json
open FabioSoft.Process
open Faqt
open Xunit

module FakeProcess =

    type FakeProcessBridge = {
        Bridge: Process
        PushStdout: string -> unit
        PushStderr: string -> unit
        Complete: int -> unit
        SentLines: ResizeArray<string>
        InterruptCalled: ref<bool>
        ShutdownCalled: ref<bool>
    }

    let create () =

        let stdoutSource = Event<string>()
        let stderrSource = Event<string>()
        let exitSource = TaskCompletionSource<int>()
        let sentLines = ResizeArray<string>()
        let interruptCalled = ref false
        let shutdownCalled = ref false

        let bridge = {
            StandardInput = fun line -> sentLines.Add(line)
            StandardOutput = stdoutSource.Publish
            StandardError = stderrSource.Publish
            Exited = exitSource.Task
            Interrupt = fun () -> interruptCalled.Value <- true
            Shutdown = fun () -> shutdownCalled.Value <- true
        }

        { Bridge = bridge
          PushStdout = stdoutSource.Trigger
          PushStderr = stderrSource.Trigger
          Complete = exitSource.SetResult
          SentLines = sentLines
          InterruptCalled = interruptCalled
          ShutdownCalled = shutdownCalled }

let private collectEvents (session: Session) =

    let events = ResizeArray<Result<StreamEvent, ParsingError>>()
    session.Subscribe(events.Add) |> ignore
    events

[<Literal>]
let private PollIntervalMilliseconds = 10

let private WaitTimeout = TimeSpan.FromSeconds(5.0)

/// A session runs its work on its own MailboxProcessor, so a test cannot know when it has caught up. Waiting
/// for the effect the test is about to assert on is what makes that deterministic - a fixed sleep passes on an
/// idle machine and fails under load, which is exactly how it behaved.
let rec private waitUntil (deadline: DateTime) condition =
    if not (condition ()) && DateTime.UtcNow < deadline then
        Thread.Sleep(PollIntervalMilliseconds)
        waitUntil deadline condition

let private waitFor condition = waitUntil (DateTime.UtcNow.Add(WaitTimeout)) condition

let private waitForEvent (events: ResizeArray<_>) predicate =
    waitFor (fun () ->
        try events.ToArray() |> Array.exists predicate
        with :? InvalidOperationException -> false)

let private waitForLines (fake: FakeProcess.FakeProcessBridge) count =
    waitFor (fun () -> fake.SentLines.Count >= count)

/// For the one assertion that nothing is written: there is no effect to wait for, so this is a bounded pause
/// giving a wrong implementation time to reveal itself rather than a synchronisation point.
let private settle () = Thread.Sleep(100)

[<Fact>]
let ``stdout NDJSON line is parsed into StreamEvent`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session

    // Act
    fake.PushStdout """{"type":"system","subtype":"init","session_id":"s1","model":"opus-4"}"""
    waitForEvent events (function Ok (Init _) -> true | _ -> false)

    // Assert
    events |> Seq.exists (function Ok (Init (SessionId "s1", "opus-4", _)) -> true | _ -> false)
    |> _.Should().BeTrue()

[<Fact>]
let ``stderr line triggers LogMessage event`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session

    // Act
    fake.PushStderr "some warning"
    waitForEvent events (function Ok (LogMessage _) -> true | _ -> false)

    // Assert
    events |> Seq.exists (function Ok (LogMessage "some warning") -> true | _ -> false)
    |> _.Should().BeTrue()

[<Fact>]
let ``process exit triggers SessionEnded event`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session

    // Act
    fake.Complete 0
    waitForEvent events (function Ok (SessionEnded _) -> true | _ -> false)

    // Assert
    events |> Seq.exists (function Ok (SessionEnded (0, "")) -> true | _ -> false)
    |> _.Should().BeTrue()

[<Fact>]
let ``process exit includes stderr in detail`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session

    // Act
    fake.PushStderr "fatal error"
    fake.Complete 1
    waitForEvent events (function Ok (SessionEnded _) -> true | _ -> false)

    // Assert
    events |> Seq.exists (function Ok (SessionEnded (1, "fatal error")) -> true | _ -> false)
    |> _.Should().BeTrue()

[<Fact>]
let ``Send Initialize encodes an initialize control request to stdin`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext Initialize
    waitForLines fake 1

    // Assert
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"type\":\"control_request\"") |> ignore
    sent.Should().Contain("\"subtype\":\"initialize\"")

[<Fact>]
let ``Send Initialize also sends the boot command to force the lazy provider boot`` () =

    // Arrange - in --print stream-json mode the provider emits nothing (no hooks, no init) until the
    // first user message, so Initialize must carry a throwaway local command to boot it eagerly. This
    // is the guard for the recurring "init turn shows only Starting Claude" regression.
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext Initialize
    waitForLines fake 2

    // Assert
    fake.SentLines.Count.Should().Be(2) |> ignore
    let bootLine = fake.SentLines[1]
    bootLine.Should().Contain("\"type\":\"user\"") |> ignore
    bootLine.Should().Contain(Session.BootCommand)

[<Fact>]
let ``Send Prompt encodes user message to stdin`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext(Prompt "hello world")
    waitForLines fake 1

    // Assert
    fake.SentLines.Count.Should().Be(1) |> ignore
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"type\":\"user\"") |> ignore
    sent.Should().Contain("\"text\":\"hello world\"")

[<Fact>]
let ``Send Prompt escapes special characters`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext(Prompt "say \"hi\" and use \\backslash")
    waitForLines fake 1

    // Assert
    fake.SentLines.Count.Should().Be(1) |> ignore
    let sent = fake.SentLines[0]
    sent.Should().Contain("\\\"hi\\\"") |> ignore
    sent.Should().Contain("\\\\backslash")

[<Fact>]
let ``Send PermissionResponse Allow encodes correctly`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext(PermissionResponse ("req-1", Allow []))
    waitForLines fake 1

    // Assert
    fake.SentLines.Count.Should().Be(1) |> ignore
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"type\":\"control_response\"") |> ignore
    sent.Should().Contain("\"request_id\":\"req-1\"") |> ignore
    sent.Should().Contain("\"behavior\":\"allow\"")

[<Fact>]
let ``Send PermissionResponse Deny encodes correctly`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext(PermissionResponse ("req-2", Deny))
    waitForLines fake 1

    // Assert
    fake.SentLines.Count.Should().Be(1) |> ignore
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"behavior\":\"deny\"") |> ignore
    sent.Should().Contain("Denied by user")

[<Fact>]
let ``Send PermissionResponse Allow encodes updatedPermissions`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let updates = [ AddRules([ { ToolName = "Bash"; RuleContent = Some "git*" } ], "allow", "localSettings") ]

    // Act
    session.OnNext(PermissionResponse ("req-3", Allow updates))
    waitForLines fake 1

    // Assert
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"updatedPermissions\":[") |> ignore
    sent.Should().Contain("\"type\":\"addRules\"") |> ignore
    sent.Should().Contain("\"toolName\":\"Bash\"") |> ignore
    sent.Should().Contain("\"ruleContent\":\"git*\"") |> ignore
    sent.Should().Contain("\"destination\":\"localSettings\"")

[<Fact>]
let ``Send SetModel encodes a set_model control request to stdin`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext(SetModel "claude-opus-4-8")
    waitForLines fake 1

    // Assert
    fake.SentLines.Count.Should().Be(1) |> ignore
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"type\":\"control_request\"") |> ignore
    sent.Should().Contain("\"subtype\":\"set_model\"") |> ignore
    sent.Should().Contain("\"model\":\"claude-opus-4-8\"")

[<Fact>]
let ``Send SetPermissionMode encodes a set_permission_mode control request to stdin`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext(SetPermissionMode "plan")
    waitForLines fake 1

    // Assert
    fake.SentLines.Count.Should().Be(1) |> ignore
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"subtype\":\"set_permission_mode\"") |> ignore
    sent.Should().Contain("\"mode\":\"plan\"")

[<Fact>]
let ``Send SetEffort encodes the non-interactive effort command as a user message`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge

    // Act
    session.OnNext(SetEffort "xhigh")
    waitForLines fake 1

    // Assert
    fake.SentLines.Count.Should().Be(1) |> ignore
    let sent = fake.SentLines[0]
    sent.Should().Contain("\"type\":\"user\"") |> ignore
    sent.Should().Contain("/effort xhigh")

[<Fact>]
let ``Send SetModel after process exit sends nothing`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session
    fake.Complete 0
    waitForEvent events (function Ok (SessionEnded _) -> true | _ -> false)

    // Act
    session.OnNext(SetModel "claude-opus-4-8")
    settle ()

    // Assert
    fake.SentLines.Count.Should().Be(0)

[<Fact>]
let ``Send Prompt after process exit triggers SessionAlreadyExited`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session
    fake.Complete 0
    waitForEvent events (function Ok (SessionEnded _) -> true | _ -> false)

    // Act
    session.OnNext(Prompt "too late")
    waitForEvent events (function Ok SessionAlreadyExited -> true | _ -> false)

    // Assert
    events |> Seq.exists (function Ok SessionAlreadyExited -> true | _ -> false)
    |> _.Should().BeTrue()

[<Fact>]
let ``Send Interrupt triggers Aborted event and calls bridge Interrupt`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session

    // Act
    session.OnNext Interrupt
    waitForEvent events (function Ok Aborted -> true | _ -> false)

    // Assert
    events |> Seq.exists (function Ok Aborted -> true | _ -> false)
    |> _.Should().BeTrue() |> ignore
    fake.InterruptCalled.Value.Should().BeTrue()

[<Fact>]
let ``Send Dispose calls bridge Shutdown`` () =

    // Arrange
    let shutdownSignal = new ManualResetEventSlim(false)
    let fake = FakeProcess.create ()
    let bridge =
        { fake.Bridge with
            Shutdown = fun () ->
                fake.ShutdownCalled.Value <- true
                shutdownSignal.Set() }
    let session = Session.toSession bridge

    // Act
    session.OnNext Dispose
    shutdownSignal.Wait(TimeSpan.FromSeconds(2.0)) |> ignore

    // Assert
    fake.ShutdownCalled.Value.Should().BeTrue()

[<Fact>]
let ``Send Dispose completes the event stream`` () =

    // Arrange
    let completedSignal = new ManualResetEventSlim(false)
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let observer =
        { new IObserver<Result<StreamEvent, ParsingError>> with
            member _.OnNext _ = ()
            member _.OnError _ = ()
            member _.OnCompleted() = completedSignal.Set() }
    session.Subscribe(observer) |> ignore

    // Act
    session.OnNext Dispose
    completedSignal.Wait(TimeSpan.FromSeconds(2.0)) |> ignore

    // Assert
    completedSignal.IsSet.Should().BeTrue()

[<Fact>]
let ``invalid NDJSON triggers Status with parsing error`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session

    // Act
    fake.PushStdout "not valid json at all"
    waitForEvent events (function Error (JsonError (MalformedJson _)) -> true | _ -> false)

    // Assert
    events |> Seq.exists (function
        | Error (JsonError (MalformedJson _)) -> true
        | _ -> false)
    |> _.Should().BeTrue()

[<Fact>]
let ``multiple stdout lines produce multiple events`` () =

    // Arrange
    let fake = FakeProcess.create ()
    let session = Session.toSession fake.Bridge
    let events = collectEvents session

    // Act
    fake.PushStdout """{"type":"system","subtype":"init","session_id":"s1","model":"opus-4"}"""
    fake.PushStdout """{"type":"result","duration_ms":1500,"duration_api_ms":1200,"num_turns":1,"result":"done","session_id":"s1","cost_usd":0.05,"model":"opus-4"}"""
    waitForEvent events (function Ok (Result _) -> true | _ -> false)

    // Assert
    events |> Seq.exists (function Ok (Init _) -> true | _ -> false)
    |> _.Should().BeTrue() |> ignore
    events |> Seq.exists (function Ok (Result _) -> true | _ -> false)
    |> _.Should().BeTrue()
