module FabioSoft.Claude.Tests.NdjsonParserTests

open System
open FabioSoft.Claude
open Faqt
open FabioSoft.Json
open Xunit

let private toLine (json: Json) =
    json.ToString()

let private assistantMessage content =
    Json.Object [
        "type", Json.String "assistant"
        "message", Json.Object [
            "content", Json.Array content
        ]
    ]

let private assistantMessageWithStop content stopReason =
    Json.Object [
        "type", Json.String "assistant"
        "message", Json.Object [
            "content", Json.Array content
            "stop_reason", Json.String stopReason
        ]
    ]

let private toolUseItem id name input =
    Json.Object [
        "type", Json.String "tool_use"
        "id", Json.String id
        "name", Json.String name
        "input", input
    ]

let private textItem text =
    Json.Object [ "type", Json.String "text"; "text", Json.String text ]

let private thinkingItem text =
    Json.Object [ "type", Json.String "thinking"; "thinking", Json.String text ]

[<Fact>]
let ``parse with assistant tool_use returns ToolUse event`` () =

    // Arrange
    let json =
        assistantMessage [|
            toolUseItem "t1" "Read" (Json.Object [ "file_path", Json.String "CLAUDE.md" ])
        |] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(ToolUse { Name = "Read"; ToolUseId = "t1"; Input = "CLAUDE.md"; FullInput = (Json.Object [ "file_path", Json.String "CLAUDE.md" ]).ToString() })

[<Fact>]
let ``parse with assistant thinking returns Thinking event`` () =

    // Arrange
    let json =
        assistantMessage [| thinkingItem "Let me check the file" |]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Thinking "Let me check the file")

[<Fact>]
let ``parse with assistant text returns TextDelta and Assistant`` () =

    // Arrange
    let json =
        assistantMessage [| textItem "Hello world" |]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(TextDelta "Hello world") |> ignore
    events.Should().Contain(Assistant { Text = "Hello world"; IsFinal = true; IsSynthetic = false })

[<Fact>]
let ``parse with mixed content returns all event types`` () =

    // Arrange
    let json =
        assistantMessage [|
            thinkingItem "hmm"
            toolUseItem "t1" "Bash" (Json.Object [ "command", Json.String "ls -la" ])
            textItem "Done"
        |] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Thinking "hmm") |> ignore
    events.Should().Contain(ToolUse { Name = "Bash"; ToolUseId = "t1"; Input = "ls -la"; FullInput = (Json.Object [ "command", Json.String "ls -la" ]).ToString() }) |> ignore
    events.Should().Contain(TextDelta "Done") |> ignore
    events.Should().Contain(Assistant { Text = "Done"; IsFinal = false; IsSynthetic = false })

[<Fact>]
let ``parse with Edit tool_use extracts file_path`` () =

    // Arrange
    let json =
        assistantMessage [|
            toolUseItem "t1" "Edit" (Json.Object [
                "file_path", Json.String "src/Main.fs"
                "old_string", Json.String "a"
                "new_string", Json.String "b"
            ])
        |] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(ToolUse { Name = "Edit"; ToolUseId = "t1"; Input = "src/Main.fs"; FullInput = (Json.Object [ "file_path", Json.String "src/Main.fs"; "old_string", Json.String "a"; "new_string", Json.String "b" ]).ToString() })

[<Fact>]
let ``parse with long Bash command preserves full text`` () =

    // Arrange
    let longCommand = String('x', 80)
    let json =
        assistantMessage [|
            toolUseItem "t1" "Bash" (Json.Object [ "command", Json.String longCommand ])
        |] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryFind (function ToolUse _ -> true | _ -> false) with
    | Some (ToolUse info) -> info.Input.Should().Be(longCommand)
    | _ -> failwith "Expected ToolUse event"

[<Fact>]
let ``parse with stop_reason tool_use sets IsFinal false`` () =

    // Arrange
    let json =
        assistantMessageWithStop [| textItem "Writing now." |] "tool_use"
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Assistant { Text = "Writing now."; IsFinal = false; IsSynthetic = false })

[<Fact>]
let ``parse with stop_reason end_turn sets IsFinal true`` () =

    // Arrange
    let json =
        assistantMessageWithStop [| textItem "Done." |] "end_turn"
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Assistant { Text = "Done."; IsFinal = true; IsSynthetic = false })

