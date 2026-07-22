namespace FabioSoft.Claude

open System
open System.Diagnostics.CodeAnalysis
open FabioSoft.Json

type SessionId = SessionId of string

type BuiltInTool =
    | Bash
    | PowerShell
    | Edit
    | Write
    | Read
    | Glob
    | Grep
    | Agent
    | NotebookEdit
    | WebFetch
    | WebSearch
    | Monitor

    override this.ToString() =
        match this with
        | Bash -> "Bash"
        | PowerShell -> "PowerShell"
        | Edit -> "Edit"
        | Write -> "Write"
        | Read -> "Read"
        | Glob -> "Glob"
        | Grep -> "Grep"
        | Agent -> "Agent"
        | NotebookEdit -> "NotebookEdit"
        | WebFetch -> "WebFetch"
        | WebSearch -> "WebSearch"
        | Monitor -> "Monitor"

type ToolSpec =
    | Tool of BuiltInTool
    | ToolWithPattern of BuiltInTool * pattern: string
    | McpTool of name: string
    | McpToolWithPattern of name: string * pattern: string

    override this.ToString() =
        match this with
        | Tool tool -> $"{tool}"
        | ToolWithPattern (tool, pattern) -> $"{tool}({pattern})"
        | McpTool name -> name
        | McpToolWithPattern (name, pattern) -> $"{name}({pattern})"

type SessionConfig = {
    WorkingDirectory: string
    Model: string option
    SystemPrompt: string option
    AppendSystemPrompt: string option
    McpConfig: string option
    AllowedTools: ToolSpec list
}

[<RequireQualifiedAccess>]
module SessionConfig =

    [<ExcludeFromCodeCoverage>]
    let defaultConfig workingDirectory = {
        WorkingDirectory = workingDirectory
        Model = None
        SystemPrompt = None
        AppendSystemPrompt = None
        McpConfig = None
        AllowedTools = []
    }

type AssistantMessage = {
    Text: string
    IsFinal: bool
    /// True when the provider marked the message synthetic (model "<synthetic>"): locally generated
    /// output of a slash command or notice, never a real model answer.
    IsSynthetic: bool
}

type ToolUseInfo = {
    Name: string
    ToolUseId: string
    Input: string
    FullInput: string
}

type ToolResultInfo = {
    ToolUseId: string
    Summary: string
    FullOutput: string
    Duration: TimeSpan
}

type RateLimitInfo = {
    LimitType: string
    Status: string
    ResetsAt: DateTimeOffset
    IsUsingOverage: bool
}

type UsageInfo = {
    InputTokens: int
    OutputTokens: int
    CacheReadTokens: int
}

type HookInfo = {
    HookId: string
    HookName: string
    HookEvent: string
}

type HookOutcome = {
    HookId: string
    HookName: string
    HookEvent: string
    Outcome: string
    ExitCode: int option
    Stdout: string
    Stderr: string
}

type ResultData = {
    SessionId: SessionId
    CostUsd: float
    Duration: TimeSpan
    Model: string
    ResultText: string
    IsError: bool
    /// The provider's num_turns: 0 marks a local no-op result (a slash command handled without any
    /// agent turn), so consumers can tell it apart from a real turn's completion.
    NumTurns: int
}

type PermissionRuleSpec = { ToolName: string; RuleContent: string option }

/// A structured permission-rule update, matching Claude Code's PermissionUpdate shape. Carried both ways:
/// inbound as the request's permission_suggestions (the "always" options the user may pick) and outbound
/// as the updatedPermissions echoed back on an allow decision so the provider remembers the chosen rule.
/// Behavior is "allow" | "deny" | "ask"; destination is one of "session" | "localSettings" |
/// "projectSettings" | "userSettings" | "cliArg".
type PermissionUpdate =
    | AddRules of rules: PermissionRuleSpec list * behavior: string * destination: string
    | ReplaceRules of rules: PermissionRuleSpec list * behavior: string * destination: string
    | RemoveRules of rules: PermissionRuleSpec list * behavior: string * destination: string
    | SetMode of mode: string * destination: string
    | AddDirectories of directories: string list * destination: string
    | RemoveDirectories of directories: string list * destination: string

type PermissionRequestInfo = {
    RequestId: string
    ToolName: string
    ToolUseId: string option
    Input: string
    DecisionReason: string option
    DecisionReasonType: string option
    /// The provider's suggested permission updates for this request - the concrete "always" options the
    /// user may pick (e.g. add an allow rule for Bash(git*)). Empty when the provider offers none.
    Suggestions: PermissionUpdate list
}

type CommandDescriptor = {
    Name: string
    Description: string
    ArgumentHint: string
}

type StreamEvent =
    | Init of sessionId: SessionId * model: string * slashCommands: string list
    | Commands of commands: CommandDescriptor list
    | SessionEnded of exitCode: int * detail: string
    | SessionAlreadyExited
    | LogMessage of text: string
    | ApiCallRetry
    | Compacting
    | Thinking of summary: string
    | ThinkingTokens of estimatedTokens: int
    | RateLimit of RateLimitInfo
    | ToolUse of ToolUseInfo
    | ToolResult of ToolResultInfo
    | TextDelta of text: string
    | Assistant of AssistantMessage
    | Usage of UsageInfo
    | Result of ResultData
    | HookStart of HookInfo
    | HookComplete of HookOutcome
    | PermissionRequest of PermissionRequestInfo
    | Aborted

type PermissionDecision =
    | Allow of updatedPermissions: PermissionUpdate list
    | Deny

type SessionInput =
    | Initialize
    | Prompt of text: string
    | PermissionResponse of requestId: string * decision: PermissionDecision
    /// Switch the running session to another model (control_request set_model).
    | SetModel of model: string
    /// Switch the running session's permission mode (control_request set_permission_mode).
    | SetPermissionMode of mode: string
    /// Switch the reasoning effort via the provider's non-interactive `/effort` command (the stream-json
    /// protocol has no set_effort control request).
    | SetEffort of level: string
    | Interrupt
    | Dispose

type ParsingError =
    | EmptyInput
    | JsonError of JsonError
    | MissingTypeField of rawJson: string
    | UnknownMessageType of messageType: string
    | UnknownSystemSubtype of subtype: string
    | MissingRequiredField of messageType: string * fieldName: string
    | InvalidMessageStructure of messageType: string * detail: string

[<RequireQualifiedAccess>]
module ParsingError =

    let getMessage error =       

        match error with
        | EmptyInput -> "Empty input line"
        | JsonError jsonError -> JsonError.getMessage jsonError
        | MissingTypeField rawJson -> $"Missing 'type' field in: {rawJson}"
        | UnknownMessageType messageType -> $"Unknown message type: {messageType}"
        | UnknownSystemSubtype subtype -> $"Unknown system subtype: {subtype}"
        | MissingRequiredField (messageType, fieldName) ->
            $"Missing required field '{fieldName}' in {messageType} message"
        | InvalidMessageStructure (messageType, detail) ->
            $"Invalid {messageType} message structure: {detail}"

    let isIgnorable error =

        match error with
        | UnknownMessageType _
        | UnknownSystemSubtype _ -> true
        | _ -> false
