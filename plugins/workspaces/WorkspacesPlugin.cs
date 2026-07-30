using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// The single authority for workspace identity: which workspaces exist, what each is called, its accent, its
/// working directory, its slot, its live agent session, and its derived activity. It owns no window and no chat
/// state - every consumer reacts to the list it broadcasts.
///
/// Impure shell only: every decision is a pure `WorkspaceUpdate` operation, and this file translates the
/// resulting effects into bus messages and persistence.
public sealed class WorkspacesPlugin : IPlugin<WorkspacesConfig>
{
    public string Id => "Workspaces";

    public WorkspacesConfig DefaultConfig => new();

    public Task<ConfigValidationResult> ValidateConfigAsync(WorkspacesConfig config) =>
        Task.FromResult<ConfigValidationResult>(new ConfigValid());

    public Task<IDisposable> ActivateAsync(IBus bus, WorkspacesConfig config)
    {
        var defaultWorkingDirectory = string.IsNullOrWhiteSpace(config.DefaultWorkingDirectory)
            ? Directory.GetCurrentDirectory()
            : config.DefaultWorkingDirectory;

        var gate = new object();
        var set = WorkspaceSet.Empty;

        // The list arrives asynchronously as this plugin's config section. Until it does there are no
        // workspaces; the one-shot guard keeps a late or repeated answer from rebuilding a live set.
        var loaded = false;

        // The latest known agent instances. Which route a workspace takes to its session depends on what is
        // running *now*, and discovery is asynchronous, so the newest answer is kept rather than queried on
        // demand - an activation must not block on a provider round trip.
        IReadOnlyList<AgentInstance> instances = [];

        // Take-overs in flight, by the instance being taken over. Adoption can wait a long time (the agent may be
        // mid-turn) and can fail, so the workspace and session it was for have to survive the wait.
        var adopting = new Dictionary<string, (Guid WorkspaceId, Guid SessionId)>();

        // Workspaces that were activated before it was known what is running, and so still need their session.
        // Ordered, and normally holds exactly one entry: the workspace the persisted list said was active.
        var deferredSessions = new List<Guid>();

        // Whether the question "what is running?" has been settled - by an answer, by the wait running out, or by
        // discovery being switched off. Only then can a session be obtained without guessing.
        var discoveryResolved = config.FleetPollSeconds <= 0;

        // How many hand-offs the quit is still waiting on. Zero outside a shutdown, so an ordinary release during
        // normal use is not mistaken for one of them.
        var parksAwaited = 0;

        var subscriptions = new List<ISubscription>();

        void Apply((WorkspaceSet Set, WorkspaceEffect[] Effects) result, bool persist = true)
        {
            var changed = !ReferenceEquals(result.Set, set);
            set = result.Set;

            foreach (var effect in result.Effects)
            {
                switch (effect)
                {
                    case ActivatedEffect activated:
                        bus.Send(new WorkspaceActivated(activated.WorkspaceId, activated.SessionId));
                        break;

                    case ObtainSessionEffect obtain:
                        // Lazy: a workspace's session is obtained on its first activation, never at creation or
                        // at load, so restoring eight workspaces does not spawn eight agents.
                        //
                        // Held - and *only* this, never the activation itself - until it is known what is
                        // running. Delaying the activation instead was a real regression: consumers bind their
                        // per-workspace state (the window layout above all) to the workspace that is active when
                        // they restore, so with none active yet they restored against nothing and every panel
                        // vanished. Which route the session takes needs the discovery answer; being the active
                        // workspace does not.
                        if (discoveryResolved)
                        {
                            ObtainSession(obtain.WorkspaceId);
                        }
                        else if (!deferredSessions.Contains(obtain.WorkspaceId))
                        {
                            deferredSessions.Add(obtain.WorkspaceId);
                        }

                        break;

                    case DisposeSessionEffect dispose:
                        bus.Send(new DisposeSession(dispose.SessionId));
                        break;

                    case ParkSessionEffect park:
                        // Keep-running rather than stop: the agent carries on without Clavis and is picked back
                        // up on the next launch.
                        bus.Send(new ReleaseAgentInstance(park.SessionId, ReleaseMode.KeepRunning));
                        break;

                    case SessionStartedEffect started:
                        bus.Send(new WorkspaceSessionStarted(
                            started.WorkspaceId, started.SessionId, started.WorkingDirectory));
                        break;

                    case ClosedEffect closed:
                        bus.Send(new WorkspaceClosed(closed.WorkspaceId));
                        break;
                }
            }

            if (!changed)
            {
                return;
            }

            Announce();
            if (persist)
            {
                Persist();
            }
        }

        // Obtain a workspace's session by whichever of the three routes applies. The session id is minted here
        // either way, so the workspace owns the correlation from the outset and the bridge's stream events route
        // back without a second round trip.
        //
        // Naming matters more than it looks: the name is what the provider lists the agent under, and so what
        // finds the agent again after it has been parked - handing a session back renames nothing but changes its
        // id, leaving the name as the only durable link.
        void ObtainSession(Guid workspaceId)
        {
            if (set.ById(workspaceId) is not { } workspace)
            {
                return;
            }

            var sessionId = Guid.NewGuid();
            switch (SessionPlan.For(workspace, instances))
            {
                case TakeOver takeOver:
                    // Adoption may have to wait for the agent's turn to finish, so the workspace is marked as
                    // adopting now and the session is only recorded once the bridge confirms it.
                    Apply(WorkspaceUpdate.Adopting(set, workspaceId, true), persist: false);
                    adopting[takeOver.InstanceId] = (workspaceId, sessionId);
                    bus.Send(new AdoptAgentInstance(takeOver.InstanceId, sessionId));
                    bus.LogInfo(Id, $"taking over agent {takeOver.InstanceId} for workspace '{workspace.Name}'");
                    break;

                case ResumeConversation resume:
                    bus.Send(new ResumeSession(
                        sessionId, resume.WorkingDirectory, resume.AgentSessionId, resume.Name));
                    Apply(WorkspaceUpdate.SessionStarted(set, workspaceId, sessionId));
                    bus.LogInfo(Id, $"reopening the conversation of workspace '{workspace.Name}'");
                    break;

                case StartFresh start:
                    bus.Send(new StartNewSession(sessionId, start.WorkingDirectory, null, start.Name));
                    Apply(WorkspaceUpdate.SessionStarted(set, workspaceId, sessionId));
                    break;
            }
        }

        void Announce() =>
            bus.Send(new WorkspaceListChanged(
                [
                    .. set.InSlotOrder().Select(workspace => new WorkspaceInfo(
                        workspace.WorkspaceId,
                        workspace.Name,
                        workspace.AccentKey,
                        workspace.WorkingDirectory,
                        workspace.SessionId,
                        workspace.Activity,
                        workspace.ActivityDetail,
                        workspace.ActivitySince,
                        workspace.Slot,
                        workspace.IsFleetAgent,
                        workspace.IsAdopting))
                ],
                set.ActiveWorkspaceId));

        void Persist() => bus.Send(new SaveConfig(Id, WorkspaceFile.Serialize(set)));

        // Obtain the sessions of workspaces that were activated before it was known what is running.
        //
        // The wait exists because which route a workspace takes to its session depends on whether an agent is
        // already running its conversation. Deciding that before the first discovery answer would always read as
        // "nothing is running": a parked agent would be left running while its transcript was reopened
        // separately, giving one conversation two lives and orphaning the agent.
        void FlushDeferredSessions()
        {
            if (!discoveryResolved || deferredSessions.Count == 0)
            {
                return;
            }

            var pending = deferredSessions.ToArray();
            deferredSessions.Clear();
            foreach (var workspaceId in pending)
            {
                ObtainSession(workspaceId);
            }
        }

        void Load(string? rawConfig)
        {
            WorkspaceSet parsed;
            try
            {
                parsed = WorkspaceFile.Parse(rawConfig, defaultWorkingDirectory);
            }
            catch (Exception exception)
            {
                bus.LogError(Id, $"Reading the workspace list failed, starting from a default: {exception.Message}");
                parsed = WorkspaceSet.Empty;
            }

            if (parsed.Workspaces.Count == 0)
            {
                parsed = WorkspaceFile.Default(defaultWorkingDirectory);
            }

            lock (gate)
            {
                if (loaded)
                {
                    return;
                }

                loaded = true;
                set = parsed with { ActiveWorkspaceId = Guid.Empty };

                // Activate immediately. Consumers bind their per-workspace state to whichever workspace is active
                // when they restore, so there must be one from the outset; only the session waits.
                Apply(WorkspaceUpdate.Activate(set, parsed.ActiveWorkspaceId));
            }
        }

        subscriptions.Add(bus.Subscribe<ConfigResult>(result =>
        {
            switch (result)
            {
                case ConfigFound found when found.PluginId == Id:
                    Load(found.RawConfig);
                    break;
                case ConfigNotFound notFound when notFound.PluginId == Id:
                    Load(null);
                    break;
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<RequestWorkspaces>(_ =>
        {
            lock (gate)
            {
                Announce();
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<ActivateWorkspace>(message =>
        {
            lock (gate)
            {
                // Activation is not user data - the persisted `active` only decides where the next launch
                // starts - but it is cheap to save and means a restart resumes where you left off.
                Apply(WorkspaceUpdate.Activate(set, message.WorkspaceId));
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<ActivateWorkspaceSlot>(message =>
        {
            lock (gate)
            {
                Apply(WorkspaceUpdate.ActivateSlot(
                    set, message.Slot, defaultWorkingDirectory, NextAccent()));
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<CreateWorkspace>(message =>
        {
            lock (gate)
            {
                var directory = string.IsNullOrWhiteSpace(message.WorkingDirectory)
                    ? defaultWorkingDirectory
                    : message.WorkingDirectory;
                Apply(WorkspaceUpdate.Create(set, message.Name, directory, NextAccent()));
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<CloseActiveWorkspace>(_ =>
        {
            lock (gate)
            {
                Apply(WorkspaceUpdate.Close(set, set.ActiveWorkspaceId));
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<CloseWorkspace>(message =>
        {
            lock (gate)
            {
                Apply(WorkspaceUpdate.Close(set, message.WorkspaceId));
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<RenameWorkspace>(message =>
        {
            lock (gate)
            {
                Apply(WorkspaceUpdate.Rename(set, message.WorkspaceId, message.Name));
            }

            return Task.CompletedTask;
        }));

        // The provider's own session id for a conversation, learned when the session reports ready. This is the
        // durable half of a workspace's session: it is what reopens the conversation on the next launch.
        subscriptions.Add(bus.Subscribe<SessionReady>(message =>
        {
            lock (gate)
            {
                Apply(WorkspaceUpdate.ConversationKnown(set, message.SessionId, message.AgentSessionId));
            }

            return Task.CompletedTask;
        }));

        // What is running outside Clavis, refreshed on a timer. Agents no workspace claims become slotless tabs,
        // so work started in the provider's own agent view is visible and reachable here too.
        subscriptions.Add(bus.Subscribe<AgentInstancesAvailable>(message =>
        {
            lock (gate)
            {
                instances = message.Instances;
                discoveryResolved = true;
                Apply(WorkspaceUpdate.MergeFleetAgents(set, instances), persist: false);
                FlushDeferredSessions();
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<AgentInstanceAdopted>(message =>
        {
            lock (gate)
            {
                if (!adopting.Remove(message.InstanceId, out var pending))
                {
                    return Task.CompletedTask;
                }

                // A tab for a foreign agent becomes a real workspace at the moment it is taken over: from here on
                // it holds a slot, carries an accent, and is persisted like any other.
                Apply(WorkspaceUpdate.Adopting(set, pending.WorkspaceId, false), persist: false);
                Apply(WorkspaceUpdate.PromoteFleetAgent(set, pending.WorkspaceId, NextAccent()), persist: false);
                Apply(WorkspaceUpdate.SessionStarted(set, pending.WorkspaceId, pending.SessionId));
                Apply(WorkspaceUpdate.ConversationKnown(set, pending.SessionId, message.InstanceId));
            }

            return Task.CompletedTask;
        }));

        // A take-over that did not happen must not leave the workspace waiting forever. Its own conversation is
        // still on disk, so fall back to reopening that; a fleet tab has nothing to fall back to and simply stops
        // claiming to be adopting.
        subscriptions.Add(bus.Subscribe<AgentInstanceAdoptionFailed>(message =>
        {
            lock (gate)
            {
                if (!adopting.Remove(message.InstanceId, out var pending))
                {
                    return Task.CompletedTask;
                }

                Apply(WorkspaceUpdate.Adopting(set, pending.WorkspaceId, false), persist: false);
                if (set.ById(pending.WorkspaceId) is not { IsFleetAgent: false, HasConversation: true } workspace)
                {
                    bus.LogWarn(Id, $"could not take over agent {message.InstanceId}");
                    return Task.CompletedTask;
                }

                bus.LogWarn(
                    Id,
                    $"could not take over agent {message.InstanceId}; reopening the conversation of "
                    + $"'{workspace.Name}' from its transcript instead");
                bus.Send(new ResumeSession(
                    pending.SessionId, workspace.WorkingDirectory, workspace.AgentSessionId, workspace.Name));
                Apply(WorkspaceUpdate.SessionStarted(set, pending.WorkspaceId, pending.SessionId));
            }

            return Task.CompletedTask;
        }));

        subscriptions.Add(bus.Subscribe<AgentInstanceAdoptionWaiting>(message =>
        {
            lock (gate)
            {
                if (adopting.TryGetValue(message.InstanceId, out var pending))
                {
                    Apply(WorkspaceUpdate.Adopting(set, pending.WorkspaceId, true), persist: false);
                }
            }

            return Task.CompletedTask;
        }));

        // The user has decided the running turn is not worth waiting for. Re-issue the same adoption with the
        // wait overridden; the bridge keeps its claim across both, so this races nothing.
        subscriptions.Add(bus.Subscribe<ForceTakeOver>(message =>
        {
            lock (gate)
            {
                var pending = adopting
                    .Where(entry => entry.Value.WorkspaceId == message.WorkspaceId)
                    .Select(entry => (entry.Key, entry.Value.SessionId))
                    .FirstOrDefault();

                if (pending.Key is null)
                {
                    return Task.CompletedTask;
                }

                bus.LogInfo(Id, $"taking over agent {pending.Key} without waiting for its turn");
                bus.Send(new AdoptAgentInstance(pending.Key, pending.SessionId, true));
            }

            return Task.CompletedTask;
        }));

        // A session's activity is a property of the session; mapping it onto a workspace is this plugin's job.
        // Activity is a live fact, so a change re-announces the list but is never persisted.
        subscriptions.Add(bus.Subscribe<SessionActivityChanged>(message =>
        {
            lock (gate)
            {
                Apply(
                    WorkspaceUpdate.ApplyActivity(
                        set, message.SessionId, message.Activity, message.Detail, message.Since),
                    persist: false);
            }

            return Task.CompletedTask;
        }));

        // Quitting hands every live session back to a background agent, so the work outlives Clavis and is picked
        // back up on the next launch. That has to happen *before* the process goes away - handing back spawns a
        // process that must be running by then - and ApplicationShutdown takes effect immediately, so this plugin
        // declares itself a shutdown participant and the window owner holds the exit until it answers.
        bus.Send(new ShutdownParticipant(Id));
        subscriptions.Add(bus.Subscribe<ShutdownPreparing>(_ =>
        {
            lock (gate)
            {
                var (_, effects) = WorkspaceUpdate.ParkAll(set);
                parksAwaited = effects.Length;
                foreach (var effect in effects)
                {
                    if (effect is ParkSessionEffect park)
                    {
                        bus.Send(new ReleaseAgentInstance(park.SessionId, ReleaseMode.KeepRunning));
                    }
                }

                if (parksAwaited == 0)
                {
                    // Nothing to park - every workspace was restored but never visited, so no agent was ever
                    // started. Do not hold the quit at all.
                    bus.Send(new ShutdownPrepared(Id));
                    return Task.CompletedTask;
                }

                bus.LogInfo(Id, $"parking {parksAwaited} agent(s) so they keep running without Clavis");
            }

            return Task.CompletedTask;
        }));

        // Each hand-off confirms when its background agent has been spawned. Only once they all have is it safe to
        // let the process go: the spawn has to have happened while Clavis was still alive to do it.
        //
        // A hand-off that never confirms is not handled here on purpose. The window owner's grace period already
        // bounds the wait, and duplicating that bound here would mean two timeouts to reason about instead of one.
        subscriptions.Add(bus.Subscribe<AgentInstanceReleased>(message =>
        {
            lock (gate)
            {
                if (parksAwaited <= 0)
                {
                    return Task.CompletedTask;
                }

                if (!message.KeptRunning)
                {
                    bus.LogWarn(Id, $"agent {message.InstanceId} could not be parked and has stopped");
                }

                parksAwaited--;
                if (parksAwaited == 0)
                {
                    bus.LogInfo(Id, "every agent is parked; Clavis can exit");
                    bus.Send(new ShutdownPrepared(Id));
                }
            }

            return Task.CompletedTask;
        }));

        // The bar is a window the HOST owns; this plugin only contributes the strip into its region, so the
        // host stays free of workspace vocabulary and no second plugin mints a top-level window.
        Application.Current?.Dispatcher.InvokeAsync(() => bus.Send(new UiRegionContribution(
            "workspace-bar", Id, 0, () => Views.WorkspaceBarView.Create(bus))));

        // The overview is an ordinary panel kind, so it inherits open/toggle/close/restore/persist/tear-off/Esc
        // and a palette command for free. One per application, not per workspace: it is a view *of* all of them.
        void AnnounceOverviewPanel() => bus.Send(new PanelKindRegistration(
            "workspace-overview", "Workspaces", 420, 200, "", true,
            _ => Views.WorkspaceOverviewView.Create(bus))
        {
            Cardinality = PanelCardinality.OnePerApplication
        });
        subscriptions.Add(bus.Subscribe<PanelKindsRequested>(_ =>
        {
            AnnounceOverviewPanel();
            return Task.CompletedTask;
        }));
        AnnounceOverviewPanel();

        // F1-F11 switch (or create) a workspace by slot, F12 opens the overview. Declared here rather than
        // hardcoded in the keymap plugin, so the shortcuts ship with the feature that owns them. Application
        // scope, never system: a system-scope binding registers an OS global hotkey, which would steal F1-F12
        // from every application on the machine - F1 is help everywhere and F12 is devtools.
        void DeclareBindings() => bus.Send(new DefaultBindingsDeclared(Id, WorkspaceBindings.Defaults));
        subscriptions.Add(bus.Subscribe<RequestDefaultBindings>(_ =>
        {
            DeclareBindings();
            return Task.CompletedTask;
        }));
        DeclareBindings();

        // Ask what is running before the config has even arrived: the answer gates the first activation, so the
        // earlier it is requested the shorter that wait is. Both plugins are essential, so the bridge is up.
        var fleetPoll = TimeSpan.FromSeconds(Math.Max(1, config.FleetPollSeconds));
        Timer? discoveryTimer = null;
        Timer? discoveryWaitTimer = null;
        if (config.FleetPollSeconds > 0)
        {
            bus.Send(new AgentInstancesRequested());
            discoveryTimer = new Timer(_ => bus.Send(new AgentInstancesRequested()), null, fleetPoll, fleetPoll);

            // The wait cannot be unbounded: a provider that is missing or wedged never answers, and holding the
            // first activation forever would leave Clavis with no chat at all. Proceeding without the answer only
            // risks reopening a transcript instead of taking an agent over, which is recoverable.
            discoveryWaitTimer = new Timer(
                _ =>
                {
                    lock (gate)
                    {
                        if (discoveryResolved)
                        {
                            return;
                        }

                        discoveryResolved = true;
                        bus.LogWarn(Id, "no answer about running agents; starting sessions without taking any over");
                        FlushDeferredSessions();
                    }
                },
                null,
                TimeSpan.FromSeconds(InitialDiscoveryWaitSeconds),
                Timeout.InfiniteTimeSpan);
        }

        bus.Send(new GetConfig(Id));
        bus.LogInfo(Id, "Workspaces plugin activated; awaiting the workspace list");

        return Task.FromResult<IDisposable>(
            new PluginDisposable(subscriptions, discoveryTimer, discoveryWaitTimer));

        string NextAccent() => AccentPalette.Assign(set.Workspaces.Select(workspace => workspace.AccentKey));
    }

    /// How long the first activation waits to learn what is running. Short on purpose: it is one provider query,
    /// and the cost of giving up early is only that a parked agent is not taken over.
    private const int InitialDiscoveryWaitSeconds = 6;

    private sealed class PluginDisposable(
        IReadOnlyList<ISubscription> subscriptions, params Timer?[] timers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var timer in timers)
            {
                try { timer?.Dispose(); }
                catch { /* cleanup best-effort */ }
            }

            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }
        }
    }
}
