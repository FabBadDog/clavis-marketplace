module FabioSoft.Nucleus.ClaudeBridge.Tests.StreamEventMapperTests

open System
open Faqt
open Faqt.Operators
open FabioSoft.Claude
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.ClaudeBridge
open Xunit

let private sessionId = Guid.NewGuid()

// Fake provider resolvers: the hook resolver echoes a recognisable name, the reason resolver passes
// the decision text straight through. The real resolution logic is covered in FabioSoft.Claude.Tests.
let private resolveHookName = Func<string, string>(fun hookEvent -> $"hook:{hookEvent}")

let private resolveReason =
    Func<string, string, string, string, ResolvedPermissionReason>(fun _ reason _ _ ->
        { MatchedRulePattern = ""; MatchedRuleScope = ""; ReasonText = (if isNull reason then "" else reason) })

let private map event =
    StreamEventMapper.Map(sessionId, event, resolveHookName, resolveReason)

[<Fact>]
let ``Map Init produces AgentInit`` () =

    // Arrange
    let event = StreamEvent.Init(SessionId "sess-123", "claude-4-opus", ["clear"; "compact"])

    // Act
    let result = map (event)

    // Assert
    let init = result :?> AgentInit
    %init.AgentSessionId.Should().Be("sess-123")
    %init.Model.Should().Be("claude-4-opus")
    %init.SessionId.Should().Be(sessionId)
    %init.SlashCommands.Should().SequenceEqual(["clear"; "compact"])

[<Fact>]
let ``Map Commands produces AgentCommandsAvailable with descriptors`` () =

    // Arrange
    let event =
        StreamEvent.Commands [
            { Name = "clear"; Description = "Start a new session"; ArgumentHint = "[name]" }
            { Name = "compact"; Description = "Summarize the conversation"; ArgumentHint = "" }
        ]

    // Act
    let result = map (event)

    // Assert
    let available = result :?> AgentCommandsAvailable
    %available.SessionId.Should().Be(sessionId)
    %available.Commands.Count.Should().Be(2)
    %available.Commands[0].Name.Should().Be("clear")
    %available.Commands[0].Description.Should().Be("Start a new session")
    %available.Commands[0].ArgumentHint.Should().Be("[name]")

[<Fact>]
let ``Map SessionEnded produces AgentSessionEnded`` () =

    // Act
    let result = map (StreamEvent.SessionEnded(0, "clean exit"))

    // Assert
    let ended = result :?> AgentSessionEnded
    %ended.ExitCode.Should().Be(0)
    %ended.Detail.Should().Be("clean exit")

[<Fact>]
let ``Map SessionAlreadyExited produces AgentSessionAlreadyExited`` () =

    // Act
    let result = map (StreamEvent.SessionAlreadyExited)

    // Assert
    %result.Should().BeOfType<AgentSessionAlreadyExited>()

[<Fact>]
let ``Map LogMessage produces AgentLogMessage`` () =

    // Act
    let result = map (StreamEvent.LogMessage "stderr output")

    // Assert
    let log = result :?> AgentLogMessage
    %log.Text.Should().Be("stderr output")

[<Fact>]
let ``Map ApiCallRetry produces AgentApiCallRetry`` () =

    // Act
    let result = map (StreamEvent.ApiCallRetry)

    // Assert
    %result.Should().BeOfType<AgentApiCallRetry>()

[<Fact>]
let ``Map Compacting produces AgentCompacting`` () =

    // Act
    let result = map (StreamEvent.Compacting)

    // Assert
    %result.Should().BeOfType<AgentCompacting>()

[<Fact>]
let ``Map Thinking produces AgentThinking`` () =

    // Act
    let result = map (StreamEvent.Thinking "analyzing code")

    // Assert
    let thinking = result :?> AgentThinking
    %thinking.Summary.Should().Be("analyzing code")

[<Fact>]
let ``Map ToolUse produces AgentToolUse`` () =

    // Arrange
    let info = { Name = "Read"; ToolUseId = "tu-1"; Input = "test.fs"; FullInput = "{\"path\":\"test.fs\"}" }

    // Act
    let result = map (StreamEvent.ToolUse info)

    // Assert
    let toolUse = result :?> AgentToolUse
    %toolUse.ToolName.Should().Be("Read") |> ignore
    %toolUse.ToolUseId.Should().Be("tu-1") |> ignore
    %toolUse.Input.Should().Be("test.fs") |> ignore
    %toolUse.FullInput.Should().Contain("\"path\"")

