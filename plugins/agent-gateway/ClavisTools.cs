using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using FabioSoft.Nucleus.Contracts;
using ModelContextProtocol.Server;

namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// The MCP tools the in-Clavis agent calls to introspect and operate the running environment. Each tool
/// is a thin shell over the bus: read-only tools issue a request or read a file; control tools send a
/// message. The GatewayContext (bus, activity ring, directories, send registry) is injected from the MCP
/// server's DI container.
[McpServerToolType]
[ExcludeFromCodeCoverage(Justification = "Integration shell over the bus and filesystem; the gated-send logic it delegates to lives in SendableMessages.")]
internal sealed class ClavisTools(GatewayContext context)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    [McpServerTool(Name = "clavis_architecture")]
    [Description("Explain how Clavis is built: the microkernel, the message bus, plugins, windows and panels, and how to use these tools. Read this first for orientation.")]
    public string Architecture() => ClavisDocs.Architecture;

    [McpServerTool(Name = "list_plugins")]
    [Description("List the plugins currently loaded in Clavis, with their state and assembly path.")]
    public async Task<string> ListPlugins()
    {
        var list = await Request<ListPlugins, PluginList>(new ListPlugins());
        var plugins = list.Plugins.Select(plugin => new
        {
            id = plugin.Id,
            state = plugin.State.ToString(),
            assemblyPath = plugin.AssemblyPath,
            activatedAt = plugin.ActivatedAt.HasValue ? plugin.ActivatedAt.Value.ToString("o") : null,
        });
        return Serialize(plugins);
    }

    [McpServerTool(Name = "describe_plugin")]
    [Description("Return a plugin's own documentation (its CLAUDE.md): purpose, location, config, and the bus messages it publishes and subscribes to. Pass the plugin id from list_plugins.")]
    public string DescribePlugin([Description("The plugin id, e.g. 'WpfHost'.")] string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Contains('/') || id.Contains('\\') || id.Contains(".."))
        {
            return $"Invalid plugin id '{id}'.";
        }

        var path = Path.Combine(context.PluginsDirectory, id, "CLAUDE.md");
        return File.Exists(path)
            ? File.ReadAllText(path)
            : $"No CLAUDE.md found for plugin '{id}' (looked in {path}).";
    }

    [McpServerTool(Name = "workspace_snapshot")]
    [Description("Report what is on screen right now: the open windows and live panels, with which window and panel are focused and visible.")]
    public async Task<string> WorkspaceSnapshot()
    {
        var snapshot = await Request<WorkspaceSnapshotRequested, FabioSoft.Contracts.Workspace.WorkspaceSnapshot>(
            new WorkspaceSnapshotRequested());
        return Serialize(new
        {
            focusedWindowId = snapshot.FocusedWindowId,
            focusedPanelInstanceId = snapshot.FocusedPanelInstanceId,
            windows = snapshot.Windows.Select(window => new
            {
                windowId = window.WindowId,
                title = window.Title,
                isPrimary = window.IsPrimary,
                isFocused = window.IsFocused,
            }),
            panels = snapshot.Panels.Select(panel => new
            {
                instanceId = panel.InstanceId,
                kind = panel.Kind,
                title = panel.Title,
                windowId = panel.WindowId,
                placement = panel.Placement,
                isVisible = panel.IsVisible,
                isFocused = panel.IsFocused,
            }),
        });
    }

    [McpServerTool(Name = "read_log")]
    [Description("Read the tail of the current Clavis log file (deliberate log entries and dead letters, not the full message firehose).")]
    public string ReadLog(
        [Description("How many trailing lines to return.")] int lines = 200,
        [Description("Only lines at this level: TRACE, DEBUG, INFO, WARN, or ERROR.")] string? level = null,
        [Description("Only lines containing this text (case-insensitive).")] string? contains = null)
    {
        if (!Directory.Exists(context.LogsDirectory))
        {
            return $"No log directory at {context.LogsDirectory}.";
        }

        var newest = new DirectoryInfo(context.LogsDirectory)
            .GetFiles("clavis-*.log")
            .OrderByDescending(file => file.Name)
            .FirstOrDefault();
        if (newest is null)
        {
            return "No log files found.";
        }

        IEnumerable<string> matched = File.ReadLines(newest.FullName);
        if (!string.IsNullOrEmpty(level))
        {
            matched = matched.Where(line => line.Contains($"[{level.Trim().ToUpperInvariant()}", StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(contains))
        {
            matched = matched.Where(line => line.Contains(contains, StringComparison.OrdinalIgnoreCase));
        }

        return string.Join(Environment.NewLine, matched.TakeLast(Math.Max(1, lines)));
    }

    [McpServerTool(Name = "recent_activity")]
    [Description("Recent message-bus activity (the live firehose of every message, newest last), optionally filtered. Use this to see what is happening across plugins.")]
    public string RecentActivity(
        [Description("Only entries whose message type or source contains this text (case-insensitive).")] string? contains = null,
        [Description("Maximum number of entries to return.")] int limit = 100)
    {
        var entries = context.Activity.Recent(contains, Math.Max(1, limit)).Select(entry => new
        {
            timestamp = entry.Timestamp.ToString("o"),
            type = entry.Type,
            source = entry.Source,
            deadLetter = entry.Reason,
        });
        return Serialize(entries);
    }

    [McpServerTool(Name = "submit_prompt")]
    [Description("Submit a prompt to the Clavis conversation exactly as if the user typed it into the prompt box.")]
    public string SubmitPrompt([Description("The prompt text to submit.")] string text)
    {
        context.Bus.Send(new UserSubmittedPrompt(text));
        return Dispatched("submit_prompt");
    }

    [McpServerTool(Name = "open_panel")]
    [Description("Open a dockable panel of the given kind (e.g. 'git-log', 'events', 'markdown') in the active window.")]
    public string OpenPanel([Description("The panel kind.")] string kind)
    {
        context.Bus.Send(new OpenPanel(kind));
        return Dispatched("open_panel");
    }

    [McpServerTool(Name = "toggle_panel")]
    [Description("Toggle a panel of the given kind: open it if none is live, otherwise close or hide it.")]
    public string TogglePanel([Description("The panel kind.")] string kind)
    {
        context.Bus.Send(new TogglePanel(kind));
        return Dispatched("toggle_panel");
    }

    [McpServerTool(Name = "close_active_panel")]
    [Description("Close or dismiss the currently focused panel.")]
    public string CloseActivePanel()
    {
        context.Bus.Send(new CloseActivePanel());
        return Dispatched("close_active_panel");
    }

    [McpServerTool(Name = "focus_input")]
    [Description("Move keyboard focus to the conversation prompt input.")]
    public string FocusInput()
    {
        context.Bus.Send(new FocusInputRequested());
        return Dispatched("focus_input");
    }

    // Generous because a human answers this one; the popup always replies on dismissal, so in practice
    // the timeout only fires when the Selection plugin is not loaded.
    private static readonly TimeSpan AskUserTimeout = TimeSpan.FromMinutes(10);

    [McpServerTool(Name = "ask_user")]
    [Description("Ask the user a question with selectable options via Clavis's native selection popup. ALWAYS prefer this over the built-in AskUserQuestion tool when running inside Clavis. Returns {accepted, value}; accepted=false means the user dismissed the popup without choosing.")]
    public async Task<string> AskUser(
        [Description("The question shown above the input.")] string question,
        [Description("The selectable options (the value returned when chosen).")] string[] options,
        [Description("Optional supporting description per option, parallel to options.")] string[]? descriptions = null,
        [Description("Allow a typed free-text answer that is not in the options list.")] bool allowFreeText = false)
    {
        var selectionOptions = options
            .Select((value, index) => new SelectionOption(
                value, value, descriptions is not null && index < descriptions.Length ? descriptions[index] : ""))
            .ToList();

        var requestId = Guid.NewGuid();
        var pending = context.Selections.Register(requestId);
        context.Bus.Send(new SelectionRequested(requestId, question, selectionOptions, allowFreeText));

        var completed = await Task.WhenAny(pending, Task.Delay(AskUserTimeout));
        if (completed != pending)
        {
            context.Selections.Abandon(requestId);
            return Serialize(new { accepted = false, value = "", note = "No answer within the timeout." });
        }

        var answer = await pending;
        return Serialize(new { accepted = answer.Accepted, value = answer.Value });
    }

    [McpServerTool(Name = "send_message")]
    [Description("Send a whitelisted bus message for an action without a dedicated tool. Dangerous lifecycle/teardown messages are rejected. typeName is the message type (e.g. 'OpenConversation'); jsonPayload is a JSON object with its fields (e.g. {\"kind\":\"git-log\"}).")]
    public string SendMessage(
        [Description("The message type name, e.g. 'OpenPanel', 'OpenConversation', 'UserAborted'.")] string typeName,
        [Description("JSON object of the message's fields; pass {} for parameterless messages.")] string jsonPayload = "{}")
    {
        var result = context.Sendable.Send(context.Bus, typeName, jsonPayload);
        return Serialize(new { ok = result.Ok, message = result.Message });
    }

    private async Task<TResponse> Request<TRequest, TResponse>(TRequest request)
    {
        using var cancellation = new CancellationTokenSource(RequestTimeout);
        return await context.Bus.Request<TRequest, TResponse>(request, cancellation.Token);
    }

    private static string Dispatched(string tool) =>
        Serialize(new { ok = true, note = $"{tool} dispatched (fire-and-forget; effect is asynchronous)." });

    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);
}