[<Fact>]
let ``parse with tool_use only does not produce Assistant event`` () =

    // Arrange
    let json =
        assistantMessage [|
            toolUseItem "t1" "Read" (Json.Object [ "file_path", Json.String "x.fs" ])
        |] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events
    |> List.exists (function Assistant _ -> true | _ -> false)
    |> _.Should().BeFalse()

[<Fact>]
let ``parse extracts usage tokens from assistant message`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "assistant"
            "message", Json.Object [
                "content", Json.Array [| textItem "Hi" |]
                "usage", Json.Object [
                    "input_tokens", Json.Integer 123L
                    "output_tokens", Json.Integer 45L
                    "cache_read_input_tokens", Json.Integer 1000L
                ]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Usage { InputTokens = 123; OutputTokens = 45; CacheReadTokens = 1000 })

[<Fact>]
let ``parse omits Usage when all token counts are zero`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "assistant"
            "message", Json.Object [
                "content", Json.Array [| textItem "Hi" |]
                "usage", Json.Object [
                    "input_tokens", Json.Integer 0L
                    "output_tokens", Json.Integer 0L
                ]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events
    |> List.exists (function Usage _ -> true | _ -> false)
    |> _.Should().BeFalse()

[<Fact>]
let ``parse without usage field produces no Usage event`` () =

    // Arrange
    let json =
        assistantMessage [| textItem "Hi" |]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events
    |> List.exists (function Usage _ -> true | _ -> false)
    |> _.Should().BeFalse()

[<Fact>]
let ``parse with init event extracts session id and model`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "init"
            "session_id", Json.String "c75b98b5-24e7-4701-9802-be21005b6d78"
            "model", Json.String "claude-opus-4-6[1m]"
            "tools", Json.Array [| Json.String "Bash"; Json.String "Read" |]
            "cwd", Json.String "/tmp"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Init(SessionId "c75b98b5-24e7-4701-9802-be21005b6d78", "claude-opus-4-6[1m]", []))

[<Fact>]
let ``parse init captures slash_commands`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "init"
            "session_id", Json.String "s1"
            "model", Json.String "opus"
            "slash_commands", Json.Array [| Json.String "clear"; Json.String "compact"; Json.String "review" |]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Init(SessionId "s1", "opus", ["clear"; "compact"; "review"]))

[<Fact>]
let ``parse control_response captures the command catalogue`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_response"
            "response", Json.Object [
                "subtype", Json.String "success"
                "request_id", Json.String "clavis-initialize"
                "response", Json.Object [
                    "commands", Json.Array [|
                        Json.Object [
                            "name", Json.String "clear"
                            "description", Json.String "Start a new session"
                            "argumentHint", Json.String "[name]"
                        ]
                        Json.Object [
                            "name", Json.String "compact"
                            "description", Json.String "Summarize"
                            "argumentHint", Json.String ""
                        ]
                    |]
                ]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(
        Commands [
            { Name = "clear"; Description = "Start a new session"; ArgumentHint = "[name]" }
            { Name = "compact"; Description = "Summarize"; ArgumentHint = "" }
        ])

[<Fact>]
let ``parse with result event extracts cost and duration`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "result"
            "subtype", Json.String "success"
            "is_error", Json.Boolean false
            "duration_ms", Json.Integer 25235L
            "result", Json.String "Hello"
            "session_id", Json.String "c75b98b5"
            "total_cost_usd", Json.Float 0.16190875
            "modelUsage", Json.Object [
                "claude-opus-4-6[1m]", Json.Object [
                    "inputTokens", Json.Integer 3L
                    "outputTokens", Json.Integer 8L
                ]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function Result data -> Some data | _ -> None) with
    | Some data ->
        data.SessionId.Should().Be(SessionId "c75b98b5") |> ignore
        data.CostUsd.Should().Be(0.16190875) |> ignore
        data.Duration.Should().Be(TimeSpan.FromMilliseconds(25235.0)) |> ignore
        data.Model.Should().Be("claude-opus-4-6[1m]") |> ignore
        data.ResultText.Should().Be("Hello") |> ignore
        data.IsError.Should().BeFalse()
    | None -> failwith "Expected Result event"