[<Fact>]
let ``Map ToolResult produces AgentToolResult`` () =

    // Arrange
    let duration = TimeSpan.FromMilliseconds(150.0)
    let info = { ToolUseId = "tu-1"; Summary = "file content"; FullOutput = "the full file content here"; Duration = duration }

    // Act
    let result = map (StreamEvent.ToolResult info)

    // Assert
    let toolResult = result :?> AgentToolResult
    %toolResult.ToolUseId.Should().Be("tu-1") |> ignore
    %toolResult.Summary.Should().Be("file content") |> ignore
    %toolResult.FullOutput.Should().Be("the full file content here") |> ignore
    %toolResult.Duration.Should().Be(duration)

[<Fact>]
let ``Map ThinkingTokens produces AgentThinkingTokens`` () =

    // Act
    let result = map (StreamEvent.ThinkingTokens 1234)

    // Assert
    let thinkingTokens = result :?> AgentThinkingTokens
    %thinkingTokens.EstimatedTokens.Should().Be(1234)

[<Fact>]
let ``Map RateLimit produces AgentRateLimit`` () =

    // Arrange
    let resetsAt = DateTimeOffset.FromUnixTimeSeconds(1780935600L)
    let info = { LimitType = "five_hour"; Status = "allowed"; ResetsAt = resetsAt; IsUsingOverage = false }

    // Act
    let result = map (StreamEvent.RateLimit info)

    // Assert
    let rateLimit = result :?> AgentRateLimit
    %rateLimit.LimitType.Should().Be("five_hour") |> ignore
    %rateLimit.Status.Should().Be("allowed") |> ignore
    %rateLimit.IsUsingOverage.Should().BeFalse()

[<Fact>]
let ``Map TextDelta produces AgentTextDelta`` () =

    // Act
    let result = map (StreamEvent.TextDelta "hello ")

    // Assert
    let delta = result :?> AgentTextDelta
    %delta.Text.Should().Be("hello ")

[<Fact>]
let ``Map Assistant produces AgentAssistant`` () =

    // Arrange
    let msg = { Text = "Done."; IsFinal = true; IsSynthetic = false }

    // Act
    let result = map (StreamEvent.Assistant msg)

    // Assert
    let assistant = result :?> AgentAssistant
    %assistant.Text.Should().Be("Done.")
    %assistant.IsFinal.Should().BeTrue()

[<Fact>]
let ``Map drops a synthetic assistant message`` () =

    // Arrange - locally generated slash-command output (e.g. the session boot command's reply) must
    // never reach the UI as assistant text.
    let msg = { Text = "/cost isn't available in this environment"; IsFinal = true; IsSynthetic = true }

    // Act
    let result = map (StreamEvent.Assistant msg)

    // Assert
    %(obj.ReferenceEquals(result, null)).Should().BeTrue()

[<Fact>]
let ``Map Usage produces AgentUsage`` () =

    // Arrange
    let info = { InputTokens = 500; OutputTokens = 200; CacheReadTokens = 100 }

    // Act
    let result = map (StreamEvent.Usage info)

    // Assert
    let usage = result :?> AgentUsage
    %usage.InputTokens.Should().Be(500)
    %usage.OutputTokens.Should().Be(200)
    %usage.CacheReadTokens.Should().Be(100)

[<Fact>]
let ``Map Result produces AgentResult`` () =

    // Arrange
    let duration = TimeSpan.FromSeconds(12.0)
    let data =
        { SessionId = SessionId "sess-1"
          CostUsd = 0.05
          Duration = duration
          Model = "opus"
          ResultText = "final answer"
          IsError = false
          NumTurns = 1 }

    // Act
    let result = map (StreamEvent.Result data)

    // Assert
    let AgentResult = result :?> AgentResult
    %AgentResult.AgentSessionId.Should().Be("sess-1")
    %AgentResult.CostUsd.Should().Be(0.05)
    %AgentResult.Model.Should().Be("opus")
    %AgentResult.ResultText.Should().Be("final answer")
    %AgentResult.IsError.Should().BeFalse()

[<Fact>]
let ``Map drops a local no-op result`` () =

    // Arrange - num_turns 0 without error is a slash command's local acknowledgement; publishing it as
    // AgentResult would terminate whatever real turn is active.
    let data =
        { SessionId = SessionId "sess-1"
          CostUsd = 0.0
          Duration = TimeSpan.Zero
          Model = "<synthetic>"
          ResultText = "/cost output"
          IsError = false
          NumTurns = 0 }

    // Act
    let result = map (StreamEvent.Result data)

    // Assert
    %(obj.ReferenceEquals(result, null)).Should().BeTrue()

