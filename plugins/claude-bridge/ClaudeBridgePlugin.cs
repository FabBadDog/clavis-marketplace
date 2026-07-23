using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

using FabioSoft.Claude;
using FabioSoft.Contracts.Session;
using FabioSoft.Nucleus.Contracts;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Core;

// F# models a Session as a type abbreviation (ISubject<input, output>), which is erased at the C#
// boundary, so this alias restores the short name for the duplex Claude session pipe.
using Session = System.Reactive.Subjects.ISubject<
    FabioSoft.Claude.SessionInput,
    Microsoft.FSharp.Core.FSharpResult<FabioSoft.Claude.StreamEvent, FabioSoft.Claude.ParsingError>>;

namespace FabioSoft.Nucleus.Plugins.ClaudeBridge;

public sealed class ClaudeBridgePlugin : IPlugin<ClaudeBridgeConfig>
{
    public string Id => "ClaudeBridge";

    /// The session's current position on the three switchable axes. The bridge sets them at launch and on
    /// every Set* command, so it is the source of truth the facade reports - the agent never does.
    private sealed record SessionAxes(string Model, string Mode, string Effort);

    public ClaudeBridgeConfig DefaultConfig => new();

    public Func<SessionConfig, Session>? SessionFactory { get; set; }

    // Injectable so the plugin's own tests run without touching the network; production leaves it null
    // and the real OAuth usage poll (UsageApi.fetchUsage) is used.
    public Func<Task<UsageWindow[]>>? UsageFetcher { get; set; }

