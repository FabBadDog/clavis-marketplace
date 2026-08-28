using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// Window lifecycle: creating the primary, recreating and retiring secondaries.
internal sealed partial class WindowManager
{
    /// A workspace's own window: full chrome, one docking surface, and the workspace's unclosable chat inside
    /// it. `workspaceId` is Guid.Empty for the bootstrap window created before any workspace is known; the
    /// first WorkspaceActivated adopts it.
    private WindowHost CreateWorkspaceWindow(Guid windowId, Guid workspaceId)
    {
        var host = new WindowHost(_bus, _config, _keymap, windowId, isPrimary: true) { WorkspaceId = workspaceId };
        Register(host);

        // Seed the focus and MainWindow only while nothing holds them: Register reassigns both on every
        // Activated, so a workspace window created later must not steal them from the one on screen. MainWindow
        // is what SelectorWindow centres popups on, and it must never be null before the first activation.
        if (_focusedWindowId == Guid.Empty)
        {
            _focusedWindowId = host.WindowId;
        }

        Application.Current.MainWindow ??= host.Window;

        // Contributors announce their chrome once, at their own activation; a window created after that would
        // otherwise come up with empty regions.
        ApplyContributions();

        // Default placement until (and unless) the saved layout arrives. The surface starts empty: a
        // StateFound restore fills it with the saved slots, and a launch with no saved layout opens the
        // configured default panels once every plugin is up (see FlushRestoreSends).
        host.Window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // A workspace window cannot be closed, and that is not merely the missing cross: Alt+F4 and the system
        // menu would otherwise destroy a window the UI offers no way to get back, taking the workspace's chat
        // with it. Closing a workspace is the bar's gesture and quitting is ExitApplication; this window's own
        // close is neither, so it is refused until teardown.
        host.Window.Closing += (_, args) =>
        {
            if (!_tearingDown)
            {
                args.Cancel = true;
                return;
            }

            SaveLayout();
        };

        return host;
    }

    /// The window for a workspace that has none yet: adopt the still-unassigned bootstrap window if it is
    /// going spare, otherwise mint a fresh one. Adoption matters exactly once per launch - the bootstrap
    /// window already holds the panels the boot restored, so replacing it would show an empty workspace and
    /// orphan them.
    ///
    /// A newly minted window is shown only once the application has been revealed; before that every window
    /// is deliberately hidden and Reveal presents them together.
    private WindowHost AdoptOrCreateWorkspaceWindow(Guid workspaceId)
    {
        if (_windows.Values.FirstOrDefault(host => host.IsPrimary && host.WorkspaceId == Guid.Empty) is { } spare)
        {
            spare.WorkspaceId = workspaceId;

            // Adoption is when this window first learns which workspace-scoped contributions are its own.
            ApplyContributions();
            return spare;
        }

        var host = CreateWorkspaceWindow(Guid.NewGuid(), workspaceId);
        if (_revealed)
        {
            ShowWithFade(host.Window);
        }

        return host;
    }

    /// Start quitting, giving every declared participant a chance to finish first.
    ///
    /// With nothing declared this is exactly the old behaviour - one ApplicationShutdown, immediately - so the
    /// barrier costs nothing when nobody needs it.
    private void BeginShutdown()
    {
        if (!_shutdown.BeginPreparing())
        {
            return;
        }

        if (_shutdown.IsSatisfied)
        {
            CompleteShutdown();
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, _config.ShutdownGraceSeconds));
        _bus.LogInfo(
            "WpfHost",
            $"quitting; waiting up to {interval.TotalSeconds:F0}s for: {string.Join(", ", _shutdown.Outstanding)}");
        _bus.Send(new ShutdownPreparing());

