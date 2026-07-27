using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

                    case StartSessionEffect start:
                        // Lazy start: a workspace's session is spawned on its first activation, never at
                        // creation or at load, so restoring eight workspaces does not spawn eight agents.
                        StartSession(start.WorkspaceId, start.WorkingDirectory);
                        break;

                    case DisposeSessionEffect dispose:
                        bus.Send(new DisposeSession(dispose.SessionId));
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

        void StartSession(Guid workspaceId, string workingDirectory)
        {
            // The session id is minted here, so the workspace owns the correlation from the outset and the
            // bridge's stream events route back without a second round trip.
            var sessionId = Guid.NewGuid();
            bus.Send(new StartNewSession(sessionId, workingDirectory, null));
            Apply(WorkspaceUpdate.SessionStarted(set, workspaceId, sessionId));
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
                        workspace.Slot))
                ],
                set.ActiveWorkspaceId));

        void Persist() => bus.Send(new SaveConfig(Id, WorkspaceFile.Serialize(set)));

        // Load the durable list, then activate whichever workspace it says was active - which is what starts
        // the first session, so the boot path is "load, activate one, spawn one agent".
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

        // A session's activity is a property of the session; mapping it onto a workspace is this plugin's job.
        // Activity is a live fact, so a change re-announces the list but is never persisted.
        subscriptions.Add(bus.Subscribe<SessionActivityChanged>(message =>
        {
            lock (gate)
            {
                Apply(
                    WorkspaceUpdate.ApplyActivity(set, message.SessionId, message.Activity, message.Detail),
                    persist: false);
            }

            return Task.CompletedTask;
        }));

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

        bus.Send(new GetConfig(Id));
        bus.LogInfo(Id, "Workspaces plugin activated; awaiting the workspace list");

        return Task.FromResult<IDisposable>(new PluginDisposable(subscriptions));

        string NextAccent() => AccentPalette.Assign(set.Workspaces.Select(workspace => workspace.AccentKey));
    }

    private sealed class PluginDisposable(IReadOnlyList<ISubscription> subscriptions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var subscription in subscriptions)
            {
                try { subscription.Dispose(); }
                catch { /* cleanup best-effort */ }
            }
        }
    }
}
