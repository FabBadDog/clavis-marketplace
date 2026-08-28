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
            _kindPlacement[panel.Kind] = new PanelPlacement(_bootstrapWindowId, SlideMode, edge);
        }
    }

    private void PlacePanel(PanelInstanceReady ready)
    {
        // Every panel is recorded the same way, whichever path puts it on screen. Restored panels used to skip
        // this and return below, so after a restart every panel that came back was of no known workspace: the
        // cardinality lookup could not match it, and asking for a kind that was already restored - the chat
        // above all - opened a second one beside it.
        _kindCardinality[ready.Kind] = LivePanels.Normalize(ready.Cardinality);
        _panelWorkspace[ready.InstanceId] = ready.WorkspaceId;
        _panelKind[ready.InstanceId] = ready.Kind;
        if (!ready.IsClosable)
        {
            _unclosableKinds.Add(ready.Kind);
        }

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

        // The kind's declared cardinality decides whether an already-live panel is reused: reveal it where it
        // sits and drop the duplicate the registry just minted (its view was never built, so there is nothing
        // to dispose).
        var existing = FindLiveInstance(ready.Kind, ready.Cardinality, ready.WorkspaceId, ready.InstanceId);
        if (existing is not null)
        {
            // This instance is being dropped, so it must not stay on the books as a live panel.
            _panelWorkspace.Remove(ready.InstanceId);
            _panelKind.Remove(ready.InstanceId);
            RevealInstance(existing.Value);
            _bus.Send(new PanelClosed(ready.InstanceId));
            return;
        }

        PlaceFresh(ready, (FrameworkElement)ready.View.Invoke());
        ScheduleSave();
    }

    /// The live instance of a kind an open should reuse, per the kind's declared cardinality: a docked tab or
    /// an edge slide-in in any window. Excludes the just-minted instance so an Open does not match itself.
    /// This method enumerates what is live (impure); LivePanels.Find owns the rule.
    private LiveInstance? FindLiveInstance(string kind, string cardinality, Guid workspaceId, Guid exclude)
    {
        var candidates = new List<LivePanel>();

        foreach (var host in _windows.Values)
        {
            foreach (var slot in LayoutTree.EnumerateSlots(host.Surface.Capture()))
            {
                if (slot.PanelKind == kind)
                {
                    candidates.Add(new LivePanel(host.WindowId, slot.PanelId, TabMode, WorkspaceOf(slot.PanelId)));
                }
            }
        }

        foreach (var host in _windows.Values)
        {
            foreach (var (instanceId, slideKind) in host.SlideInInstances)
            {
                if (slideKind == kind)
                {
                    candidates.Add(new LivePanel(host.WindowId, instanceId, SlideMode, WorkspaceOf(instanceId)));
                }
            }
        }

        return LivePanels.Find(candidates, cardinality, workspaceId, exclude) is { } found
            && _windows.TryGetValue(found.WindowId, out var owner)
                ? new LiveInstance(owner, found.PanelId, found.Mode)
                : null;
    }

    // Which workspace a live panel belongs to. Unknown (a panel restored from a layout saved before panels
    // carried a workspace) reads as Guid.Empty - the same workspace an un-scoped open resolves to.
    private Guid WorkspaceOf(Guid instanceId) => _panelWorkspace.GetValueOrDefault(instanceId);

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
    ///
    /// For an unclosable kind the gesture only ever summons: it reveals the live instance instead of banishing
    /// it, so the toggle stays useful (it still brings the chat to the front) without being able to remove it.
    private void TogglePanel(string kind, Guid workspaceId)
    {
        var cardinality = _kindCardinality.GetValueOrDefault(kind, PanelCardinality.OnePerWorkspace);
        var live = FindLiveInstance(kind, cardinality, workspaceId, Guid.Empty);
        if (live is null)
        {
            _bus.Send(new OpenPanel(kind, workspaceId));
            return;
        }

        var instance = live.Value;
        if (_unclosableKinds.Contains(kind))
        {
            RevealInstance(instance);
            return;
        }

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

        instance.Host.Surface.RemovePanel(instance.PanelId);
        _bus.Send(new PanelClosed(instance.PanelId));
        ScheduleSave();
    }

    /// Close or dismiss whatever panel the focused window currently surfaces: an open slide-in is hidden
    /// first, otherwise the active docked panel is closed. Backs the panel-scoped Esc binding, which resolves
    /// only for kinds that have no intrinsic Esc behaviour of their own.
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

        // Esc must not be able to remove a kind that declared itself unclosable - it would take the workspace's
        // chat with it and leave a workspace that is not one.
        if (_unclosableKinds.Contains(host.Surface.ActivePanelKind))
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
    /// Surface.PanelRemoved. A panel currently anchored as a slide-in is dismissed through the same slide-in
    /// close path.
    private void ClosePanel(Guid instanceId)
    {
        if (_panelKind.TryGetValue(instanceId, out var kind) && _unclosableKinds.Contains(kind))
        {
            return;
        }

        foreach (var host in _windows.Values)
        {
            if (host.Surface.PanelIds.Contains(instanceId))
            {
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
                var host = ResolveWindow(PlacementWindowFor(ready.WorkspaceId, placement.WindowId));
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
                var host = ResolveWindow(PlacementWindowFor(ready.WorkspaceId, placement.WindowId));
                host.Surface.AddPanel(ready.InstanceId, ready.Kind, ready.Title, view, DockTarget.IntoActiveGroup);
                _kindPlacement[ready.Kind] = new PanelPlacement(host.WindowId, TabMode, "");
                return;
            }
        }
    }

    // Where a fresh panel of this workspace goes, given where its kind was last placed. Enumerates the live
    // windows (impure); PanelPlacements owns the rule.
    private Guid PlacementWindowFor(Guid workspaceId, Guid remembered)
    {
        var candidates = _windows.Values
            .Select(host => new PlaceableWindow(host.WindowId, host.WorkspaceId, host.IsPrimary))
            .ToList();

        var fallback = (GetFocused() ?? GetPrimary())?.WindowId ?? Guid.Empty;
        return PanelPlacements.PlacementWindow(candidates, workspaceId, remembered, fallback);
    }
}
