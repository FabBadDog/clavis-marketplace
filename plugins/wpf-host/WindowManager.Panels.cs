using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// Placing, toggling, closing and retitling panels on the windows' docking surfaces.
internal sealed partial class WindowManager
{
    private void SeedDefaultSlidePlacement()
    {
        foreach (var panel in _config.DefaultSlidePanels)
        {
            if (string.IsNullOrEmpty(panel.Kind))
            {
                continue;
            }

            var edge = string.IsNullOrEmpty(panel.Edge) ? DefaultSlideEdge : panel.Edge;
            _kindPlacement[panel.Kind] = new PanelPlacement(_primaryWindowId, SlideMode, edge);
        }
    }

    private void PlacePanel(PanelInstanceReady ready)
    {
        // Restore path: the slot already exists in the surface (showing its compile-log placeholder while the
        // panel's plugin was still building). Stop the placeholder's log, swap in the real view, and fade it
        // in so the panel resolves out of the placeholder rather than hard-cutting.
        if (_pendingRestorePlacement.Remove(ready.InstanceId, out var windowId)
            && _windows.TryGetValue(windowId, out var owner))
        {
            DisposePlaceholder(ready.InstanceId);
            var view = (FrameworkElement)ready.View.Invoke();
            owner.Surface.ReplacePanelView(ready.InstanceId, view);
            Motion.appear(view);
            return;
        }

        // Slide-in restore path: re-anchor the panel to its saved edge, parked (hidden) until summoned.
        if (_pendingRestoreSlideIn.Remove(ready.InstanceId, out var slide)
            && _windows.TryGetValue(slide.WindowId, out var slideHost))
        {
            slideHost.AddSlideIn(ready.InstanceId, ready.Kind, ready.Title, (FrameworkElement)ready.View.Invoke(), slide.Edge, show: false);
            return;
        }

        // Singleton per kind: if a panel of this kind is already live, reveal it where it sits and drop the
        // duplicate the registry just minted (its view was never built, so there is nothing to dispose).
        var existing = FindLiveInstance(ready.Kind, ready.InstanceId);
        if (existing is not null)
        {
            RevealInstance(existing.Value);
            _bus.Send(new PanelClosed(ready.InstanceId));
            return;
        }

        PlaceFresh(ready, (FrameworkElement)ready.View.Invoke());
        ScheduleSave();
    }

    /// The live instance of a kind, if one is currently placed: a docked tab or an edge slide-in in any
    /// window. Excludes the just-minted instance so an Open does not match itself.
    private LiveInstance? FindLiveInstance(string kind, Guid exclude)
    {
        foreach (var host in _windows.Values)
        {
            foreach (var slot in LayoutTree.EnumerateSlots(host.Surface.Capture()))
            {
                if (slot.PanelKind == kind && slot.PanelId != exclude)
                {
                    return new LiveInstance(host, slot.PanelId, TabMode);
                }
            }
        }

        foreach (var host in _windows.Values)
        {
            foreach (var (instanceId, slideKind) in host.SlideInInstances)
            {
                if (slideKind == kind && instanceId != exclude)
                {
                    return new LiveInstance(host, instanceId, SlideMode);
                }
            }
        }

        return null;
    }

    /// Bring an existing panel back to the user: surface its window, then either slide it in or focus its
    /// tab. Covers "the window is open but not displayed" (activate) and "it was slid in" (re-summon).
    private static void RevealInstance(LiveInstance instance)
    {
        BringToFront(instance.Host.Window);

        if (instance.Mode == SlideMode)
        {
            instance.Host.ShowSlideIn(instance.PanelId);
        }
        else
        {
            instance.Host.Surface.FocusPanel(instance.PanelId);
            instance.Host.FocusSurface();
        }
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }

    /// Open the kind if it has no live instance, or close/dismiss it if it does - the one gesture that both
    /// summons and banishes a panel. A live slide-in toggles its visibility (shown -> hidden, hidden ->
    /// revealed); a live docked tab closes; with no live instance it opens through the normal placement path.
    private void TogglePanel(string kind)
    {
        var live = FindLiveInstance(kind, Guid.Empty);
        if (live is null)
        {
            _bus.Send(new OpenPanel(kind));
            return;
        }

        var instance = live.Value;
        if (instance.Mode == SlideMode)
        {
            if (instance.Host.IsSlideInOpen(instance.PanelId))
            {
                instance.Host.HideSlideIn(instance.PanelId);
            }
            else
            {
                BringToFront(instance.Host.Window);
                instance.Host.ShowSlideIn(instance.PanelId);
            }

            return;
        }

        // The primary window's locked sole panel cannot be toggled away - the main window always keeps it.
        if (instance.Host.IsSolePanelLocked)
        {
            return;
        }

        instance.Host.Surface.RemovePanel(instance.PanelId);
        _bus.Send(new PanelClosed(instance.PanelId));
        ScheduleSave();
    }

