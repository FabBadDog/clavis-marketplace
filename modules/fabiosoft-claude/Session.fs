namespace FabioSoft.Claude

open System
open System.Diagnostics.CodeAnalysis
open System.Text
open System.Threading
open System.Reactive
open System.Reactive.Subjects
open CliWrap
open FabioSoft.Json
open FabioSoft.Process

type Session = ISubject<SessionInput, Result<StreamEvent, ParsingError>>

[<RequireQualifiedAccess; CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Session =

    let private encodeUserPrompt (prompt: string) =

        Json.Object [
            "type", Json.String "user"
            "message", Json.Object [
                "role", Json.String "user"
                "content", Json.Array [|
                    Json.Object [
                        "type", Json.String "text"
                        "text", Json.String prompt
                    ]
                |]
            ]
        ]
        |> _.ToString()

    let private encodeInitializeRequest () =

        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String "clavis-initialize"
            "request", Json.Object [
                "subtype", Json.String "initialize"
            ]
        ]
        |> _.ToString()

    /// The throwaway local command sent right after the initialize control request. In --print
    /// stream-json mode the provider boots lazily: it emits nothing - no SessionStart hooks, no
    /// system/init, not even the initialize control_response - until the first user message arrives.
    /// A local slash command forces that boot eagerly (hooks fire, MCP servers load, init streams)
    /// without running an agent turn: it costs no API call and answers with a synthetic assistant
    /// message plus a num_turns=0 result, both of which the parser flags so consumers drop them.
    [<Literal>]
    let BootCommand = "/cost"

    /// A control request that sets one session property (set_model / set_permission_mode). The request id
    /// only needs to be unique within the session; the response is informational.
    let private encodeSetRequest (subtype: string) (field: string) (value: string) =

        Json.Object [
            "type", Json.String "control_request"
            "request_id", Json.String $"""clavis-{subtype}-{Guid.NewGuid().ToString("N")}"""
            "request", Json.Object [
                "subtype", Json.String subtype
                field, Json.String value
            ]
        ]
        |> _.ToString()

    let private encodeRuleSpec (spec: PermissionRuleSpec) =

        match spec.RuleContent with
        | Some content -> Json.Object [ "toolName", Json.String spec.ToolName; "ruleContent", Json.String content ]
        | None -> Json.Object [ "toolName", Json.String spec.ToolName ]

    let private encodeRuleUpdate updateType rules behavior destination =

        Json.Object [
            "type", Json.String updateType
            "rules", Json.Array(rules |> List.map encodeRuleSpec |> List.toArray)
            "behavior", Json.String behavior
            "destination", Json.String destination
        ]

    let private encodeDirectoryUpdate updateType directories destination =

        Json.Object [
            "type", Json.String updateType
            "directories", Json.Array(directories |> List.map Json.String |> List.toArray)
            "destination", Json.String destination
        ]

    let private encodePermissionUpdate update =

        match update with
        | AddRules(rules, behavior, destination) -> encodeRuleUpdate "addRules" rules behavior destination
        | ReplaceRules(rules, behavior, destination) -> encodeRuleUpdate "replaceRules" rules behavior destination
        | RemoveRules(rules, behavior, destination) -> encodeRuleUpdate "removeRules" rules behavior destination
        | SetMode(mode, destination) ->
            Json.Object [ "type", Json.String "setMode"; "mode", Json.String mode; "destination", Json.String destination ]
        | AddDirectories(directories, destination) -> encodeDirectoryUpdate "addDirectories" directories destination
        | RemoveDirectories(directories, destination) -> encodeDirectoryUpdate "removeDirectories" directories destination

    let private encodePermissionResponse (requestId: string) (decision: PermissionDecision) =

        let response =
            match decision with
            | Deny ->
                Json.Object [
                    "behavior", Json.String "deny"
                    "message", Json.String "Denied by user"
                ]
            | Allow updatedPermissions ->
                Json.Object [
                    "behavior", Json.String "allow"
                    "updatedInput", Json.Object []
                    "updatedPermissions", Json.Array(updatedPermissions |> List.map encodePermissionUpdate |> List.toArray)
                ]
        Json.Object [
            "type", Json.String "control_response"
            "response", Json.Object [
                "request_id", Json.String requestId
                "subtype", Json.String "success"
                "response", response
            ]
        ]
        |> _.ToString()

    let toSession (proc: Process) : Session =

        let output = new Subject<Result<StreamEvent, ParsingError>>()
        let stderrBuffer = StringBuilder()

        proc.StandardOutput.Add(fun line ->
            if not (String.IsNullOrWhiteSpace(line)) then
                for result in NdjsonParser.parse line do
                    output.OnNext result)

        proc.StandardError.Add(fun line ->
            stderrBuffer.AppendLine(line) |> ignore
            output.OnNext (Ok (LogMessage line)))

        let watchExit =
            async {
                try
                    let! exitCode = proc.Exited |> Async.AwaitTask
                    let trailing = stderrBuffer.ToString().Trim()
                    output.OnNext (Ok (SessionEnded (exitCode, trailing)))
                with
                | :? OperationCanceledException -> ()
                | ex -> output.OnNext (Ok (SessionEnded (-1, ex.Message)))
            }

        Async.Start(watchExit, CancellationToken.None)

        let mailbox =
            MailboxProcessor.Start(fun inbox ->
                let rec loop () = async {
                    let! message = inbox.Receive()
                    match message with
                    | Initialize ->
                        if not proc.Exited.IsCompleted then
                            encodeInitializeRequest () |> proc.StandardInput
                            // Force the lazy provider to boot now (see BootCommand) so the session-start
                            // hooks and init handshake stream at startup, not on the first real prompt.
                            encodeUserPrompt BootCommand |> proc.StandardInput
                        return! loop ()
                    | Prompt text ->
                        if proc.Exited.IsCompleted then
                            output.OnNext (Ok SessionAlreadyExited)
                        else
                            encodeUserPrompt text |> proc.StandardInput
                        return! loop ()
                    | PermissionResponse (requestId, decision) ->
                        encodePermissionResponse requestId decision |> proc.StandardInput
                        return! loop ()
                    | SetModel model ->
                        if not proc.Exited.IsCompleted then
                            encodeSetRequest "set_model" "model" model |> proc.StandardInput
                        return! loop ()
                    | SetPermissionMode mode ->
                        if not proc.Exited.IsCompleted then
                            encodeSetRequest "set_permission_mode" "mode" mode |> proc.StandardInput
                        return! loop ()
                    | SetEffort level ->
                        // No set_effort control request exists; the provider's /effort command supports
                        // non-interactive use, so it is sent as an ordinary user message.
                        if not proc.Exited.IsCompleted then
                            encodeUserPrompt $"/effort {level}" |> proc.StandardInput
                        return! loop ()
                    | Interrupt ->
                        output.OnNext (Ok Aborted)
                        proc.Interrupt()
                        return! loop ()
                    | Dispose ->
                        proc.Shutdown()
                        // Terminate the Rx stream so subscribers detach rather than lingering on a subject
                        // that will never emit again. OnCompleted (not Dispose) so a late buffered emission
                        // from the still-draining process is a no-op, not an ObjectDisposedException.
                        output.OnCompleted()
                }
                loop ())

        Subject.Create<SessionInput, Result<StreamEvent, ParsingError>>(
            Observer.Create<SessionInput>(Action<SessionInput>(mailbox.Post)),
            output)

    [<ExcludeFromCodeCoverage>]
    let private buildCommand (config: SessionConfig) =

        ClaudeCommand.create ()
        |> ClaudeCommand.withPrint
        |> ClaudeCommand.withInputFormat InputFormat.StreamJson
        |> ClaudeCommand.withOutputFormat OutputFormat.StreamJson
        |> ClaudeCommand.withVerbose
        |> ClaudeCommand.withIncludeHookEvents
        |> ClaudeCommand.withPermissionPromptTool Stdio
        |> ClaudeCommand.withPermissionMode Default
        |> (match config.Model with
            | Some model -> ClaudeCommand.withModel model
            | None -> id)
        |> (match config.SystemPrompt with
            | Some prompt -> ClaudeCommand.withSystemPrompt prompt
            | None -> id)
        |> (match config.AppendSystemPrompt with
            | Some prompt -> ClaudeCommand.withAppendSystemPrompt prompt
            | None -> id)
        |> (match config.McpConfig with
            | Some path -> ClaudeCommand.withMcpConfig path
            | None -> id)
        |> (match config.AllowedTools with
            | [] -> id
            | tools -> ClaudeCommand.withAllowedTools tools)
        |> CliCommand.withWorkingDirectory config.WorkingDirectory
        |> CliCommand.withValidation CommandResultValidation.None

    [<ExcludeFromCodeCoverage>]
    let start (config: SessionConfig) : Session =

        buildCommand config |> CliWrapProcess.start |> toSession