[<Fact>]
let ``parse result captures is_error true and result text`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "result"
            "subtype", Json.String "error_during_execution"
            "is_error", Json.Boolean true
            "result", Json.String "Something went wrong"
            "session_id", Json.String "s1"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function Result data -> Some data | _ -> None) with
    | Some data ->
        data.IsError.Should().BeTrue() |> ignore
        data.ResultText.Should().Be("Something went wrong")
    | None -> failwith "Expected Result event"

[<Fact>]
let ``parse user message with string content does not crash`` () =

    // The Anthropic content shorthand: a user message whose `content` is a plain string rather than a
    // block array (sub-agent prompts, the local-command caveat, replayed prompts). Reading it as a typed
    // array used to throw UnexpectedPropertyType; it must now parse cleanly and yield no tool results.
    // Arrange
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.String "Read-only RESEARCH. Question: how does X work?"
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    result |> List.iter (function
        | Error error -> failwith $"Expected Ok, got Error: {ParsingError.getMessage error}"
        | Ok _ -> ())

[<Fact>]
let ``parse with system hook_started event returns HookStart`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "hook_started"
            "hook_id", Json.String "abc"
            "hook_name", Json.String "SessionStart:startup"
            "hook_event", Json.String "SessionStart"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(HookStart {
        HookId = "abc"
        HookName = "SessionStart:startup"
        HookEvent = "SessionStart" })

[<Fact>]
let ``parse with system hook_response event returns HookComplete`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "hook_response"
            "hook_id", Json.String "abc"
            "hook_name", Json.String "SessionStart:startup"
            "hook_event", Json.String "SessionStart"
            "outcome", Json.String "success"
            "exit_code", Json.Integer 0L
            "stdout", Json.String "hi\n"
            "stderr", Json.String ""
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(HookComplete {
        HookId = "abc"
        HookName = "SessionStart:startup"
        HookEvent = "SessionStart"
        Outcome = "success"
        ExitCode = Some 0
        Stdout = "hi\n"
        Stderr = "" })

[<Fact>]
let ``parse with system task_started returns TaskStarted event`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "task_started"
            "task_id", Json.String "af6a6b089414be6dd"
            "tool_use_id", Json.String "toolu_01BYzRa7P9pxNeW5FGQgM35V"
            "description", Json.String "Return single word"
            "subagent_type", Json.String "general-purpose"
            "task_type", Json.String "local_agent"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(TaskStarted("af6a6b089414be6dd", "Return single word", "local_agent"))

[<Fact>]
let ``parse with system task_notification returns TaskCompleted event`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "task_notification"
            "task_id", Json.String "af6a6b089414be6dd"
            "tool_use_id", Json.String "toolu_01BYzRa7P9pxNeW5FGQgM35V"
            "status", Json.String "completed"
            "summary", Json.String "alpha"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(TaskCompleted("af6a6b089414be6dd", "completed", "alpha"))

[<Fact>]
let ``parse with system task_updated is recognised and yields no event`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "task_updated"
            "task_id", Json.String "af6a6b089414be6dd"
            "patch", Json.Object [ "status", Json.String "completed" ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    result.Should().BeEmpty()

[<Fact>]
let ``parse with rate_limit_event returns RateLimit event`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "rate_limit_event"
            "rate_limit_info", Json.Object [
                "status", Json.String "allowed"
                "rateLimitType", Json.String "five_hour"
                "resetsAt", Json.Integer 1780935600L
                "isUsingOverage", Json.Boolean false
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function RateLimit info -> Some info | _ -> None) with
    | Some info ->
        info.LimitType.Should().Be("five_hour") |> ignore
        info.Status.Should().Be("allowed") |> ignore
        info.IsUsingOverage.Should().BeFalse()
    | None -> failwith "Expected RateLimit event"

[<Fact>]
let ``parse tool_result captures full output from block content`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "tool_result"
                        "tool_use_id", Json.String "t1"
                        "content", Json.String "the complete tool output text"
                    ]
                |]
            ]
        ] |> toLine

    // Act
    let events = NdjsonParser.parse json |> List.choose Result.toOption

    // Assert
    match events |> List.tryPick (function ToolResult info -> Some info | _ -> None) with
    | Some info -> info.FullOutput.Should().Be("the complete tool output text")
    | None -> failwith "Expected ToolResult event"

