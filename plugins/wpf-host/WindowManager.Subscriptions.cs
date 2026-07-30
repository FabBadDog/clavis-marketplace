using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// The one place every bus subscription is declared, so the routing table reads as a table.
internal sealed partial class WindowManager
{
    private void SubscribeToBus()
    {
        // Routed by region owner rather than broadcast: the bar owns exactly one region, everything else is
        // the primary's. Broadcasting to every window instead would hand the same view element to two visual
        // trees (secondary windows define title-bar regions too), which WPF rejects outright.
        _subscriptions.Add(_bus.Subscribe<UiRegionContribution>(contribution =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (contribution.RegionId == BarWindow.RegionId)
                {
                    _bar?.Regions.AddContribution(contribution);
                }
                else
                {
                    GetPrimary()?.Regions.AddContribution(contribution);
                }
            });
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<UiRegionRemoved>(removal =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (removal.RegionId == BarWindow.RegionId)
                {
                    _bar?.Regions.RemoveContribution(removal);
                }
                else
                {
                    GetPrimary()?.Regions.RemoveContribution(removal);
                }
            });
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<PanelInstanceReady>(ready =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => PlacePanel(ready));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<PanelStateChanged>(message =>
        {
            _panelState[message.InstanceId] = message.State;
            Application.Current.Dispatcher.InvokeAsync(ScheduleSave);
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ShowSlideIn>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
                _windows.Values.FirstOrDefault(window => window.HasSlideIn(message.InstanceId))?.ShowSlideIn(message.InstanceId));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CloseWindow>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => CloseSecondaryWindow(message.WindowId));
            return Task.CompletedTask;
        }));

        // The active panel's owner reports whether its status bar has content; collapse the primary window's
        // status row when it has none so the panel fills the space (the host knows no placeholder vocabulary).
        _subscriptions.Add(_bus.Subscribe<StatusBarAvailability>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetPrimary()?.SetStatusBarVisible(message.Available));
            return Task.CompletedTask;
        }));

        // The saved layout arrives as this plugin's runtime state; restore it onto the (still hidden)
        // primary, then reveal once the essential set is also up.
        _subscriptions.Add(_bus.Subscribe<StateResult>(result =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                OnStateResult(result);
                RevealWhenReady();
            });
            return Task.CompletedTask;
        }));

        // The essential plugins are up (Configuration among them, so the state answer normally precedes
        // this). If that answer cannot arrive - a failed Configuration plugin - the failsafe reveals with
        // the default placement after a short grace rather than never.
        _subscriptions.Add(_bus.Subscribe<EssentialPluginsReady>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _essentialsReady = true;
                RevealWhenReady();
                StartRevealFailsafe();
            });
            return Task.CompletedTask;
        }));

        // Restore sends are deferred until every plugin is up, so the registry has the panel kinds it
        // needs to resolve them. Reveal() first: bootstrap completion is the reveal's final guarantee,
        // queued at normal priority so it precedes the host's idle-priority no-window viability check.
        _subscriptions.Add(_bus.Subscribe<BootstrapComplete>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Reveal();
                _bootstrapComplete = true;
                FlushRestoreSends();
            });
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<KeymapChanged>(changed =>
        {
            _keymap.Update(changed.Bindings);
            var systemBindings = changed.Bindings.Where(binding => binding.Scope == KeymapScope.System).ToList();
            Application.Current.Dispatcher.InvokeAsync(() => _globalHotkey?.SetSystemBindings(systemBindings));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CommandsAvailable>(available =>
        {
            _keymap.UpdateCommands(available.Commands);
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<TogglePanel>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => TogglePanel(message.Kind, message.WorkspaceId));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CloseActivePanel>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(CloseActivePanel);
            return Task.CompletedTask;
        }));

        // A named panel instance is closed by id (e.g. when a markdown definition is deleted, its owner
        // closes every open panel bound to it). Completes the previously-unwired ClosePanel contract.
        _subscriptions.Add(_bus.Subscribe<ClosePanel>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => ClosePanel(message.InstanceId));
            return Task.CompletedTask;
        }));

        // Retitle a live panel's tab (e.g. when its markdown definition is renamed while docked).
        _subscriptions.Add(_bus.Subscribe<SetPanelTitle>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => RetitlePanel(message.InstanceId, message.Title));
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ToggleShortcutHelp>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => GetFocused()?.ToggleHelp());
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<CloseActiveWindow>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => CloseSecondaryWindow(_focusedWindowId));
            return Task.CompletedTask;
        }));

        // Which workspace is on screen. A layout migrated from version 1 (or written before the workspace list
        // existed) carries Guid.Empty as its workspace; the first activation adopts those entries onto the real
        // workspace, so an existing layout is kept rather than discarded as an orphan.
        _subscriptions.Add(_bus.Subscribe<WorkspaceActivated>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _activeWorkspaceId = message.WorkspaceId;
                if (_restoredLayout is { } restored)
                {
                    _restoredLayout = LayoutMigration.Adopt(restored, message.WorkspaceId);
                }

                foreach (var host in _windows.Values.Where(host => !host.IsPrimary && host.WorkspaceId == Guid.Empty))
                {
                    host.WorkspaceId = message.WorkspaceId;
                }

                // Swap each window onto this workspace's surface. A workspace seen for the first time gets a
                // fresh surface, and its saved panels are materialised into it right after.
                var firstVisit = !_windows.Values.Any(host => host.HasSurfaceFor(message.WorkspaceId));
                foreach (var host in _windows.Values)
                {
                    host.ActivateWorkspace(message.WorkspaceId);
                }

                if (firstVisit)
                {
                    RestoreWorkspacePanels(message.WorkspaceId);
                }

                // A workspace's extra windows travel with it: the ones belonging to another workspace go away,
                // and this workspace's come back. The primary is untouched - it is the constant.
                ApplyWorkspaceWindowVisibility();
                ScheduleSave();
            });
            return Task.CompletedTask;
        }));

        // The first workspace list is when orphans become knowable: a layout left behind by a workspace closed
        // in an earlier session has nothing to belong to, so it is discarded rather than carried forward for
        // ever. Unassigned (Guid.Empty) entries deliberately survive - those are adopted, not orphaned.
        _subscriptions.Add(_bus.Subscribe<WorkspaceListChanged>(message =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (_orphansDropped || _restoredLayout is not { } restored)
                {
                    return;
                }

                _orphansDropped = true;
                var live = message.Workspaces.Select(workspace => workspace.WorkspaceId).ToList();
                _restoredLayout = LayoutMigration.DropOrphans(restored, live);
            });
            return Task.CompletedTask;
        }));

        // Quitting is now an explicit intent rather than what `exit` happens to mean: the palette's `exit`
        // closes the active workspace, so this is the one gesture that ends the process. Persist the layout
        // first - the same order the primary window's own close uses.
        _subscriptions.Add(_bus.Subscribe<ExitApplication>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SaveLayout();
                BeginShutdown();
            });
            return Task.CompletedTask;
        }));

        // A plugin with work to do on the way out declares itself here, at its own activation. The bus's
        // bootstrap buffer replays declarations made before this subscription existed, so activation order
        // between the participant and this plugin does not matter.
        _subscriptions.Add(_bus.Subscribe<ShutdownParticipant>(message =>
        {
            _shutdown.Declare(message.PluginId);
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ShutdownPrepared>(message =>
        {
            if (!_shutdown.Ready(message.PluginId) || !_shutdown.IsPreparing)
            {
                return Task.CompletedTask;
            }

            // An answer can arrive after the application has already gone (the grace period expiring first is
            // exactly that case), and by then Application.Current is null. Completing directly is correct there:
            // there is no dispatcher left to marshal onto, and CompleteShutdown is idempotent.
            var application = Application.Current;
            if (application is null)
            {
                CompleteShutdown();
            }
            else
            {
                application.Dispatcher.InvokeAsync(CompleteShutdown);
            }

            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<SummonClavis>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(Summon);
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ToggleClavis>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(ToggleVisibility);
            return Task.CompletedTask;
        }));

        // Introspection: report what is currently on screen. Read on the UI thread (it touches live WPF
        // state), then answer with a single LayoutSnapshot - the response half of a bus Request.
        _subscriptions.Add(_bus.Subscribe<LayoutSnapshotRequested>(_ =>
        {
            Application.Current.Dispatcher.InvokeAsync(() => _bus.Send(BuildSnapshot()));
            return Task.CompletedTask;
        }));
    }
}
