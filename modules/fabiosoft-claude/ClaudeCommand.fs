namespace FabioSoft.Claude

open CliWrap

type OutputFormat =
    | StreamJson
    | Text
    | Json

    override this.ToString() =
        match this with
        | StreamJson -> "stream-json"
        | Text -> "text"
        | Json -> "json"

type InputFormat =
    | StreamJson
    | Text

    override this.ToString() =
        match this with
        | StreamJson -> "stream-json"
        | Text -> "text"

type PermissionMode =
    | Default
    | AcceptEdits
    | Auto
    | BypassPermissions
    | DontAsk
    | Plan

    override this.ToString() =
        match this with
        | Default -> "default"
        | AcceptEdits -> "acceptEdits"
        | Auto -> "auto"
        | BypassPermissions -> "bypassPermissions"
        | DontAsk -> "dontAsk"
        | Plan -> "plan"

type PermissionPromptTool =
    | Stdio

    override this.ToString() =
        match this with
        | Stdio -> "stdio"

type EffortLevel =
    | Low
    | Medium
    | High
    | ExtraHigh
    | Max

    override this.ToString() =
        match this with
        | Low -> "low"
        | Medium -> "medium"
        | High -> "high"
        | ExtraHigh -> "xhigh"
        | Max -> "max"

[<RequireQualifiedAccess>]
module ClaudeCommand =

    let private escape (value: string) =
        let escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"")
        $"\"{escaped}\""

    let private append argument (command: Command) : Command =
        let existing = command.Arguments
        let arguments =
            if System.String.IsNullOrEmpty(existing) then argument
            else $"{existing} {argument}"
        command.WithArguments(arguments)

    type private CommandPipe = Command -> Command

    let private flag name (command: Command) =
        append $"--{name}" command

    let private flagWith name value (command: Command) =
        append $"--{name} {value}" command

    let private flagEscaped name value (command: Command) =
        flagWith name (escape value) command

    /// Creates a Command targeting the claude executable.
    let create () =
        Cli.Wrap("claude")

    /// Non-interactive mode: print response and exit. Skips the workspace trust dialog.
    let withPrint : CommandPipe = flag "print"
    
    /// Continue the most recent conversation in the current working directory.
    let withContinue : CommandPipe = flag "continue"
    
    /// When resuming, create a new session ID instead of reusing the original.
    let withForkSession : CommandPipe = flag "fork-session"
    
    /// Bypass all permission checks. Only recommended for sandboxes with no internet access.
    let withDangerouslySkipPermissions : CommandPipe = flag "dangerously-skip-permissions"
    
    /// Enable bypassing permissions as an option without it being the default.
    let withAllowDangerouslySkipPermissions : CommandPipe = flag "allow-dangerously-skip-permissions"
    
    /// Override verbose mode setting from config.
    let withVerbose : CommandPipe = flag "verbose"
    
    /// Minimal mode: skip hooks, LSP, plugins, attribution, auto-memory, and CLAUDE.md auto-discovery.
    let withBare : CommandPipe = flag "bare"
    
    /// Re-emit user messages from stdin back on stdout for acknowledgment (requires stream-json I/O).
    let withReplayUserMessages : CommandPipe = flag "replay-user-messages"
    
    /// Include all hook lifecycle events in the output stream (requires stream-json output).
    let withIncludeHookEvents : CommandPipe = flag "include-hook-events"
    
    /// Include partial message chunks as they arrive (requires --print and stream-json output).
    let withIncludePartialMessages : CommandPipe = flag "include-partial-messages"
    
    /// Disable session persistence — sessions will not be saved to disk and cannot be resumed.
    let withNoSessionPersistence : CommandPipe = flag "no-session-persistence"
    
    /// Disable all skills (slash commands).
    let withDisableSlashCommands : CommandPipe = flag "disable-slash-commands"
    
    /// Move per-machine system prompt sections into the first user message for better cross-user cache reuse.
    let withExcludeDynamicSystemPromptSections : CommandPipe = flag "exclude-dynamic-system-prompt-sections"
    
    /// Only use MCP servers from --mcp-config, ignoring all other MCP configurations.
    let withStrictMcpConfig : CommandPipe = flag "strict-mcp-config"
    
    /// Create a new git worktree for this session.
    let withWorktree : CommandPipe = flag "worktree"

    /// Resume a conversation by session ID.
    let withResume sessionId : CommandPipe = flagWith "resume" sessionId
    
    /// Use a specific session ID (must be a valid UUID).
    let withSessionId uuid : CommandPipe = flagWith "session-id" uuid
    
    /// Model for the session. Accepts an alias (e.g. "sonnet") or full name (e.g. "claude-sonnet-4-6").
    let withModel model : CommandPipe = flagWith "model" model
    
    /// Automatic fallback model when the default model is overloaded (only works with --print).
    let withFallbackModel model : CommandPipe = flagWith "fallback-model" model
    
    /// Create a new git worktree with a specific name.
    let withNamedWorktree name : CommandPipe = flagWith "worktree" name

    /// Set a display name for this session (shown in prompt box, /resume picker, and terminal title).
    let withName name : CommandPipe = flagEscaped "name" name
    
    /// System prompt to use for the session, replacing the default.
    let withSystemPrompt prompt : CommandPipe = flagEscaped "system-prompt" prompt
    
    /// Append additional instructions to the default system prompt.
    let withAppendSystemPrompt prompt : CommandPipe = flagEscaped "append-system-prompt" prompt
    
    /// Additional directory to allow tool access to.
    let withAddDirectory directory : CommandPipe = flagEscaped "add-dir" directory
    
    /// Agent for the current session, overriding the 'agent' setting.
    let withAgent agent : CommandPipe = flagEscaped "agent" agent
    
    /// JSON object defining custom agents (e.g. '{"reviewer": {"description": "...", "prompt": "..."}}').
    let withAgents json : CommandPipe = flagEscaped "agents" json
    
    /// Load MCP servers from a JSON config file.
    let withMcpConfig path : CommandPipe = flagEscaped "mcp-config" path
    
    /// Load plugins from a directory for this session only.
    let withPluginDirectory path : CommandPipe = flagEscaped "plugin-dir" path
    
    /// Path to a settings JSON file or a JSON string to load additional settings from.
    let withSettings pathOrJson : CommandPipe = flagEscaped "settings" pathOrJson
    
    /// JSON Schema for structured output validation.
    let withJsonSchema schema : CommandPipe = flagEscaped "json-schema" schema

    /// Output format (only works with --print): text, json (single result), or stream-json (realtime).
    let withOutputFormat (format: OutputFormat) : CommandPipe = flagWith "output-format" $"{format}"
    
    /// Input format (only works with --print): text (default) or stream-json (realtime streaming).
    let withInputFormat (format: InputFormat) : CommandPipe = flagWith "input-format" $"{format}"
    
    /// Permission mode for the session.
    let withPermissionMode (mode: PermissionMode) : CommandPipe = flagWith "permission-mode" $"{mode}"
    
    /// Transport for permission prompts in non-interactive mode.
    let withPermissionPromptTool (tool: PermissionPromptTool) : CommandPipe = flagWith "permission-prompt-tool" $"{tool}"
    
    /// Effort level for the session (controls reasoning depth).
    let withEffort (level: EffortLevel) : CommandPipe = flagWith "effort" $"{level}"
    
    /// Maximum dollar amount to spend on API calls (only works with --print).
    let withMaxBudgetUsd (amount: float) : CommandPipe = flagWith "max-budget-usd" $"{amount}"

    /// Tools to allow, with optional patterns restricting their use.
    let withAllowedTools (tools: ToolSpec list) : CommandPipe =
        tools |> List.map _.ToString() |> String.concat " " |> flagEscaped "allowed-tools"

    /// Tools to deny, with optional patterns restricting the denial scope.
    let withDisallowedTools (tools: ToolSpec list) : CommandPipe =
        tools |> List.map _.ToString() |> String.concat " " |> flagEscaped "disallowed-tools"

    /// Specify the list of available built-in tools for the session.
    let withTools (tools: BuiltInTool list) : CommandPipe =
        tools |> List.map _.ToString() |> String.concat "," |> flagEscaped "tools"

    /// Beta headers to include in API requests (API key users only).
    let withBetas (betas: string list) : CommandPipe =
        betas |> String.concat " " |> flagWith "betas"

    /// Positional prompt argument passed to claude.
    let withPrompt prompt : CommandPipe =
        append (escape prompt)