[<Fact>]
let ``parse thinking_tokens returns ThinkingTokens event`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "thinking_tokens"
            "estimated_tokens", Json.Integer 42L
        ] |> toLine

    // Act
    let events = NdjsonParser.parse json |> List.choose Result.toOption

    // Assert
    events.Should().Contain(ThinkingTokens 42)

[<Fact>]
let ``parse hook_progress is recognised and produces no event or error`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "hook_progress"
            "hook_id", Json.String "h1"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    result.Should().BeEmpty()

[<Fact>]
let ``parse with empty string returns EmptyInput error`` () =

    NdjsonParser.parse ""
    |> _.Should().Be([Error EmptyInput])

[<Fact>]
let ``parse with whitespace returns EmptyInput error`` () =

    NdjsonParser.parse "   "
    |> _.Should().Be([Error EmptyInput])

[<Fact>]
let ``parse with malformed JSON returns MalformedJson error`` () =

    match NdjsonParser.parse "not json at all" with
    | [Error (JsonError (MalformedJson _))] -> ()
    | other -> failwith $"Expected [Error MalformedJson], got {other}"

[<Fact>]
let ``parse with incomplete JSON returns MalformedJson error`` () =

    match NdjsonParser.parse """{"type":"assistant""" with
    | [Error (JsonError (MalformedJson _))] -> ()
    | other -> failwith $"Expected [Error MalformedJson], got {other}"

[<Fact>]
let ``parse with assistant missing message field returns error`` () =

    // Arrange
    let json =
        Json.Object [ "type", Json.String "assistant" ]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    match result with
    | [Error (MissingRequiredField ("assistant", "message"))] -> ()
    | other -> failwith $"Expected [Error MissingRequiredField], got {other}"

[<Fact>]
let ``parse with assistant missing content array returns error`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "assistant"
            "message", Json.Object []
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    match result with
    | [Error (InvalidMessageStructure ("assistant", _))] -> ()
    | other -> failwith $"Expected [Error InvalidMessageStructure], got {other}"

[<Fact>]
let ``parse with init missing session_id uses empty string`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "init"
            "model", Json.String "sonnet"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Init(SessionId "", "sonnet", []))

[<Fact>]
let ``parse with result missing optional fields uses defaults`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "result"
            "subtype", Json.String "success"
            "session_id", Json.String "s1"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function Result data -> Some data | _ -> None) with
    | Some data ->
        data.CostUsd.Should().Be(0.0) |> ignore
        data.Duration.Should().Be(TimeSpan.Zero) |> ignore
        data.Model.Should().Be("") |> ignore
        data.ResultText.Should().Be("") |> ignore
        data.IsError.Should().BeFalse()
    | None -> failwith "Expected Result event"

[<Fact>]
let ``parse with tool_use_result as string does not crash`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "tool_result"
                        "tool_use_id", Json.String "t1"
                        "tool_use_result", Json.String "[Request interrupted by user for tool use]"
                    ]
                |]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    result |> List.iter (function
    | Error error -> failwith $"Expected Ok, got Error: {ParsingError.getMessage error}"
    | Ok _ -> ())

[<Fact>]
let ``parse with control_request returns PermissionRequest with reason and type`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "req1"
            "request", Json.Object [
                "tool_name", Json.String "Write"
                "input", Json.Object [
                    "file_path", Json.String "C:/tmp.txt"
                    "content", Json.String "hello"
                ]
                "decision_reason", Json.String "Safety check failed"
                "decision_reason_type", Json.String "safetyCheck"
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function PermissionRequest info -> Some info | _ -> None) with
    | Some info ->
        info.RequestId.Should().Be("req1") |> ignore
        info.ToolName.Should().Be("Write") |> ignore
        info.DecisionReason.Should().Be(Some "Safety check failed") |> ignore
        info.DecisionReasonType.Should().Be(Some "safetyCheck")
    | None -> failwith "Expected PermissionRequest event"

[<Fact>]
let ``parse with control_request and no reason fields returns None for both`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "req2"
            "request", Json.Object [
                "tool_name", Json.String "Bash"
                "input", Json.Object [ "command", Json.String "rm -rf /" ]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function PermissionRequest info -> Some info | _ -> None) with
    | Some info ->
        info.DecisionReason.Should().Be(None) |> ignore
        info.DecisionReasonType.Should().Be(None)
    | None -> failwith "Expected PermissionRequest event"

