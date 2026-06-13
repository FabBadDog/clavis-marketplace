module FabioSoft.Claude.Tests.ClaudeCommandTests

open FabioSoft.Claude
open FabioSoft.Process
open Faqt
open Xunit

[<Fact>]
let ``create produces command targeting claude executable`` () =
    // Act
    let command = ClaudeCommand.create ()

    // Assert
    command.TargetFilePath.Should().Be("claude")

[<Fact>]
let ``withPrint appends print flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withPrint

    // Assert
    command.Arguments.Should().Be("--print")

[<Fact>]
let ``withContinue appends continue flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withContinue

    // Assert
    command.Arguments.Should().Be("--continue")

[<Fact>]
let ``withResume appends session id`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withResume "abc-123"

    // Assert
    command.Arguments.Should().Be("--resume abc-123")

[<Fact>]
let ``withSessionId appends session-id flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withSessionId "550e8400-e29b-41d4-a716-446655440000"

    // Assert
    command.Arguments.Should().Be("--session-id 550e8400-e29b-41d4-a716-446655440000")

[<Fact>]
let ``withForkSession appends fork-session flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withForkSession

    // Assert
    command.Arguments.Should().Be("--fork-session")

[<Fact>]
let ``withName appends escaped name`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withName "my session"

    // Assert
    command.Arguments.Should().Be("--name \"my session\"")

[<Fact>]
let ``withModel appends model flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withModel "claude-sonnet-4-6"

    // Assert
    command.Arguments.Should().Be("--model claude-sonnet-4-6")

[<Fact>]
let ``withFallbackModel appends fallback-model flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withFallbackModel "claude-haiku-4-5-20251001"

    // Assert
    command.Arguments.Should().Be("--fallback-model claude-haiku-4-5-20251001")

[<Fact>]
let ``withOutputFormat appends output-format flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withOutputFormat OutputFormat.StreamJson

    // Assert
    command.Arguments.Should().Be("--output-format stream-json")

[<Fact>]
let ``withInputFormat appends input-format flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withInputFormat InputFormat.Text

    // Assert
    command.Arguments.Should().Be("--input-format text")

[<Fact>]
let ``withSystemPrompt appends escaped prompt`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withSystemPrompt "You are a helpful assistant"

    // Assert
    command.Arguments.Should().Be("--system-prompt \"You are a helpful assistant\"")

[<Fact>]
let ``withSystemPrompt escapes quotes in prompt`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withSystemPrompt """Say "hello" """

    // Assert
    command.Arguments.Should().Contain("\\\"hello\\\"")

[<Fact>]
let ``withAppendSystemPrompt appends append-system-prompt flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withAppendSystemPrompt "Extra instructions"

    // Assert
    command.Arguments.Should().Be("--append-system-prompt \"Extra instructions\"")

[<Fact>]
let ``withPermissionMode appends permission-mode flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withPermissionMode AcceptEdits

    // Assert
    command.Arguments.Should().Be("--permission-mode acceptEdits")

[<Theory>]
[<InlineData("default")>]
[<InlineData("acceptEdits")>]
[<InlineData("auto")>]
[<InlineData("bypassPermissions")>]
[<InlineData("dontAsk")>]
[<InlineData("plan")>]
let ``PermissionMode toString covers all cases`` expected =
    // Act
    let modes = [Default; AcceptEdits; Auto; BypassPermissions; DontAsk; Plan]
    let strings = modes |> List.map _.ToString()

    // Assert
    strings.Should().Contain(expected)

[<Fact>]
let ``withPermissionPromptTool appends permission-prompt-tool flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withPermissionPromptTool Stdio

    // Assert
    command.Arguments.Should().Be("--permission-prompt-tool stdio")

[<Fact>]
let ``withDangerouslySkipPermissions appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withDangerouslySkipPermissions

    // Assert
    command.Arguments.Should().Be("--dangerously-skip-permissions")

[<Fact>]
let ``withAllowDangerouslySkipPermissions appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withAllowDangerouslySkipPermissions

    // Assert
    command.Arguments.Should().Be("--allow-dangerously-skip-permissions")

[<Theory>]
[<InlineData("low")>]
[<InlineData("medium")>]
[<InlineData("high")>]
[<InlineData("xhigh")>]
[<InlineData("max")>]
let ``EffortLevel toString covers all cases`` expected =
    // Act
    let levels = [Low; Medium; High; ExtraHigh; Max]
    let strings = levels |> List.map _.ToString()

    // Assert
    strings.Should().Contain(expected)

[<Fact>]
let ``withEffort appends effort flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withEffort High

    // Assert
    command.Arguments.Should().Be("--effort high")

[<Fact>]
let ``withMaxBudgetUsd appends max-budget-usd flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withMaxBudgetUsd 5.0

    // Assert
    command.Arguments.Should().Be("--max-budget-usd 5")