[<Fact>]
let ``Map keeps a zero-turn result that reports an error`` () =

    // Arrange - a failure before any turn ran (e.g. an auth error) must still surface.
    let data =
        { SessionId = SessionId "sess-1"
          CostUsd = 0.0
          Duration = TimeSpan.Zero
          Model = ""
          ResultText = "Invalid API key"
          IsError = true
          NumTurns = 0 }

    // Act
    let result = map (StreamEvent.Result data)

    // Assert
    let agentResult = result :?> AgentResult
    %agentResult.IsError.Should().BeTrue()
    %agentResult.ResultText.Should().Be("Invalid API key")

[<Fact>]
let ``Map HookStart produces AgentHookStart`` () =

    // Arrange
    let info = { HookId = "h-1"; HookName = "pre-commit"; HookEvent = "PreToolUse" }

    // Act
    let result = map (StreamEvent.HookStart info)

    // Assert
    let hook = result :?> AgentHookStart
    %hook.HookId.Should().Be("h-1")
    %hook.HookName.Should().Be("hook:PreToolUse")
    %hook.HookEvent.Should().Be("PreToolUse")
    %hook.IsSessionStart.Should().BeFalse()

[<Fact>]
let ``Map HookStart flags session-start hooks`` () =

    // Arrange
    let info = { HookId = "h-2"; HookName = "init"; HookEvent = "SessionStart" }

    // Act
    let result = map (StreamEvent.HookStart info)

    // Assert
    let hook = result :?> AgentHookStart
    %hook.IsSessionStart.Should().BeTrue()

[<Fact>]
let ``Map HookComplete produces AgentHookComplete`` () =

    // Arrange
    let outcome = {
        HookId = "h-1"; HookName = "lint"; HookEvent = "PostToolUse"
        Outcome = "passed"; ExitCode = Some 0; Stdout = "ok"; Stderr = ""
    }

    // Act
    let result = map (StreamEvent.HookComplete outcome)

    // Assert
    let hook = result :?> AgentHookComplete
    %hook.Outcome.Should().Be("passed")
    %(hook.ExitCode.Value).Should().Be(0)
    %hook.Stdout.Should().Be("ok")

[<Fact>]
let ``Map PermissionRequest produces AgentPermissionRequest`` () =

    // Arrange
    let info = {
        RequestId = "pr-1"; ToolName = "Bash"; ToolUseId = Some "tu-5"
        Input = "rm -rf /tmp"; DecisionReason = Some "dangerous"; DecisionReasonType = None
    }

    // Act
    let result = map (StreamEvent.PermissionRequest info)

    // Assert
    let perm = result :?> AgentPermissionRequest
    %perm.RequestId.Should().Be("pr-1")
    %perm.ToolName.Should().Be("Bash")
    %perm.ToolUseId.Should().Be("tu-5")
    %perm.Input.Should().Contain("rm -rf")
    %perm.ReasonText.Should().Be("dangerous")
    %perm.MatchedRulePattern.Should().Be("")

[<Fact>]
let ``Map Aborted produces AgentAborted`` () =

    // Act
    let result = map (StreamEvent.Aborted)

    // Assert
    %result.Should().BeOfType<AgentAborted>()

[<Fact>]
let ``MapError produces AgentParsingError with message`` () =

    // Act
    let result = StreamEventMapper.MapError(sessionId, ParsingError.EmptyInput)

    // Assert
    %result.SessionId.Should().Be(sessionId)
    %result.Message.Should().Be("Empty input line")
    %result.IsIgnorable.Should().BeFalse()

[<Fact>]
let ``MapError flags unknown message types as ignorable`` () =

    // Act
    let result = StreamEventMapper.MapError(sessionId, ParsingError.UnknownMessageType "future_event")

    // Assert
    %result.IsIgnorable.Should().BeTrue()

[<Fact>]
let ``Map preserves SessionId on all events`` () =

    // Act
    let results = [
        map (StreamEvent.ApiCallRetry)
        map (StreamEvent.Compacting)
        map (StreamEvent.Aborted)
        map (StreamEvent.SessionAlreadyExited)
    ]

    // Assert
    for result in results do
        %result.SessionId.Should().Be(sessionId)