        // The grace period is a backstop, not a schedule: participants normally answer in well under it. It
        // exists so a participant that never answers delays the quit rather than preventing it.
        //
        // Note what the clock actually measures. It starts here, at the *send*, and a participant whose subscriber
        // channel is busy may not process the broadcast for seconds - so the window covers queue latency as well
        // as the work, and the elapsed time is logged to keep that visible rather than guessable.
        var startedWaiting = DateTimeOffset.UtcNow;
        var grace = new DispatcherTimer { Interval = interval };
        grace.Tick += (_, _) =>
        {
            grace.Stop();
            if (!_shutdown.IsSatisfied)
            {
                _bus.LogWarn(
                    "WpfHost",
                    $"quitting without waiting further after {(DateTimeOffset.UtcNow - startedWaiting).TotalSeconds:F1}s"
                    + $" for: {string.Join(", ", _shutdown.Outstanding)}");
            }

            CompleteShutdown();
        };
        grace.Start();
    }

    private void CompleteShutdown()
    {
        if (_shutdown.TryExit())
        {
            _bus.Send(new ApplicationShutdown());
        }
    }

    private void RecreateSecondaryWindow(PersistedWindow entry, PersistedWorkspaceLayout? layout)
    {
        // Keep the saved id rather than minting a fresh one. A panel window is matched by id - unlike a chrome
        // window, which is matched by workspace because it is created anew each launch - so a new id here left
        // the saved layout pointing at a window that no longer existed: its entry could never be restored into
        // again, and capture kept carrying it over as an arrangement belonging to no live window. Ids are
        // unique within a launch and only the bootstrap window exists at this point, so the saved id is free.
        var host = NewSecondaryHost(
            _windows.ContainsKey(entry.WindowId) ? Guid.NewGuid() : entry.WindowId);
        host.WorkspaceId = entry.WorkspaceId;
        ApplyBounds(host.Window, entry.Bounds);
        RestoreLayout(host, layout);

        // Before the reveal the recreated window stays hidden - Reveal() presents all windows in one
        // entrance. A restore that lands after the reveal (failsafe path) shows it directly.
        if (_revealed)
        {
            ShowWithFade(host.Window);
        }
    }

    private WindowHost NewSecondaryHost(Guid windowId)
    {
        var host = new WindowHost(_bus, _config, _keymap, windowId, isPrimary: false);
        host.Window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // The owner link is deferred: it needs this window's workspace, which its caller sets after this
        // returns, and an owner that has actually been shown. Reveal and every show path call EnsureOwner.
        host.Window.Closing += (_, _) =>
        {
            // A window's slide-ins are not in its docking layout, so retire them explicitly: drop their
            // palette summon commands and dispose the panel instances.
            foreach (var slideInId in host.SlideInIds)
            {
                _bus.Send(new SlideInClosed(slideInId));
                _bus.Send(new PanelClosed(slideInId));
            }

            _windows.Remove(windowId);
            _bus.Send(new WindowClosed(windowId));
            ScheduleSave();
        };
        Register(host);
        return host;
    }

    // Make a workspace's own chrome window own that workspace's panel windows, so they minimize and restore
    // together and a tear-off centres on the window it came from. Resolved by workspace rather than by
    // "the primary": with one chrome window per workspace, owning a panel window to whichever workspace
    // happened to be active when it was created would tie it to the wrong one for the rest of the session.
    //
    // WPF rejects an owner that has not been shown, so this is a no-op while the owner is still hidden (a
    // pre-reveal restore) and is re-attempted whenever a window is revealed or shown.
    private void EnsureOwner(WindowHost host)
    {
        if (host.IsPrimary || host.Window.Owner is not null)
        {
            return;
        }

        var owner = (WorkspaceWindow(host.WorkspaceId) ?? GetPrimary())?.Window;
        if (owner is not null && owner.IsVisible && !ReferenceEquals(host.Window, owner))
        {
            host.Window.Owner = owner;
        }
    }

    private void CloseSecondaryWindow(Guid windowId)
    {
        if (_windows.TryGetValue(windowId, out var host) && !host.IsPrimary)
        {
            var window = host.Window;
            Motion.fadeWindow(window, 0.0, window.Close);
        }
    }

    // Retire a panel window once its last panel is gone (no docked panels and no slide-ins). A workspace's own
    // window is never closed this way - it is the workspace, and an empty one is still the way back in. It
    // cannot become empty anyway while its chat is unclosable, but the rule is stated rather than relied upon.
    private void CloseIfEmptySecondary(WindowHost host)
    {
        if (!host.IsPrimary && !host.Surface.PanelIds.Any() && host.SlideInIds.Count == 0)
        {
            CloseSecondaryWindow(host.WindowId);
        }
    }
}
