namespace FabioSoft.Nucleus.Plugins.AgentGateway;

/// Static, agent-facing documentation served through the MCP server: a short system-prompt guide and a
/// fuller architecture overview. Kept here (not the repo CLAUDE.md, which is build/style guidance for
/// human contributors) so the agent gets exactly what helps it operate the running environment.
internal static class ClavisDocs
{
    /// Appended to every in-Clavis session's system prompt - the guide to the Clavis MCP server's tools.
    /// Deliberately short: it points at the tools rather than inlining knowledge, so the heavy detail
    /// stays on-demand.
    public const string McpGuide =
        """
        You are running inside Clavis, a desktop UI host for Claude Code. The host exposes a "clavis" MCP
        server so you can inspect and operate the environment you run in. Use its tools when the user asks
        about Clavis itself, what is on screen, what plugins are loaded, or to drive the UI:
        - clavis_architecture - how Clavis is built (read this first if you need orientation).
        - list_plugins / describe_plugin - what is loaded and what each plugin does.
        - workspace_snapshot - which windows and panels are open, focused, and visible right now.
        - read_log / recent_activity - the host log file and the live message-bus activity.
        - open_panel / toggle_panel / close_active_panel / focus_input / submit_prompt - drive the UI.
        - ask_user - ask the user a question with selectable options via the native selection popup.
          ALWAYS use this instead of your built-in AskUserQuestion tool while running inside Clavis;
          the popup is the host's native UI and returns the user's pick (or a free-text answer when
          you allow it).
        - send_message - send a whitelisted bus message for actions without a dedicated tool.
        Prefer the dedicated tools over send_message. These tools act on the live Clavis app, not files.
        """;

    /// Returned by the clavis_architecture tool.
    public const string Architecture =
        """
        # Clavis architecture (for the in-Clavis agent)

        Clavis is a sci-fi-themed WPF desktop UI for Claude Code, built in F# on the **Nucleus
        microkernel**. A small kernel loads plugins that communicate over an in-process **message bus**.
        The host (FabioSoft.Clavis.Shell) owns no UI of its own - every visible thing is a plugin.

        ## Message bus
        Plugins talk by sending and subscribing to typed messages on the bus (publish/subscribe, plus
        request/response). Cross-plugin message types live in shared "contract" assemblies so their type
        identity is shared across plugins. There is no other coupling between plugins: to make something
        happen, you send the message a plugin is listening for. The bus also exposes an activity stream of
        every message (see the recent_activity tool) and a log stream persisted to ~/.clavis/logs (see the
        read_log tool).

        ## Plugins
        Each plugin implements IPlugin<TConfig> and is shipped as source next to the executable, compiled
        on launch and loaded into its own AssemblyLoadContext. Use list_plugins to see what is active and
        describe_plugin <id> to read a specific plugin's own documentation (purpose, location, and the
        messages it publishes and subscribes to). Core plugins include WpfHost (windows + docking),
        Conversation (the chat), ClaudeBridge (spawns and maps the Claude session - that is how *you* run),
        PanelRegistry, and several dockable panels (git-log, events, markdown, keymap).

        ## Windows and panels
        WpfHost owns a primary window (the conversation chrome) and any number of secondary panel-host
        windows, each tiling dockable panels in a split tree, plus edge "slide-in" panels. workspace_snapshot
        reports the live windows and panels with their focus/visibility. Drive the UI with open_panel,
        toggle_panel (summon or banish a kind), close_active_panel, and focus_input.

        ## Creating a new plugin (summary)
        Add a folder under src/plugins/<Name>/ with a .csproj and a class implementing
        IPlugin<TConfig> (Id, DefaultConfig, ValidateConfigAsync, ActivateAsync). In ActivateAsync,
        subscribe to the bus messages you care about and return an IDisposable that tears the
        subscriptions down. Any NEW cross-plugin message type must be added to a shared contract assembly
        under src/contracts/ (never defined inside the plugin), or it will not dispatch. Register the
        plugin for deployment in the Shell .fsproj. See the repo CLAUDE.md for the full conventions.

        ## Driving Clavis through these tools
        These MCP tools operate on the *running* application, not on files. submit_prompt enqueues a prompt
        exactly as if the user typed it. send_message sends a whitelisted bus message for actions without a
        dedicated tool; lifecycle/teardown messages (shutdown, closing windows, unloading plugins) are
        deliberately not available.
        """;
}