[<Fact>]
let ``parse with control_request captures tool_use_id`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "req-tid"
            "request", Json.Object [
                "tool_name", Json.String "Write"
                "tool_use_id", Json.String "toolu_01ABC"
                "input", Json.Object [ "file_path", Json.String "C:/tmp.txt" ]
                "decision_reason_type", Json.String "rule"
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function PermissionRequest info -> Some info | _ -> None) with
    | Some info ->
        info.ToolUseId.Should().Be(Some "toolu_01ABC")
    | None -> failwith "Expected PermissionRequest event"

[<Fact>]
let ``parse with control_request and ask rule type has None reason`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "req3"
            "request", Json.Object [
                "tool_name", Json.String "Write"
                "input", Json.Object [ "file_path", Json.String "C:/tmp.txt" ]
                "decision_reason_type", Json.String "rule"
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function PermissionRequest info -> Some info | _ -> None) with
    | Some info ->
        info.DecisionReason.Should().Be(None) |> ignore
        info.DecisionReasonType.Should().Be(Some "rule")
    | None -> failwith "Expected PermissionRequest event"

[<Fact>]
let ``parse with control_request captures permission_suggestions`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "req-sug"
            "request", Json.Object [
                "tool_name", Json.String "Bash"
                "input", Json.Object [ "command", Json.String "git status" ]
                "permission_suggestions", Json.Array [|
                    Json.Object [
                        "type", Json.String "addRules"
                        "behavior", Json.String "allow"
                        "destination", Json.String "localSettings"
                        "rules", Json.Array [|
                            Json.Object [ "toolName", Json.String "Bash"; "ruleContent", Json.String "git*" ]
                        |]
                    ]
                    Json.Object [
                        "type", Json.String "addDirectories"
                        "destination", Json.String "session"
                        "directories", Json.Array [| Json.String "C:/repo" |]
                    ]
                |]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function PermissionRequest info -> Some info | _ -> None) with
    | Some info ->
        match info.Suggestions with
        | [ AddRules(rules, behavior, destination); AddDirectories(directories, _) ] ->
            behavior.Should().Be("allow") |> ignore
            destination.Should().Be("localSettings") |> ignore
            rules.Head.ToolName.Should().Be("Bash") |> ignore
            rules.Head.RuleContent.Should().Be(Some "git*") |> ignore
            directories.Should().SequenceEqual([ "C:/repo" ])
        | _ -> failwith "Unexpected suggestion shape"
    | None -> failwith "Expected PermissionRequest event"

[<Fact>]
let ``parse with control_request and no suggestions yields an empty list`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "req-nos"
            "request", Json.Object [
                "tool_name", Json.String "Bash"
                "input", Json.Object [ "command", Json.String "ls" ]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function PermissionRequest info -> Some info | _ -> None) with
    | Some info -> info.Suggestions.Should().BeEmpty()
    | None -> failwith "Expected PermissionRequest event"

[<Fact>]
let ``parse skips an unknown suggestion type`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "req-unk"
            "request", Json.Object [
                "tool_name", Json.String "Bash"
                "input", Json.Object [ "command", Json.String "ls" ]
                "permission_suggestions", Json.Array [|
                    Json.Object [ "type", Json.String "futureThing"; "destination", Json.String "session" ]
                    Json.Object [ "type", Json.String "setMode"; "mode", Json.String "plan"; "destination", Json.String "session" ]
                |]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function PermissionRequest info -> Some info | _ -> None) with
    | Some info ->
        match info.Suggestions with
        | [ SetMode(mode, _) ] -> mode.Should().Be("plan")
        | _ -> failwith "Expected only the setMode suggestion"
    | None -> failwith "Expected PermissionRequest event"

[<Fact>]
let ``parse with assistant tool_use and null stop_reason produces IsFinal false`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "assistant"
            "message", Json.Object [
                "content", Json.Array [|
                    toolUseItem "t1" "Write" (Json.Object [
                        "file_path", Json.String "C:/tmp.txt"
                        "content", Json.String "hello"
                    ])
                |]
                "stop_reason", Json.Null
            ]
            "session_id", Json.String "s1"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    let assistantEvents = events |> List.choose (function Assistant assistant -> Some assistant | _ -> None)
    assistantEvents.Should().BeEmpty("tool-only turns produce no Assistant event, so IsFinal cannot be true")

