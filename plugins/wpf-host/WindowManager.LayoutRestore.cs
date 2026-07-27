using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// Persistence: reading the saved layout back onto the windows, and capturing it again to save.
internal sealed partial class WindowManager
{
    // Restore the saved layout once, when its state arrives. Other plugins receive StateResult on the
    // same subject, so it is filtered to this plugin's id; StateNotFound (first run, or a deleted state.yaml)
    // leaves the default primary in place. The one-shot guard keeps a later result from rebuilding live
    // windows.
    private void OnStateResult(StateResult result)
    {
        if (_restoredFromConfig)
        {
            return;
        }

        switch (result)
        {
            case StateFound found when found.PluginId == PluginId:
                _restoredFromConfig = true;
                var saved = LayoutFile.Deserialize(found.RawState);
                if (saved is not null)
                {
                    RestoreSavedLayout(saved);
                }

                break;

            case StateNotFound notFound when notFound.PluginId == PluginId:
                _restoredFromConfig = true;
                break;
        }
    }

    private void RestoreSavedLayout(PersistedLayout saved)
    {
        _layoutApplied = true;
        var primary = GetPrimary();
        var primaryEntry = saved.Windows.FirstOrDefault(window => window.IsPrimary);
        if (primary is not null && primaryEntry is not null)
        {
            if (ApplyBounds(primary.Window, primaryEntry.Bounds))
            {
                primary.Window.WindowState = WindowState.Maximized;
            }

            RestoreLayout(primary, primaryEntry);
        }

        foreach (var entry in saved.Windows.Where(window => !window.IsPrimary))
        {
            RecreateSecondaryWindow(entry);
        }

        // If the window is already up (reveal or BootstrapComplete happened before this restore landed -
        // the failsafe/late-config paths), flush the sends this restore just queued; otherwise Reveal or
        // the BootstrapComplete handler will flush them.
        if (_revealed || _bootstrapComplete)
        {
            FlushRestoreSends();
        }
    }

    private void RestoreLayout(WindowHost host, PersistedWindow entry)
    {
        // A layout saved under a since-renamed kind is read through the retirement map first, so an old name
        // does not restore as a slot nothing can resolve.
        var layout = LayoutTree.RenameKinds(entry.Layout, _config.RetiredPanelKinds);

        // Every slot - the chat among them - restores through the same path: a compile-log placeholder now,
        // swapped for the real view when its owning plugin resolves the kind.
        host.Surface.Restore(layout, CreatePlaceholderView);

        foreach (var slot in LayoutTree.EnumerateSlots(layout))
        {
            _panelState[slot.PanelId] = slot.SavedState ?? "";
            _pendingRestorePlacement[slot.PanelId] = host.WindowId;
            _kindPlacement[slot.PanelKind] = new PanelPlacement(host.WindowId, TabMode, "");
            _pendingRestoreSends.Add(
                new RestoreRequest(slot.PanelId, slot.PanelKind, slot.SavedState ?? "", Guid.Empty));
        }

        // Slide-ins are not part of the docking tree, so re-materialise them separately: parked (hidden) on
        // the same edge of this window, and remembered as the kind's placement so opening it later slides it
        // back in from there rather than docking a fresh tab.
        foreach (var slide in entry.SlideIns ?? [])
        {
            var kind = _config.RetiredPanelKinds.GetValueOrDefault(slide.Kind, slide.Kind);
            _panelState[slide.PanelId] = slide.SavedState ?? "";
            _pendingRestoreSlideIn[slide.PanelId] = new SlideInRestore(host.WindowId, kind, slide.Title, slide.Edge);
            _kindPlacement[kind] = new PanelPlacement(host.WindowId, SlideMode, slide.Edge);
            _pendingRestoreSends.Add(
                new RestoreRequest(slide.PanelId, kind, slide.SavedState ?? "", Guid.Empty));
        }
    }

    // Restore requests are deferred until every plugin is up, so the registry has the kinds to resolve them.
    // A launch with no saved layout at all (first run, or a deleted state.yaml) opens the configured default
    // panels instead, so the window is never blank - that configuration is how the host seeds a chat without
    // naming one in code. A saved layout is taken at its word, including an empty one: a chat the user closed
    // stays closed, which is the point of the chat being an ordinary panel.
    private void FlushRestoreSends()
    {
        if (!_layoutApplied && !_defaultsOpened)
        {
            _defaultsOpened = true;
            foreach (var kind in _config.DefaultPanels)
            {
                _bus.Send(new OpenPanel(kind));
            }
        }

        foreach (var request in _pendingRestoreSends)
        {
            _bus.Send(new RestorePanel(request.InstanceId, request.Kind, request.SavedState, request.WorkspaceId));
        }

        _pendingRestoreSends.Clear();
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveLayout()
    {
        _saveTimer.Stop();
        try
        {
            _bus.Send(new SaveState(PluginId, LayoutFile.Serialize(CaptureLayout())));
        }
        catch (Exception exception)
        {
            _bus.LogError("WpfHost", $"Saving the window layout failed: {exception.Message}");
        }
    }

    private PersistedLayout CaptureLayout()
    {
        var windows = _windows.Values.Select(CaptureWindow).ToList();
        return new PersistedLayout(LayoutFile.CurrentVersion, windows);
    }

    private PersistedWindow CaptureWindow(WindowHost host) =>
        new(host.WindowId, host.IsPrimary, BoundsOf(host.Window), LayoutTree.FoldState(host.Surface.Capture(), _panelState))
        {
            SlideIns =
            [
                .. host.SlideInLayouts.Select(slide =>
                    new PersistedSlideIn(slide.InstanceId, slide.Kind, slide.Title, slide.Edge,
                        _panelState.GetValueOrDefault(slide.InstanceId, "")))
            ]
        };

    private static bool ApplyBounds(Window window, PersistedWindowState bounds)
    {
        if (IsOnScreen(bounds))
        {
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            return bounds.IsMaximized;
        }

        if (bounds.IsMaximized)
        {
            return true;
        }

        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return false;
    }

    // A window is "on screen" when its centre falls within the virtual desktop, so a layout saved on a
    // monitor that is now unplugged falls back to centre-screen instead of opening off-screen. This reads
    // the live desktop bounds; the arithmetic itself lives in LayoutTree, where it is testable.
    private static bool IsOnScreen(PersistedWindowState state) =>
        LayoutTree.IsCenterWithin(
            state,
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

    private static PersistedWindowState BoundsOf(Window window)
    {
        var isMaximized = window.WindowState == WindowState.Maximized;
        var bounds = isMaximized
            ? window.RestoreBounds
            : new Rect(window.Left, window.Top, window.Width, window.Height);
        return new PersistedWindowState(bounds.X, bounds.Y, bounds.Width, bounds.Height, isMaximized);
    }
}
