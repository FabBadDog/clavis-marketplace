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
        var host = new WindowHost(_bus, _config, _keymap, () => _permissionPending, _primaryWindowId, isPrimary: true);
        _focusedWindowId = host.WindowId;
        Application.Current.MainWindow = host.Window;
        Register(host);

        // Default placement until (and unless) the saved layout arrives: centre-screen with the conversation
        // seeded, so the window is never blank. A StateFound restore later applies the saved bounds and
        // rebuilds the surface; SeedConversation is idempotent and Surface.Restore replaces it cleanly.
        host.Window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        host.SeedConversation();

        host.Window.Closing += (_, _) =>
        {
            SaveLayout();
            _bus.Send(new ApplicationShutdown());
        };

        return host;
    }

    private void RecreateSecondaryWindow(PersistedWindow entry)
    {
        var host = NewSecondaryHost(Guid.NewGuid());
        ApplyBounds(host.Window, entry.Bounds);
        RestoreLayout(host, entry);

        // Before the reveal the recreated window stays hidden - Reveal() presents all windows in one
        // entrance. A restore that lands after the reveal (failsafe path) shows it directly.
        if (_revealed)
        {
            ShowWithFade(host.Window);
        }
    }

    private WindowHost NewSecondaryHost(Guid windowId)
    {
        var host = new WindowHost(_bus, _config, _keymap, () => _permissionPending, windowId, isPrimary: false);
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
    // window is never closed this way - its sole panel is locked, so it cannot become empty.
    private void CloseIfEmptySecondary(WindowHost host)
    {
        if (!host.IsPrimary && !host.Surface.PanelIds.Any() && host.SlideInIds.Count == 0)
        {
            CloseSecondaryWindow(host.WindowId);
        }
    }
}