[<Fact>]
let ``parse tool_result with numFiles summarizes file count`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "tool_result"
                        "tool_use_id", Json.String "t1"
                    ]
                |]
            ]
            "tool_use_result", Json.Object [
                "numFiles", Json.Integer 42L
                "durationMs", Json.Integer 150L
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function ToolResult info -> Some info | _ -> None) with
    | Some info ->
        info.Summary.Should().Be("42 files") |> ignore
        info.Duration.Should().Be(TimeSpan.FromMilliseconds(150.0))
    | None -> failwith "Expected ToolResult event"

[<Fact>]
let ``parse tool_result with short content uses it as summary`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "tool_result"
                        "tool_use_id", Json.String "t1"
                    ]
                |]
            ]
            "tool_use_result", Json.Object [
                "content", Json.String "short text"
                "durationMs", Json.Integer 50L
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function ToolResult info -> Some info | _ -> None) with
    | Some info ->
        info.Summary.Should().Be("short text") |> ignore
        info.Duration.Should().Be(TimeSpan.FromMilliseconds(50.0))
    | None -> failwith "Expected ToolResult event"

[<Fact>]
let ``parse tool_result with long content truncates to 80 chars`` () =

    // Arrange
    let longContent = String('x', 100)
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "tool_result"
                        "tool_use_id", Json.String "t1"
                    ]
                |]
            ]
            "tool_use_result", Json.Object [
                "content", Json.String longContent
                "durationMs", Json.Integer 10L
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function ToolResult info -> Some info | _ -> None) with
    | Some info ->
        info.Summary.Length.Should().BeLessThanOrEqualTo(80) |> ignore
        info.Summary.Should().EndWith("...")
    | None -> failwith "Expected ToolResult event"

[<Fact>]
let ``parse tool_result without tool_use_result object returns empty summary`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "tool_result"
                        "tool_use_id", Json.String "t1"
                    ]
                |]
            ]
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function ToolResult info -> Some info | _ -> None) with
    | Some info ->
        info.Summary.Should().Be("") |> ignore
        info.Duration.Should().Be(TimeSpan.Zero)
    | None -> failwith "Expected ToolResult event"

[<Fact>]
let ``parse tool_result with empty object returns empty summary`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "tool_result"
                        "tool_use_id", Json.String "t1"
                    ]
                |]
            ]
            "tool_use_result", Json.Object []
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryPick (function ToolResult info -> Some info | _ -> None) with
    | Some info ->
        info.Summary.Should().Be("")
    | None -> failwith "Expected ToolResult event"

