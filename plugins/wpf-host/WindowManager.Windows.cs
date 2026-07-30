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
    private WindowHost CreatePrimaryWindow()
    {
        var host = new WindowHost(_bus, _config, _keymap, _primaryWindowId, isPrimary: true);
        _focusedWindowId = host.WindowId;
        Application.Current.MainWindow = host.Window;
        Register(host);

        // Default placement until (and unless) the saved layout arrives. The surface starts empty: a
        // StateFound restore fills it with the saved slots, and a launch with no saved layout opens the
        // configured default panels once every plugin is up (see FlushRestoreSends).
        host.Window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        host.Window.Closing += (_, _) =>
        {
            SaveLayout();
            BeginShutdown();
        };

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
        var host = NewSecondaryHost(Guid.NewGuid());
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
        // Owner can only be set once the primary has been shown; during a pre-reveal restore it is still
        // hidden (WPF would throw), so the owner link is deferred to Reveal in that case.
        LinkToPrimaryOwner(host.Window);
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

    // Make the primary window own a secondary, so the pair minimize/restore together and the secondary
    // centres on the primary. WPF rejects an owner that has not been shown, so this is a no-op while the
    // primary is still hidden (a pre-reveal restore); Reveal links any such secondary once the primary is up.
    private void LinkToPrimaryOwner(Window secondary)
    {
        var primary = GetPrimary()?.Window;
        if (primary is not null && primary.IsVisible && !ReferenceEquals(secondary, primary))
        {
            secondary.Owner = primary;
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

    // Retire a secondary window once its last panel is gone (no docked panels and no slide-ins). The primary
    // window is never closed this way - it carries the window chrome and stays as the way back in, empty or not.
    private void CloseIfEmptySecondary(WindowHost host)
    {
        if (!host.IsPrimary && !host.Surface.PanelIds.Any() && host.SlideInIds.Count == 0)
        {
            CloseSecondaryWindow(host.WindowId);
        }
    }
}