    /// Close or dismiss whatever panel the focused window currently surfaces: an open slide-in is hidden
    /// first, otherwise the active docked panel (never the conversation) is closed. Backs the panel-scoped
    /// Esc binding, which resolves only while a closeable panel holds focus.
    private void CloseActivePanel()
    {
        var host = GetFocused() ?? GetPrimary();
        if (host is null)
        {
            return;
        }

        if (host.DismissOpenSlideIns())
        {
            return;
        }

        var closed = host.CloseActiveDockedPanel();
        if (closed != Guid.Empty)
        {
            _bus.Send(new PanelClosed(closed));
            ScheduleSave();
        }
    }

    /// Close a docked panel instance by id, wherever it is docked: remove it from its surface, announce
    /// PanelClosed, and persist. Mirrors the tab-close path so an emptied secondary window is retired via
    /// Surface.PanelRemoved. The primary window's locked sole panel (the conversation) is never removed. A
    /// panel currently anchored as a slide-in is dismissed through the same slide-in close path.
    private void ClosePanel(Guid instanceId)
    {
        foreach (var host in _windows.Values)
        {
            if (host.Surface.PanelIds.Contains(instanceId))
            {
                if (host.IsSolePanelLocked)
                {
                    return;
                }

                host.Surface.RemovePanel(instanceId);
                _bus.Send(new PanelClosed(instanceId));
                ScheduleSave();
                return;
            }

            if (host.HasSlideIn(instanceId))
            {
                CloseSlideInPanel(host, instanceId);
                return;
            }
        }
    }

    /// Dismiss a panel anchored as a slide-in (its handle's close cross, or a ClosePanel for a slide-in): lift
    /// it out, announce PanelClosed so the owner disposes it, and retire the window if it is now empty.
    private void CloseSlideInPanel(WindowHost host, Guid instanceId)
    {
        if (host.TryTakeSlideIn(instanceId) is not null)
        {
            _bus.Send(new PanelClosed(instanceId));
            CloseIfEmptySecondary(host);
            ScheduleSave();
        }
    }

    /// Retitle a docked panel's tab in place. The surface's LayoutChanged (fired by RetitlePanel) schedules
    /// the layout save, so the new title persists.
    private void RetitlePanel(Guid instanceId, string title)
    {
        foreach (var host in _windows.Values)
        {
            if (host.Surface.PanelIds.Contains(instanceId))
            {
                host.Surface.RetitlePanel(instanceId, title);
                return;
            }
        }
    }

    /// First open (no live instance) of a kind: place it in the mode it was last in - a tab in a window, an
    /// edge slide-in, or a fresh standalone window - falling back to a tab in the active window.
    private void PlaceFresh(PanelInstanceReady ready, FrameworkElement view)
    {
        var placement = _kindPlacement.GetValueOrDefault(ready.Kind);

        switch (placement.Mode)
        {
            case SlideMode:
            {
                var host = ResolveWindow(placement.WindowId);
                var edge = string.IsNullOrEmpty(placement.Edge) ? DefaultSlideEdge : placement.Edge;
                host.AddSlideIn(ready.InstanceId, ready.Kind, ready.Title, view, edge);
                _kindPlacement[ready.Kind] = new PanelPlacement(host.WindowId, SlideMode, edge);
                return;
            }

            case WindowMode:
            {
                var host = NewSecondaryHost(Guid.NewGuid());
                host.Surface.AddPanel(ready.InstanceId, ready.Kind, ready.Title, view, DockTarget.IntoActiveGroup);
                ShowWithFade(host.Window);
                _bus.Send(new WindowOpened(host.WindowId, "CLAVIS"));
                _kindPlacement[ready.Kind] = new PanelPlacement(host.WindowId, WindowMode, "");
                return;
            }

            default:
            {
                var host = ResolveWindow(placement.WindowId);
                host.Surface.AddPanel(ready.InstanceId, ready.Kind, ready.Title, view, DockTarget.IntoActiveGroup);
                _kindPlacement[ready.Kind] = new PanelPlacement(host.WindowId, TabMode, "");
                return;
            }
        }
    }
}
