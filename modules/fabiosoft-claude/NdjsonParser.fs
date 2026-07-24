namespace FabioSoft.Claude

open System
open System.IO
open System.Text.RegularExpressions
open FabioSoft.Json
open FsToolkit.ErrorHandling

[<RequireQualifiedAccess>]
module NdjsonParser =

    let private toParsingError error = ParsingError.JsonError error

    let private tryGetValue<'T> propertyName json =

        Json.tryGetValue<'T> propertyName json |> Result.mapError toParsingError

    let private getValueOrDefault<'T> propertyName defaultValue json =

        Json.getValueOrDefault<'T> propertyName defaultValue json |> Result.mapError toParsingError

    let private foldContent folder initialState (content: Json[]) =

        content
        |> Array.fold (fun accumulator item ->
            accumulator |> Result.bind (fun state -> folder state item)) (Ok initialState)

    // Anthropic message `content` is either an array of content blocks or, as shorthand, a plain string
    // (equivalent to a single text block). Replayed user prompts, sub-agent prompts and synthetic messages
    // use the string form, so reading `content` as a typed array crashes on them. Normalise both shapes to
    // a block array here; None means the field was absent entirely.
    let private contentBlocks message =

        result {
            let! raw = tryGetValue<Json> "content" message
            match raw with
            | Some (Json.Array items) -> return Some items
            | Some (Json.String text) ->
                return Some [| Json.Object [ "type", Json.String "text"; "text", Json.String text ] |]
            | Some _ -> return Some [||]
            | None -> return None
        }

    // Condense a shell command for the collapsed tool header: collapse whitespace to a single line, rewrite
    // a `& "…\Script.ext" rest` invocation to just `Script.ext rest` (the full path is in the expandable
    // detail), and cap the length. The untruncated command is preserved in FullInput.
    let private shortenCommand (command: string) =

        let singleLine = Regex.Replace(command, @"\s+", " ").Trim()
        let simplified =
            let invocation = Regex.Match(singleLine, "^&\\s+[\"']([^\"']+)[\"']\\s*(.*)$")
            if invocation.Success then
                let baseName = Path.GetFileName(invocation.Groups[1].Value)
                (baseName + " " + invocation.Groups[2].Value).Trim()
            else
                singleLine

        FabioSoft.Common.String.shorten 90 simplified

    let private summarizeToolInput (name: string) (input: Json) =

        result {
            match name with
            | "Skill" ->
                let! skill = getValueOrDefault<string> "skill" "" input
                let! args = getValueOrDefault<string> "args" "" input
                return if args = "" then skill else $"{skill} · {args}"
            | "Read" | "Glob" | "Grep" ->
                let! filePath = tryGetValue<string> "file_path" input
                match filePath with
                | Some value -> return value
                | None ->
                    let! path = tryGetValue<string> "path" input
                    match path with
                    | Some value -> return value
                    | None ->
                        let! pattern = tryGetValue<string> "pattern" input
                        return pattern |> Option.defaultValue ""
            | "Edit" | "Write" ->
                return! getValueOrDefault<string> "file_path" "" input
            | "Bash" | "PowerShell" ->
                let! command = tryGetValue<string> "command" input
                return command |> Option.defaultValue "" |> shortenCommand
            | _ ->
                return ""
        }

    let private parseInit root =

        result {
            let! sessionId = getValueOrDefault<string> "session_id" "" root
            let! model = getValueOrDefault<string> "model" "" root
            let! slashCommands = getValueOrDefault<string[]> "slash_commands" [||] root
            return [Init(SessionId sessionId, model, List.ofArray slashCommands)]
        }

    let private parseCommandDescriptor item =

        result {
            let! name = getValueOrDefault<string> "name" "" item
            let! description = getValueOrDefault<string> "description" "" item
            let! argumentHint = getValueOrDefault<string> "argumentHint" "" item
            return { Name = name; Description = description; ArgumentHint = argumentHint }
        }

    // The provider's `initialize` control request returns its full command catalogue (with
    // descriptions) in a control_response - the early, turn-free discovery path for the command palette.
    let private parseControlResponse root =

        result {
            let! outer = tryGetValue<Json> "response" root
            match outer with
            | None ->
                return []
            | Some outer ->
                let! inner = tryGetValue<Json> "response" outer
                match inner with
                | None ->
                    return []
                | Some inner ->
                    let! commands = tryGetValue<Json[]> "commands" inner
                    match commands with
                    | None ->
                        return []
                    | Some commands ->
                        let! descriptors =
                            foldContent (fun accumulated item ->
                                parseCommandDescriptor item
                                |> Result.map (fun descriptor -> accumulated @ [descriptor])) [] commands
                        return [Commands descriptors]
        }

    let private parseHookStarted root =

        result {
            let! name = getValueOrDefault<string> "hook_name" "" root
            if name = "" then
                return! Error(MissingRequiredField("hook_started", "hook_name"))
            else
                let! hookId = getValueOrDefault<string> "hook_id" "" root
                let! hookEvent = getValueOrDefault<string> "hook_event" "" root
                return [HookStart { HookId = hookId; HookName = name; HookEvent = hookEvent }]
        }

    let private parseHookResponse root =

        result {
            let! name = getValueOrDefault<string> "hook_name" "" root
            if name = "" then
                return! Error(MissingRequiredField("hook_response", "hook_name"))
            else
                let! hookId = getValueOrDefault<string> "hook_id" "" root
                let! hookEvent = getValueOrDefault<string> "hook_event" "" root
                let! outcome = getValueOrDefault<string> "outcome" "" root
                let! exitCode = tryGetValue<int> "exit_code" root
                let! stdout = getValueOrDefault<string> "stdout" "" root
                let! stderr = getValueOrDefault<string> "stderr" "" root
                return [HookComplete {
                    HookId = hookId
                    HookName = name
                    HookEvent = hookEvent
                    Outcome = outcome
                    ExitCode = exitCode
                    Stdout = stdout
                    Stderr = stderr }]
        }

    let private parseThinkingTokens root =

        result {
            let! estimated = getValueOrDefault<int> "estimated_tokens" 0 root
            return [ThinkingTokens estimated]
        }

    let private parseTaskStarted root =

        result {
            let! taskId = getValueOrDefault<string> "task_id" "" root
            let! description = getValueOrDefault<string> "description" "" root
            let! taskType = getValueOrDefault<string> "task_type" "" root
            return [TaskStarted(taskId, description, taskType)]
        }

    let private parseTaskNotification root =

        result {
            let! taskId = getValueOrDefault<string> "task_id" "" root
            let! status = getValueOrDefault<string> "status" "" root
            let! summary = getValueOrDefault<string> "summary" "" root
            return [TaskCompleted(taskId, status, summary)]
        }

    let private systemSubtypeHandlers: Map<string, Json -> Result<StreamEvent list, ParsingError>> =

        Map.ofList [
            "init", parseInit
            "hook_started", parseHookStarted
            "hook_response", parseHookResponse
            "api_retry", (fun _ -> Ok [ApiCallRetry])
            "compact_boundary", (fun _ -> Ok [Compacting])
            "thinking_tokens", parseThinkingTokens
            "task_started", parseTaskStarted
            "task_notification", parseTaskNotification
            // Hook progress is conveyed by the running hook's own spinner; recognise it so it is not an error.
            "hook_progress", (fun _ -> Ok [])
            // Interim task transitions (running -> completed) are recognised so they are not "unknown"
            // noise; the terminal task_notification carries the summary the tracker displays.
            "task_updated", (fun _ -> Ok [])
        ]

    let private parseSystem root =

        result {
            let! subtype = tryGetValue<string> "subtype" root
            match subtype with
            | Some subtype ->
                match systemSubtypeHandlers |> Map.tryFind subtype with
                | Some handler -> return! handler root
                | None -> return! Error(UnknownSystemSubtype subtype)
            | None ->
                return! Error(MissingRequiredField("system", "subtype"))
        }

    let private parseUsage message =

        result {
            let! usage = tryGetValue<Json> "usage" message
            match usage with
            | Some usage ->
                let! inputTokens = getValueOrDefault<int> "input_tokens" 0 usage
                let! outputTokens = getValueOrDefault<int> "output_tokens" 0 usage
                let! cacheReadTokens = getValueOrDefault<int> "cache_read_input_tokens" 0 usage
                if inputTokens = 0 && outputTokens = 0 && cacheReadTokens = 0 then
                    return []
                else
                    return [Usage {
                        InputTokens = inputTokens
                        OutputTokens = outputTokens
                        CacheReadTokens = cacheReadTokens }]
            | None ->
                return []
        }

    let private parseContentItem item =

        result {
            let! itemType = tryGetValue<string> "type" item
            match itemType with
            | Some "thinking" ->
                let! thinking = getValueOrDefault<string> "thinking" "" item
                return [Thinking thinking]
            | Some "tool_use" ->
                let! name = getValueOrDefault<string> "name" "" item
                let! toolUseId = getValueOrDefault<string> "id" "" item
                let! inputProperty = tryGetValue<Json> "input" item
                let! input =
                    match inputProperty with
                    | Some inputElement -> summarizeToolInput name inputElement
                    | None -> Ok ""
                // The full, untruncated input (raw JSON) for the expand-to-detail view. A Skill call carries
                // nothing beyond the skill + args already in the header, so it gets no detail (no expand).
                let fullInput =
                    match name with
                    | "Skill" -> ""
                    | _ -> inputProperty |> Option.map (fun json -> json.ToString()) |> Option.defaultValue ""
                return [ToolUse { Name = name; ToolUseId = toolUseId; Input = input; FullInput = fullInput }]
            | Some "text" ->
                let! text = getValueOrDefault<string> "text" "" item
                return if text <> "" then
                           [TextDelta text]
                       else
                           []
            | _ ->
                return []
        }

    let private collectContentEvents content =

        foldContent (fun events item ->
            parseContentItem item |> Result.map (fun newEvents -> events @ newEvents)) [] content

    let private extractFullText content =

        foldContent (fun texts item ->
            tryGetValue<string> "type" item
            |> Result.bind (fun itemType ->
                if itemType = Some "text" then
                    tryGetValue<string> "text" item
                    |> Result.map (Option.map (fun text -> text :: texts) >> Option.defaultValue texts)
                else
                    Ok texts)) [] content
        |> Result.map (List.rev >> String.concat "")

    let private parseAssistant root =

        result {
            let! message = tryGetValue<Json> "message" root
            match message with
            | None ->
                return! Error(MissingRequiredField("assistant", "message"))
            | Some message ->
                let! content = contentBlocks message
                match content with
                | None ->
                    return! Error(InvalidMessageStructure("assistant", "missing content array"))
                | Some content ->
                    let! model = getValueOrDefault<string> "model" "" message
                    let isSynthetic = model = "<synthetic>"
                    let! fullText = extractFullText content
                    if isSynthetic then
                        // Locally generated output (slash-command result, provider notice) - no text
                        // deltas or usage, just the flagged assistant text so consumers can drop it.
                        return
                            if fullText <> "" then
                                [Assistant { Text = fullText; IsFinal = true; IsSynthetic = true }]
                            else
                                []
                    else
                        let! events = collectContentEvents content
                        let! stopReason = tryGetValue<string> "stop_reason" message
                        let hasToolUse = events |> List.exists (function ToolUse _ -> true | _ -> false)
                        let isFinal = not hasToolUse && stopReason <> Some "tool_use"
                        let! usageEvents = parseUsage message
                        let withAssistant =
                            if fullText <> "" then
                                events @ [Assistant { Text = fullText; IsFinal = isFinal; IsSynthetic = false }]
                            else
                                events
                        return withAssistant @ usageEvents
        }

    // Lenient text extraction over the Json DU (no typed reads that could fault on a string-vs-array
    // mismatch): pull the readable text out of a tool_result content value, which may itself be a plain
    // string or an array of {type:text, text:...} blocks.
    let private textOfBlock block =

        match block with
        | Json.String text -> text
        | Json.Object properties ->
            properties
            |> List.tryPick (fun (key, value) ->
                match key, value with
                | "text", Json.String text -> Some text
                | _ -> None)
            |> Option.defaultValue ""
        | _ -> ""

    let private toolResultBlockText item =

        match item with
        | Json.Object properties ->
            match properties |> List.tryPick (fun (key, value) -> if key = "content" then Some value else None) with
            | Some (Json.String text) -> text
            | Some (Json.Array blocks) -> blocks |> Array.map textOfBlock |> String.concat ""
            | _ -> ""
        | _ -> ""

    // Reads the rich tool_use_result envelope leniently: its `content` may be a string, an array or absent,
    // so it is matched on the DU rather than read as a typed string (which would fault on the array shape).
    let private summarizeToolResult root =

        result {
            let! toolUseResult = tryGetValue<Json> "tool_use_result" root
            match toolUseResult with
            | Some toolUseResult ->
                let! durationMilliseconds = getValueOrDefault<int> "durationMs" 0 toolUseResult
                let! numFiles = tryGetValue<int> "numFiles" toolUseResult
                let contentText = toolResultBlockText toolUseResult
                let summary =
                    match numFiles with
                    | Some count -> $"{count} files"
                    | None -> FabioSoft.Common.String.shorten 80 contentText
                return (summary, contentText, TimeSpan.FromMilliseconds(float durationMilliseconds))
            | None ->
                return ("", "", TimeSpan.Zero)
        }

    let private parseToolResult root =

        result {
            let! message = tryGetValue<Json> "message" root
            match message with
            | None ->
                return! Error(MissingRequiredField("user", "message"))
            | Some message ->
                let! content = contentBlocks message
                match content with
                | None ->
                    return! Error(InvalidMessageStructure("user", "missing content array"))
                | Some content ->
                    let! summary, envelopeText, duration = summarizeToolResult root
                    return!
                        foldContent (fun results item ->
                            tryGetValue<string> "type" item
                            |> Result.bind (fun itemType ->
                                match itemType with
                                | Some "tool_result" ->
                                    getValueOrDefault<string> "tool_use_id" "" item
                                    |> Result.map (fun toolUseId ->
                                        // Prefer the per-block content for the full output; fall back to the
                                        // tool_use_result envelope. The summary keeps the envelope value, or a
                                        // shortened block when the envelope had none.
                                        let blockText = toolResultBlockText item
                                        let fullOutput = if blockText <> "" then blockText else envelopeText
                                        let effectiveSummary =
                                            if summary <> "" then summary
                                            else FabioSoft.Common.String.shorten 80 blockText
                                        results @ [ToolResult {
                                            ToolUseId = toolUseId
                                            Summary = effectiveSummary
                                            FullOutput = fullOutput
                                            Duration = duration }])
                                | _ ->
                                    Ok results)) [] content
        }

    let private parseResult root =

        result {
            let! sessionId = getValueOrDefault<string> "session_id" "" root
            let! costUsd = getValueOrDefault<float> "total_cost_usd" 0.0 root
            let! durationMilliseconds = getValueOrDefault<int> "duration_ms" 0 root
            let! isError = getValueOrDefault<bool> "is_error" false root
            // Absent num_turns counts as a real turn: only an explicit 0 marks a local no-op result.
            let! numTurns = getValueOrDefault<int> "num_turns" 1 root
            // The final answer/summary the provider prints once the turn ends. Read tolerantly: on some
            // result subtypes it is absent or non-string, in which case there is simply no summary text.
            let! resultJson = tryGetValue<Json> "result" root
            let resultText =
                match resultJson with
                | Some (Json.String text) -> text
                | _ -> ""
            let! modelUsage = tryGetValue<Json> "modelUsage" root
            let model =
                match modelUsage with
                | Some (Json.Object properties) ->
                    properties |> List.tryHead |> Option.map fst |> Option.defaultValue ""
                | _ -> ""
            return [Result {
                SessionId = SessionId sessionId
                CostUsd = costUsd
                Duration = TimeSpan.FromMilliseconds(float durationMilliseconds)
                Model = model
                ResultText = resultText
                IsError = isError
                NumTurns = numTurns }]
        }

    let private parseRuleSpec (json: Json) =

        result {
            let! toolName = getValueOrDefault<string> "toolName" "" json
            let! ruleContent = tryGetValue<string> "ruleContent" json
            return { ToolName = toolName; RuleContent = ruleContent }
        }

    let private parseRules json =

        result {
            let! rules = tryGetValue<Json> "rules" json
            match rules with
            | Some(Json.Array items) -> return! items |> Array.toList |> List.traverseResultM parseRuleSpec
            | _ -> return []
        }

    let private parseDirectories json =

        tryGetValue<string[]> "directories" json
        |> Result.map (Option.map Array.toList >> Option.defaultValue [])

    // One permission_suggestions entry -> the neutral PermissionUpdate. An unknown discriminator yields
    // None so a future provider addition is skipped rather than failing the whole request.
    let private parseSuggestion (json: Json) =

        result {
            let! updateType = getValueOrDefault<string> "type" "" json
            let! destination = getValueOrDefault<string> "destination" "" json
            match updateType with
            | "addRules" | "replaceRules" | "removeRules" ->
                let! rules = parseRules json
                let! behavior = getValueOrDefault<string> "behavior" "" json
                let build =
                    match updateType with
                    | "replaceRules" -> ReplaceRules
                    | "removeRules" -> RemoveRules
                    | _ -> AddRules
                return Some(build (rules, behavior, destination))
            | "setMode" ->
                let! mode = getValueOrDefault<string> "mode" "" json
                return Some(SetMode(mode, destination))
            | "addDirectories" ->
                let! directories = parseDirectories json
                return Some(AddDirectories(directories, destination))
            | "removeDirectories" ->
                let! directories = parseDirectories json
                return Some(RemoveDirectories(directories, destination))
            | _ -> return None
        }

    let private parseSuggestions request =

        tryGetValue<Json> "permission_suggestions" request
        |> Result.bind (function
            | Some(Json.Array items) ->
                items |> Array.toList |> List.traverseResultM parseSuggestion |> Result.map (List.choose id)
            | _ -> Ok [])

    let private parseControlRequest root =

        result {
            let! requestId = getValueOrDefault<string> "request_id" "" root
            let! request = tryGetValue<Json> "request" root
            match request with
            | None ->
                return! Error(MissingRequiredField("control_request", "request"))
            | Some request ->
                let! toolName = getValueOrDefault<string> "tool_name" "" request
                let! toolUseId = tryGetValue<string> "tool_use_id" request
                let! input =
                    tryGetValue<Json> "input" request
                    |> Result.bind (fun inputOption ->
                        match inputOption with
                        | Some inputElement -> summarizeToolInput toolName inputElement
                        | None -> Ok "")
                let! reason = tryGetValue<string> "decision_reason" request
                let! reasonType = tryGetValue<string> "decision_reason_type" request
                let! suggestions = parseSuggestions request
                return [PermissionRequest {
                    RequestId = requestId
                    ToolName = toolName
                    ToolUseId = toolUseId
                    Input = input
                    DecisionReason = reason
                    DecisionReasonType = reasonType
                    Suggestions = suggestions
                    RawRequest = request.ToString() }]
        }

    let private parseRateLimit root =

        result {
            let! info = tryGetValue<Json> "rate_limit_info" root
            match info with
            | Some info ->
                let! limitType = getValueOrDefault<string> "rateLimitType" "" info
                let! status = getValueOrDefault<string> "status" "" info
                let! resetsAtUnix = getValueOrDefault<int64> "resetsAt" 0L info
                let! isUsingOverage = getValueOrDefault<bool> "isUsingOverage" false info
                return [RateLimit {
                    LimitType = limitType
                    Status = status
                    ResetsAt = DateTimeOffset.FromUnixTimeSeconds(resetsAtUnix)
                    IsUsingOverage = isUsingOverage }]
            | None ->
                return []
        }

    let private typeHandlers: Map<string, Json -> Result<StreamEvent list, ParsingError>> =

        Map.ofList [
            "system", parseSystem
            "assistant", parseAssistant
            "user", parseToolResult
            "result", parseResult
            "rate_limit_event", parseRateLimit
            "control_request", parseControlRequest
            "control_response", parseControlResponse
        ]

    let parse (line: string) : Result<StreamEvent, ParsingError> list =

        if String.IsNullOrWhiteSpace(line) then
            [Error EmptyInput]
        else
            let inner =
                result {
                    let! json = Json.parse line |> Result.mapError toParsingError
                    let! messageType = tryGetValue<string> "type" json
                    match messageType with
                    | Some messageType ->
                        match typeHandlers |> Map.tryFind messageType with
                        | Some handler -> return! handler json
                        | None -> return! Error(UnknownMessageType messageType)
                    | None ->
                        return! Error(MissingTypeField(FabioSoft.Common.String.shorten 100 line))
                }
            match inner with
            | Ok events -> events |> List.map Ok
            | Error error -> [Error error]