    public Task<ConfigValidationResult> ValidateConfigAsync(ClaudeBridgeConfig config) => Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, ClaudeBridgeConfig config)
    {
        var sessions = new ConcurrentDictionary<Guid, Session>();
        var axesBySession = new ConcurrentDictionary<Guid, SessionAxes>();

        // Per-request memory of the provider's permission suggestions, keyed by request id: stashed as each
        // permission request streams in and recovered (then removed) when the user's chosen option comes
        // back, so the bridge can translate "suggestion-{index}" into the concrete updatedPermissions.
        var pendingSuggestions = new ConcurrentDictionary<string, PermissionUpdate[]>();

        // The current snapshot of one session's axes plus the full choice catalog, for (re)publication.
        AgentCapabilities CapabilitiesOf(Guid sessionId, SessionAxes axes) =>
            new(sessionId, axes.Model, axes.Mode, axes.Effort,
                ClaudeCatalog.Models, ClaudeCatalog.Modes, ClaudeCatalog.Efforts);

        // The hook catalogue is user-global (~/.claude/settings.json), so it is read once and shared
        // across every session; the per-session firing counters live on each session's closure below.
        var hookCatalog = new ClaudeHookCatalog();

        // Latest editor state (open file + selection), cached from the bus so each outgoing prompt can
        // carry the active-file/selection context the agent would otherwise get from an IDE connection.
        EditorStateChanged? editorState = null;
        var editorStateLock = new object();

        // The AgentGateway publishes its mcp-config JSON + system-prompt guide once on activation; cache the
        // latest so each session attaches them inline (the bus bootstrap buffer replays it before any session
        // starts, which is why no on-disk handshake is needed). Null until/unless the gateway is active.
        ClavisMcpAvailable? clavisMcp = null;
        var clavisMcpLock = new object();

        var startSubscription = bus.Subscribe<StartNewSession>(message =>
        {
            ClavisMcpAvailable? availableMcp;
            lock (clavisMcpLock)
            {
                availableMcp = clavisMcp;
            }

            var (mcpConfig, appendSystemPrompt, allowedTools) = ResolveClavisMcp(config.AttachClavisMcp, availableMcp);
            var sessionConfig = new SessionConfig(
                message.WorkingDirectory,
                message.Model is not null ? FSharpOption<string>.Some(message.Model) : FSharpOption<string>.None,
                FSharpOption<string>.None,
                appendSystemPrompt,
                mcpConfig,
                allowedTools);

            var sessionId = message.SessionId;
            var session = CreateSession(sessionConfig);
            sessions.TryAdd(sessionId, session);

            // Per-session provider-specific resolvers bound into the mapper: the hook firing counter is
            // private to this session's stream (observed serially, so no locking), and the permission
            // resolver reads the settings files scoped to this session's working directory.
            var hookCounters       = new Dictionary<string, int>();
            var permissionResolver = new ClaudePermissionResolver(message.WorkingDirectory);

            string ResolveHookDisplayName(string hookEvent)
            {
                var index = hookCounters.GetValueOrDefault(hookEvent, 0);
                hookCounters[hookEvent] = index + 1;
                return hookCatalog.ResolveDisplayName(hookEvent, index);
            }

            // The mapper returns null for provider-internal chatter (synthetic assistant text, local
            // no-op results); those never reach the bus.
            session
                .Where(result => result.IsOk)
                .Select(result =>
                {
                    // Remember the request's suggestions before mapping strips them to display labels, so
                    // the eventual response can name the concrete rule to persist. Log what the provider
                    // offered (kind + scope, or none) so a "why is there no wider-scope option" is answerable.
                    if (result.ResultValue is StreamEvent.PermissionRequest permission)
                    {
                        var suggestions = permission.Item.Suggestions.ToArray();
                        pendingSuggestions[permission.Item.RequestId] = suggestions;
                        bus.LogInfo(
                            "ClaudeBridge",
                            $"permission request {permission.Item.ToolName}: {suggestions.Length} suggestion(s) {DescribeSuggestions(suggestions)}");
                    }

                    return StreamEventMapper.Map(
                        sessionId, result.ResultValue, ResolveHookDisplayName, permissionResolver.Resolve);
                })
                .Where(mapped => mapped is not null)
                .Subscribe(mapped =>
                {
                    bus.Send(mapped!);
                    if (mapped is AgentInit init)
                    {
                        bus.Send(new SessionReady(sessionId, init.AgentSessionId, init.Model));

                        // The bridge sets these when launching the session, so it is the source of truth
                        // for the current selection and the available choices - the agent never reports
                        // them. The session launches in permission mode Default (Session.start) and the
                        // model's default effort. The reported model id is normalised onto the catalog id
                        // (init may report a dated long form) so pickers and indicators match by id.
                        var model = ClaudeCatalog.ResolveModel(init.Model)?.Id ?? init.Model;
                        var axes = new SessionAxes(model, ClaudeCatalog.DefaultModeId, ClaudeCatalog.DefaultEffortFor(model));
                        axesBySession[sessionId] = axes;
                        bus.Send(CapabilitiesOf(sessionId, axes));
                    }
                });

            session
                .Where(result => result.IsError)
                .Subscribe(result =>
                {
                    var error = StreamEventMapper.MapError(sessionId, result.ErrorValue);

                    // Parse failures used to vanish: AgentParsingError is never persisted by the host LogSink,
                    // so a stream line we could not process left no trace in the log files. Mirror it to the
                    // log here - genuine failures as warnings, the benign "type we don't model yet" cases as
                    // debug - so the activity stream and the on-disk logs both record unprocessable messages.
                    if (error.IsIgnorable)
                    {
                        bus.LogDebug("ClaudeBridge", $"stream message ignored: {error.Message}");
                    }
                    else
                    {
                        bus.LogWarn("ClaudeBridge", $"stream parse error: {error.Message}");
                    }

                    bus.Send(error);
                });

            // Provider handshake: asks the agent for its capabilities (command catalogue, etc.) and
            // forces the lazy provider to boot now (Session sends a throwaway local command alongside
            // the initialize request), so the session-start hooks, MCP loading and init event stream at
            // startup instead of waiting for the first user turn.
            session.OnNext(SessionInput.Initialize);

            bus.Send(new SessionStarted(sessionId));
            bus.LogInfo("ClaudeBridge", $"session started: {sessionId} ({message.WorkingDirectory})");
            return Task.CompletedTask;
        });

        var promptSubscription = bus.Subscribe<SendPrompt>(message =>
        {
            if (!sessions.TryGetValue(message.SessionId, out var session))
            {
                return Task.CompletedTask;
            }

            EditorStateChanged? snapshot;
            lock (editorStateLock)
            {
                snapshot = editorState;
            }

            session.OnNext(SessionInput.NewPrompt(EditorContext.Decorate(message.Text, snapshot)));

            return Task.CompletedTask;
        });

        var permissionSubscription = bus.Subscribe<SendPermissionResponse>(message =>
        {
            if (!sessions.TryGetValue(message.SessionId, out var session))
            {
                return Task.CompletedTask;
            }

            pendingSuggestions.TryRemove(message.RequestId, out var suggestions);
            var decision = StreamEventMapper.ResolvePermissionDecision(message.OptionId, suggestions);
            session.OnNext(SessionInput.NewPermissionResponse(message.RequestId, decision));
            return Task.CompletedTask;
        });

        var interruptSubscription = bus.Subscribe<InterruptSession>(message =>
        {
            if (sessions.TryGetValue(message.SessionId, out var session))
            {
                session.OnNext(SessionInput.Interrupt);
            }

            return Task.CompletedTask;
        });

        var disposeSubscription = bus.Subscribe<DisposeSession>(message =>
        {
            if (sessions.TryRemove(message.SessionId, out var session))
            {
                axesBySession.TryRemove(message.SessionId, out _);
                session.OnNext(SessionInput.Dispose);
                bus.LogInfo("ClaudeBridge", $"session disposed: {message.SessionId}");
            }

            return Task.CompletedTask;
        });

        // The three axis switches. Each validates against the catalog, instructs the running provider
        // session, updates the bridge's source-of-truth snapshot, and only then confirms with the
        // Agent*Changed event (plus a fresh AgentCapabilities so pickers see the new current values).
        // The provider applies set_model/set_permission_mode/effort without a counter-signal we could
        // await, so the confirmation follows the dispatched instruction.
        var setModelSubscription = bus.Subscribe<SetSessionModel>(message =>
        {
            if (!sessions.TryGetValue(message.SessionId, out var session)
                || !axesBySession.TryGetValue(message.SessionId, out var axes))
            {
                return Task.CompletedTask;
            }

            if (!ClaudeCatalog.IsKnownModel(message.Model))
            {
                bus.LogWarn("ClaudeBridge", $"ignoring switch to unknown model '{message.Model}'");
                return Task.CompletedTask;
            }

            session.OnNext(SessionInput.NewSetModel(message.Model));
            var effort = ClaudeCatalog.CoerceEffort(message.Model, axes.Effort);
            if (effort != axes.Effort && effort.Length > 0)
            {
                session.OnNext(SessionInput.NewSetEffort(effort));
            }

            var updated = axes with { Model = message.Model, Effort = effort };
            axesBySession[message.SessionId] = updated;
            bus.Send(new AgentModelChanged(message.SessionId, message.Model));
            if (effort != axes.Effort)
            {
                bus.Send(new AgentEffortChanged(message.SessionId, effort));
            }

            bus.Send(CapabilitiesOf(message.SessionId, updated));
            bus.LogInfo("ClaudeBridge", $"session {message.SessionId} switched to model '{message.Model}'");
            return Task.CompletedTask;
        });

        var setModeSubscription = bus.Subscribe<SetSessionMode>(message =>
        {
            if (!sessions.TryGetValue(message.SessionId, out var session)
                || !axesBySession.TryGetValue(message.SessionId, out var axes))
            {
                return Task.CompletedTask;
            }

            if (!ClaudeCatalog.IsKnownMode(message.Mode))
            {
                bus.LogWarn("ClaudeBridge", $"ignoring switch to unknown mode '{message.Mode}'");
                return Task.CompletedTask;
            }

            session.OnNext(SessionInput.NewSetPermissionMode(message.Mode));
            axesBySession[message.SessionId] = axes with { Mode = message.Mode };
            bus.Send(new AgentModeChanged(message.SessionId, message.Mode));
            bus.Send(CapabilitiesOf(message.SessionId, axesBySession[message.SessionId]));
            bus.LogInfo("ClaudeBridge", $"session {message.SessionId} switched to mode '{message.Mode}'");
            return Task.CompletedTask;
        });

        var setEffortSubscription = bus.Subscribe<SetSessionEffort>(message =>
        {
            if (!sessions.TryGetValue(message.SessionId, out var session)
                || !axesBySession.TryGetValue(message.SessionId, out var axes))
            {
                return Task.CompletedTask;
            }

            if (!ClaudeCatalog.SupportsEffort(axes.Model, message.Effort))
            {
                bus.LogWarn(
                    "ClaudeBridge",
                    $"ignoring effort '{message.Effort}': not supported by model '{axes.Model}'");
                return Task.CompletedTask;
            }

            session.OnNext(SessionInput.NewSetEffort(message.Effort));
            axesBySession[message.SessionId] = axes with { Effort = message.Effort };
            bus.Send(new AgentEffortChanged(message.SessionId, message.Effort));
            bus.Send(CapabilitiesOf(message.SessionId, axesBySession[message.SessionId]));
            bus.LogInfo("ClaudeBridge", $"session {message.SessionId} switched to effort '{message.Effort}'");
            return Task.CompletedTask;
        });

        var editorStateSubscription = bus.Subscribe<EditorStateChanged>(message =>
        {
            lock (editorStateLock)
            {
                editorState = message;
            }

            return Task.CompletedTask;
        });

        var editorClosedSubscription = bus.Subscribe<EditorClosed>(_ =>
        {
            lock (editorStateLock)
            {
                editorState = null;
            }

            return Task.CompletedTask;
        });

        var clavisMcpSubscription = bus.Subscribe<ClavisMcpAvailable>(message =>
        {
            lock (clavisMcpLock)
            {
                clavisMcp = message;
            }

            return Task.CompletedTask;
        });

        // Generic one-shot summarization behind the agent facade (IBus.Request<Summarize, SummaryResult>):
        // a fast headless `claude` call on haiku, leaving the configured session model untouched. Always
        // replies - empty when it cannot summarize - so the caller's Request never hangs and can fall back.
        var summarizeSubscription = bus.Subscribe<Summarize>(async message =>
        {
            var summary = await SummarizeWithClaude(message.Text, message.MaxLength);
            bus.Send(new SummaryResult(summary));
        });

        // Usage is account-global, independent of any session: poll it on its own cadence and publish a
        // provider-neutral AgentUsageReport for the usage indicator.
        var usagePoller = new UsagePoller(bus, UsageFetcher ?? (() => UsageApi.fetchUsage()));
        usagePoller.Start();

        bus.LogInfo("ClaudeBridge", "Claude bridge plugin activated");

        var disposable = new BridgeDisposable(
            sessions,
            usagePoller,
            startSubscription,
            promptSubscription,
            permissionSubscription,
            interruptSubscription,
            disposeSubscription,
            setModelSubscription,
            setModeSubscription,
            setEffortSubscription,
            editorStateSubscription,
            editorClosedSubscription,
            clavisMcpSubscription,
            summarizeSubscription);

        return Task.FromResult<IDisposable>(disposable);
    }

    // One-shot summarize via a headless `claude --print --bare --model haiku`. Returns "" on any failure
    // (claude missing, non-zero exit, timeout, empty output) so the bridge can still answer the request and
    // the caller falls back. Uses haiku explicitly for speed/cost; the configured session model is untouched.
    private static async Task<string> SummarizeWithClaude(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var prompt =
            $"Summarize the following text in at most {maxLength} characters, in the imperative mood. "
            + "Output only the summary line - no preamble, quotes, or trailing punctuation.\n\n"
            + text;
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo("claude")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in new[] { "--print", "--bare", "--model", "haiku", "--output-format", "text", prompt })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process is null)
            {
                return "";
            }

            var stdout = process.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { /* best-effort */ }
                return "";
            }

            if (process.ExitCode != 0)
            {
                return "";
            }

            var line = (await stdout)
                .Replace("\r\n", "\n")
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Length > 0) ?? "";
            return line.Length > maxLength ? line[..maxLength].TrimEnd() : line;
        }
        catch (Exception)
        {
            return "";
        }
    }

    private Session CreateSession(SessionConfig config)
    {
        if (SessionFactory is not null)
        {
            return SessionFactory(config);
        }

        return SessionModule.start(config);
    }

    // A compact "kind scope" description of the provider's permission suggestions, for the diagnostic log.
    private static string DescribeSuggestions(PermissionUpdate[] suggestions) =>
        suggestions.Length == 0
            ? ""
            : "[" + string.Join(", ", suggestions.Select(suggestion => suggestion switch
            {
                PermissionUpdate.AddRules r => $"addRules {r.behavior}->{r.destination}",
                PermissionUpdate.ReplaceRules r => $"replaceRules {r.behavior}->{r.destination}",
                PermissionUpdate.RemoveRules r => $"removeRules {r.behavior}->{r.destination}",
                PermissionUpdate.SetMode m => $"setMode {m.mode}->{m.destination}",
                PermissionUpdate.AddDirectories d => $"addDirectories->{d.destination}",
                PermissionUpdate.RemoveDirectories d => $"removeDirectories->{d.destination}",
                _ => "?"
            })) + "]";

    // The gateway registers its tools under this MCP server name, so allow-listing the server prefix
    // pre-approves every clavis tool. The agent introspects and drives its own host through these; making it
    // ask permission to read its own environment is wrong by design (and the prompt is a round-trip the
    // loopback-free stdio transport makes safe to skip).
    private const string ClavisMcpServer = "mcp__clavis";

    /// Resolve the AgentGateway MCP wiring for a new session from the cached ClavisMcpAvailable: the
    /// mcp-config JSON and system-prompt guide to attach inline, plus the tools to pre-allow. When
    /// attachment is disabled or the gateway never announced itself, the session gets no Clavis MCP wiring.
    private static (FSharpOption<string> McpConfig, FSharpOption<string> AppendSystemPrompt, FSharpList<ToolSpec> AllowedTools) ResolveClavisMcp(bool attach, ClavisMcpAvailable? available)
    {
        var noTools = FSharpList<ToolSpec>.Empty;
        if (!attach || available is null)
        {
            return (FSharpOption<string>.None, FSharpOption<string>.None, noTools);
        }

        var allowed = ListModule.OfArray(new[] { ToolSpec.NewMcpTool(ClavisMcpServer) });
        return (FSharpOption<string>.Some(available.ConfigJson), FSharpOption<string>.Some(available.Guide), allowed);
    }

    private sealed class BridgeDisposable(
        ConcurrentDictionary<Guid, Session> sessions,
        IDisposable usagePoller,
        params ISubscription[] subscriptions) : IDisposable
    {
        public void Dispose()
        {
            try { usagePoller.Dispose(); }
            catch { /* cleanup best-effort */ }

            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }

            foreach (var kvp in sessions)
            {
                try { kvp.Value.OnNext(SessionInput.Dispose); }
                catch { /* cleanup best-effort */ }
            }

            sessions.Clear();
        }
    }
}