[<Fact>]
let ``withAllowedTools appends allowedTools flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withAllowedTools [ToolWithPattern (Bash, "git *"); Tool Edit]

    // Assert
    command.Arguments.Should().Be("--allowed-tools \"Bash(git *) Edit\"")

[<Fact>]
let ``withDisallowedTools appends disallowedTools flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withDisallowedTools [Tool Bash; Tool Write]

    // Assert
    command.Arguments.Should().Be("--disallowed-tools \"Bash Write\"")

[<Fact>]
let ``withTools appends tools flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withTools [Bash; Edit; Read]

    // Assert
    command.Arguments.Should().Be("--tools \"Bash,Edit,Read\"")

[<Fact>]
let ``withAddDirectory appends add-dir flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withAddDirectory @"C:\Other\Project"

    // Assert
    command.Arguments.Should().Be(@"--add-dir ""C:\\Other\\Project""")

[<Fact>]
let ``withAgent appends agent flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withAgent "reviewer"

    // Assert
    command.Arguments.Should().Be("--agent \"reviewer\"")

[<Fact>]
let ``withMcpConfig appends mcp-config flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withMcpConfig "servers.json"

    // Assert
    command.Arguments.Should().Be("--mcp-config \"servers.json\"")

[<Fact>]
let ``withStrictMcpConfig appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withStrictMcpConfig

    // Assert
    command.Arguments.Should().Be("--strict-mcp-config")

[<Fact>]
let ``withPluginDirectory appends plugin-dir flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withPluginDirectory @"C:\Plugins"

    // Assert
    command.Arguments.Should().Be(@"--plugin-dir ""C:\\Plugins""")

[<Fact>]
let ``withSettings appends settings flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withSettings "settings.json"

    // Assert
    command.Arguments.Should().Be("--settings \"settings.json\"")

[<Fact>]
let ``withJsonSchema appends json-schema flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withJsonSchema """{"type":"object"}"""

    // Assert
    command.Arguments.Should().Contain("--json-schema")

[<Fact>]
let ``withVerbose appends verbose flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withVerbose

    // Assert
    command.Arguments.Should().Be("--verbose")

[<Fact>]
let ``withBare appends bare flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withBare

    // Assert
    command.Arguments.Should().Be("--bare")

[<Fact>]
let ``withReplayUserMessages appends replay flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withReplayUserMessages

    // Assert
    command.Arguments.Should().Be("--replay-user-messages")

[<Fact>]
let ``withIncludeHookEvents appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withIncludeHookEvents

    // Assert
    command.Arguments.Should().Be("--include-hook-events")

[<Fact>]
let ``withIncludePartialMessages appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withIncludePartialMessages

    // Assert
    command.Arguments.Should().Be("--include-partial-messages")

[<Fact>]
let ``withNoSessionPersistence appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withNoSessionPersistence

    // Assert
    command.Arguments.Should().Be("--no-session-persistence")

[<Fact>]
let ``withDisableSlashCommands appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withDisableSlashCommands

    // Assert
    command.Arguments.Should().Be("--disable-slash-commands")

[<Fact>]
let ``withExcludeDynamicSystemPromptSections appends flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withExcludeDynamicSystemPromptSections

    // Assert
    command.Arguments.Should().Be("--exclude-dynamic-system-prompt-sections")

[<Fact>]
let ``withWorktree appends worktree flag`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withWorktree

    // Assert
    command.Arguments.Should().Be("--worktree")

[<Fact>]
let ``withNamedWorktree appends worktree with name`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withNamedWorktree "feature-branch"

    // Assert
    command.Arguments.Should().Be("--worktree feature-branch")

[<Fact>]
let ``withPrompt appends escaped prompt text`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withPrint
        |> ClaudeCommand.withPrompt "Fix the bug"

    // Assert
    command.Arguments.Should().Be("--print \"Fix the bug\"")

[<Fact>]
let ``multiple flags compose in pipeline`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withPrint
        |> ClaudeCommand.withInputFormat InputFormat.StreamJson
        |> ClaudeCommand.withOutputFormat OutputFormat.StreamJson
        |> ClaudeCommand.withVerbose
        |> ClaudeCommand.withPermissionMode Default

    // Assert
    command.Arguments.Should().Be(
        "--print --input-format stream-json --output-format stream-json --verbose --permission-mode default")

[<Fact>]
let ``claude commands compose with CliCommand functions`` () =
    // Act
    let command =
        ClaudeCommand.create ()
        |> ClaudeCommand.withPrint
        |> ClaudeCommand.withOutputFormat OutputFormat.StreamJson
        |> CliCommand.withWorkingDirectory @"C:\Projects"
        |> CliCommand.withValidation CliWrap.CommandResultValidation.None

    // Assert
    command.TargetFilePath.Should().Be("claude") |> ignore
    command.Arguments.Should().Be("--print --output-format stream-json") |> ignore
    command.WorkingDirPath.Should().Be(@"C:\Projects") |> ignore
    command.Validation.Should().Be(CliWrap.CommandResultValidation.None)
