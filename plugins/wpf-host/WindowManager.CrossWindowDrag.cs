using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// Dragging a panel between windows, and tearing one off into a window of its own.
internal sealed partial class WindowManager
{
    /// A panel was dropped on targetHost's surface but lives in another window. Lift it from its current
    /// surface (preserving its live view) and adopt it here, so panels drag freely between windows. This is
    /// the OLE path, taken when the target window did register as a drop target (e.g. the primary window).
    private void MovePanelAcrossWindows(WindowHost targetHost, ExternalPanelDrop drop)
    {
        // Prefer a different window's docked panel (the classic cross-window OLE case), then any window's
        // slide-in - including this window's own, which is how a slide-in re-docks into the surface it overlays.
        var source = _windows.Values.FirstOrDefault(window =>
                !ReferenceEquals(window, targetHost) && window.Surface.PanelIds.Contains(drop.PanelId))
            ?? _windows.Values.FirstOrDefault(window => window.HasSlideIn(drop.PanelId));
        if (source is null)
        {
            return;
        }

        var transfer = source.TakePanel(drop.PanelId);
        if (transfer is null)
        {
            return;
        }

        targetHost.Surface.AddExistingPanel(transfer, drop.Target);
        _kindPlacement[transfer.Slot.PanelKind] = new PanelPlacement(targetHost.WindowId, TabMode, "");
        CloseIfEmptySecondary(source);
        ScheduleSave();
    }

    /// A drag off sourceHost ended with no window accepting the OLE drop - the case where the target is an
    /// owned, transparent window the OS never registered as a drop target. Resolve by the cursor's screen
    /// point: drop into another window's surface at the zone under the cursor, leave the panel be if it
    /// landed back over its own window (no zone), or - dropped clear of every window - tear it off into a
    /// brand-new window at that point.
    private void ResolveCrossWindowDrop(WindowHost sourceHost, DragFellThrough fell)
    {
        var target = _windows.Values.FirstOrDefault(window =>
            !ReferenceEquals(window, sourceHost) && IsPointOverSurface(window, fell.ScreenPoint));
        if (target is not null)
        {
            var moved = sourceHost.TakePanel(fell.PanelId);
            if (moved is not null)
            {
                target.Surface.AddExistingPanelAt(moved, fell.ScreenPoint);
                _kindPlacement[moved.Slot.PanelKind] = new PanelPlacement(target.WindowId, TabMode, "");
                CloseIfEmptySecondary(sourceHost);
                ScheduleSave();
            }

            return;
        }

        if (IsPointOverSurface(sourceHost, fell.ScreenPoint))
        {
            return; // dropped back over its own window but not on a dock zone - leave it where it was
        }

        TearOffToNewWindow(sourceHost, fell);
    }

    /// Tear a panel out into a new window positioned at the drop point. Replaces the old "new empty window"
    /// flow: windows now come into being only by dragging a panel clear of every existing window.
    private void TearOffToNewWindow(WindowHost sourceHost, DragFellThrough fell)
    {
        var transfer = sourceHost.TakePanel(fell.PanelId);
        if (transfer is null)
        {
            return;
        }

        var host = NewSecondaryHost(Guid.NewGuid());
        PositionAtCursor(host.Window, sourceHost.Window, fell.ScreenPoint);
        host.Surface.AddExistingPanel(transfer, DockTarget.IntoActiveGroup);
        ShowWithFade(host.Window);
        _bus.Send(new WindowOpened(host.WindowId, "CLAVIS"));
        _kindPlacement[transfer.Slot.PanelKind] = new PanelPlacement(host.WindowId, WindowMode, "");
        CloseIfEmptySecondary(sourceHost);
        ScheduleSave();
    }

    /// Place a torn-off window so its title strip sits under the cursor. The drop point is in physical
    /// pixels (from the OS cursor query); map it to device-independent units using the source window's DPI.
    private static void PositionAtCursor(Window window, Window reference, Point screenPoint)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var source = PresentationSource.FromVisual(reference);
        var point = source is null
            ? screenPoint
            : source.CompositionTarget.TransformFromDevice.Transform(screenPoint);
        window.Left = point.X - 60;
        window.Top = point.Y - 14;
    }

    /// Paint the drop-zone hint on whichever window the cursor is over during a cross-window drag started
    /// on sourceHost. The source window keeps painting its own hint through the working same-window OLE
    /// path, so it is skipped here; every other window is shown the hint when under the cursor, cleared
    /// otherwise.
    private void UpdateCrossWindowHint(WindowHost sourceHost, Point screenPoint)
    {
        foreach (var window in _windows.Values)
        {
            if (ReferenceEquals(window, sourceHost))
            {
                continue;
            }

            if (IsPointOverSurface(window, screenPoint))
            {
                window.Surface.ShowExternalDropHint(screenPoint);
            }
            else
            {
                window.Surface.ClearExternalDropHint();
            }
        }

        // Clear of every window the drop tears the panel off into a new window - preview that outline at the
        // cursor so the gesture's outcome reads, rather than leaving a bare "drop not allowed" cursor.
        var overAnyWindow = _windows.Values.Any(window => IsPointOverWindowBounds(window, screenPoint));
        if (overAnyWindow)
        {
            _tearOffPreview.Hide();
        }
        else
        {
            _tearOffPreview.ShowAt(screenPoint, sourceHost.Window);
        }
    }

    private void ClearCrossWindowHints()
    {
        _tearOffPreview.Hide();
        foreach (var window in _windows.Values)
        {
            window.Surface.ClearExternalDropHint();
        }
    }

    // Whether a screen point falls within a window's whole bounds (chrome included), so a drag over a
    // window's title strip is not mistaken for "clear of every window" and offered as a tear-off.
    private static bool IsPointOverWindowBounds(WindowHost host, Point screenPoint)
    {
        if (host.Window.WindowState == WindowState.Minimized || !host.Window.IsVisible)
        {
            return false;
        }

        try
        {
            var local = host.Window.PointFromScreen(screenPoint);
            return local.X >= 0 && local.Y >= 0
                && local.X <= host.Window.ActualWidth && local.Y <= host.Window.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false; // window not yet sourced (no HWND); treat as not under the cursor
        }
    }

    private static bool IsPointOverSurface(WindowHost host, Point screenPoint)
    {
        if (host.Window.WindowState == WindowState.Minimized || !host.Window.IsVisible)
        {
            return false;
        }

        try
        {
            var local = host.Surface.PointFromScreen(screenPoint);
            return local.X >= 0 && local.Y >= 0
                && local.X <= host.Surface.ActualWidth && local.Y <= host.Surface.ActualHeight;
        }
        catch (InvalidOperationException)
        {
            return false; // the surface has no presentation source (window not shown)
        }
    }
}