[<Fact>]
let ``parse with unknown system subtype returns error`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "system"
            "subtype", Json.String "future_feature"
            "data", Json.String "something"
        ] |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    match result with
    | [Error (UnknownSystemSubtype "future_feature")] -> ()
    | other -> failwith $"Expected [Error UnknownSystemSubtype], got {other}"

[<Fact>]
let ``ParsingError.getMessage for EmptyInput returns non-empty message`` () =

    ParsingError.getMessage EmptyInput
    |> _.Should().NotBeEmpty()

[<Fact>]
let ``ParsingError.getMessage for MalformedJson includes parse error details`` () =

    ParsingError.getMessage (JsonError(MalformedJson(0, 5, ["valid JSON"], "Unexpected character")))
    |> _.Should().Contain("Unexpected character")

[<Fact>]
let ``ParsingError.getMessage for MissingProperty includes property name`` () =

    ParsingError.getMessage (JsonError(MissingProperty "session_id"))
    |> _.Should().Contain("session_id")

[<Fact>]
let ``ParsingError.getMessage for UnexpectedPropertyType includes property name`` () =

    ParsingError.getMessage (JsonError(UnexpectedPropertyType("age", typeof<string>, typeof<int64>)))
    |> _.Should().Contain("age")

[<Fact>]
let ``ParsingError.getMessage for MissingTypeField includes raw JSON`` () =

    ParsingError.getMessage (MissingTypeField """{"foo":"bar"}""")
    |> _.Should().Contain("""{"foo":"bar"}""")

[<Fact>]
let ``ParsingError.getMessage for UnknownMessageType includes type name`` () =

    ParsingError.getMessage (UnknownMessageType "future_event")
    |> _.Should().Contain("future_event")

[<Fact>]
let ``ParsingError.getMessage for UnknownSystemSubtype includes subtype name`` () =

    ParsingError.getMessage (UnknownSystemSubtype "new_subtype")
    |> _.Should().Contain("new_subtype")

[<Fact>]
let ``ParsingError.getMessage for MissingRequiredField includes field and message type`` () =

    // Act
    let message = ParsingError.getMessage (MissingRequiredField("assistant", "content"))

    // Assert
    message.Should().Contain("content") |> ignore
    message.Should().Contain("assistant")

[<Fact>]
let ``ParsingError.getMessage for InvalidMessageStructure includes type and detail`` () =

    // Act
    let message = ParsingError.getMessage (InvalidMessageStructure("user", "content is not an array"))

    // Assert
    message.Should().Contain("user") |> ignore
    message.Should().Contain("content is not an array")

[<Fact>]
let ``ParsingError.isIgnorable is true for UnknownMessageType`` () =

    ParsingError.isIgnorable (UnknownMessageType "future_event")
    |> _.Should().BeTrue()

[<Fact>]
let ``ParsingError.isIgnorable is true for UnknownSystemSubtype`` () =

    ParsingError.isIgnorable (UnknownSystemSubtype "new_subtype")
    |> _.Should().BeTrue()

[<Fact>]
let ``ParsingError.isIgnorable is false for a genuine parse error`` () =

    ParsingError.isIgnorable EmptyInput
    |> _.Should().BeFalse()


[<Fact>]
let ``parse with synthetic assistant flags it and emits no text delta`` () =

    // Arrange - the provider marks locally generated slash-command output with model "<synthetic>"
    let json =
        Json.Object [
            "type", Json.String "assistant"
            "message", Json.Object [
                "model", Json.String "<synthetic>"
                "content", Json.Array [| textItem "/cost isn't available in this environment" |]
            ]
        ]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(
        Assistant { Text = "/cost isn't available in this environment"; IsFinal = true; IsSynthetic = true })
    |> ignore
    events
    |> List.exists (function TextDelta _ -> true | _ -> false)
    |> _.Should().BeFalse()

[<Fact>]
let ``parse with real model keeps the assistant unsynthetic`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "assistant"
            "message", Json.Object [
                "model", Json.String "claude-fable-5"
                "content", Json.Array [| textItem "real answer" |]
            ]
        ]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    events.Should().Contain(Assistant { Text = "real answer"; IsFinal = true; IsSynthetic = false })

[<Fact>]
let ``parse result reads num_turns`` () =

    // Arrange - a local no-op result (the session boot command's acknowledgement) carries num_turns 0
    let json =
        Json.Object [
            "type", Json.String "result"
            "session_id", Json.String "s1"
            "num_turns", Json.Integer 0L
            "is_error", Json.Boolean false
            "result", Json.String "/cost output"
        ]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryFind (function StreamEvent.Result _ -> true | _ -> false) with
    | Some (StreamEvent.Result data) -> data.NumTurns.Should().Be(0)
    | _ -> failwith "Expected Result event"

[<Fact>]
let ``parse result without num_turns counts as a real turn`` () =

    // Arrange
    let json =
        Json.Object [
            "type", Json.String "result"
            "session_id", Json.String "s1"
            "is_error", Json.Boolean false
            "result", Json.String "done"
        ]
        |> toLine

    // Act
    let result = NdjsonParser.parse json

    // Assert
    let events = result |> List.choose Result.toOption
    match events |> List.tryFind (function StreamEvent.Result _ -> true | _ -> false) with
    | Some (StreamEvent.Result data) -> data.NumTurns.Should().Be(1)
    | _ -> failwith "Expected Result event"