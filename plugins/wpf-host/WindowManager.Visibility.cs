using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// Reveal, summon and banish: the gated first entrance and the show/hide transitions after it.
internal sealed partial class WindowManager
{
    private bool InVisibilityTransition =>
        _pendingVisibilityTransitions > 0
        && DateTime.UtcNow - _visibilityTransitionStarted < VisibilityTransitionFailsafe;

    private void BeginVisibilityTransition(int slidingWindows)
    {
        _pendingVisibilityTransitions = slidingWindows;
        _visibilityTransitionStarted = DateTime.UtcNow;
    }

    private void CompleteVisibilityTransition()
    {
        if (_pendingVisibilityTransitions > 0)
        {
            _pendingVisibilityTransitions--;
        }
    }

    // The reveal preconditions: the essential plugins are active and the saved layout has been applied
    // (or determined absent). Configuration is essential, so on a healthy boot both arrive back to back.
    private void RevealWhenReady()
    {
        if (_essentialsReady && _restoredFromConfig)
        {
            Reveal();
        }
    }

    private void StartRevealFailsafe()
    {
        if (_revealed || _revealFailsafe is not null)
        {
            return;
        }

        _revealFailsafe = new DispatcherTimer { Interval = RevealFailsafe };
        _revealFailsafe.Tick += (_, _) =>
        {
            _revealFailsafe?.Stop();
            Reveal();
        };
        _revealFailsafe.Start();
    }

    // The boot's one entrance: every window the restore materialised appears together - secondaries
    // first, the primary last so it ends focused - each already at its restored bounds, falling in from
    // the top of the screen as the host's splash drops out the bottom.
    private void Reveal()
    {
        if (_revealed)
        {
            return;
        }

        _revealed = true;
        _revealFailsafe?.Stop();

        var primary = GetPrimary();
        if (primary is null)
        {
            return;
        }

        foreach (var host in OrderedWindows().Reverse())
        {
            // showWindowFallingIn (not Show()+fallInWindow) parks the window off-screen before its first
            // paint - these windows have never been shown, so a plain Show() would present them at their
            // resting bounds for a frame before the animation had a chance to move them.
            Motion.showWindowFallingIn(host.Window, null);
        }

        // The primary is now shown, so it is a valid owner: link any secondary restored before the reveal,
        // which could not be owned while the primary was still hidden.
        foreach (var host in OrderedWindows())
        {
            if (!host.IsPrimary && host.Window.Owner is null)
            {
                host.Window.Owner = primary.Window;
            }
        }

        ShowBar(primary);

        primary.Window.Activate();
        // Land focus inside the surface rather than on a window-owned input: the prompt belongs to the chat
        // panel now, so "focus the first thing in the active panel" is both correct and chat-agnostic.
        primary.FocusSurface();
        _bus.LogInfo("WpfHost", "primary window shown");

        // Materialise the restored panels now, at the reveal, rather than waiting for BootstrapComplete:
        // PanelRegistry is essential (up by now) and buffers a restore whose owning plugin is still loading,
        // so each panel pops in as its plugin comes up instead of all of them appearing seconds later.
        FlushRestoreSends();
    }

    /// Show the active workspace's secondary windows and hide the others. Only runs while the application is
    /// revealed and not mid-transition: before the reveal every window is deliberately hidden, and hiding or
    /// showing during a slide would capture an animated position as a window's resting place.
    private void ApplyWorkspaceWindowVisibility()
    {
        if (!_revealed || InVisibilityTransition)
        {
            return;
        }

        foreach (var host in _windows.Values.Where(host => !host.IsPrimary))
        {
            var belongs = IsInActiveWorkspace(host);
            if (belongs && !host.Window.IsVisible)
            {
                ShowWithFade(host.Window);
            }
            else if (!belongs && host.Window.IsVisible)
            {
                Motion.fadeWindow(host.Window, 0.0, host.Window.Hide);
            }
        }
    }

    /// Place the bar on the monitor the primary window is on and show it. Never activated, never owned by the
    /// primary: an owned window would hide with its owner, and the bar surviving a banish is the point of it.
    private void ShowBar(WindowHost primary)
    {
        if (_bar is not { } bar)
        {
            return;
        }

        if (WindowSnapBehavior.RectOf(primary.Window) is { } rect)
        {
            var dpi = PresentationSource.FromVisual(primary.Window)?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            bar.PlaceOn(WorkAreaContaining(rect), dpi);
        }

        bar.Window.Show();
        bar.ReassertTopmost();
    }

    // The work area of the monitor the given rectangle sits on, falling back to the primary monitor's when the
    // lookup fails (an unplugged display) so the bar lands somewhere visible rather than nowhere.
    private static ScreenRectangle WorkAreaContaining(ScreenRectangle windowRect)
    {
        foreach (var area in WindowSnapBehavior.WorkAreas())
        {
            var centerX = (windowRect.Left + windowRect.Right) / 2;
            if (centerX >= area.Left && centerX <= area.Right)
            {
                return area;
            }
        }

        return new ScreenRectangle(
            (int)SystemParameters.WorkArea.Left, (int)SystemParameters.WorkArea.Top,
            (int)SystemParameters.WorkArea.Right, (int)SystemParameters.WorkArea.Bottom);
    }

    private void Summon()
    {
        var primary = GetPrimary();
        if (primary is null || !_revealed || InVisibilityTransition)
        {
            return;
        }

        // Secondaries first, the primary last so it ends up activated with keyboard focus. A hidden
        // window falls in from the top; a minimized one just restores (the OS restore already presents
        // it in place); an already-visible one comes forward without replaying the entrance.
        var entering = new List<Window>();
        foreach (var host in OrderedWindows().Reverse())
        {
            var window = host.Window;
            if (window.WindowState == WindowState.Minimized)
            {
                window.WindowState = WindowState.Normal;
                window.Show();
            }
            else if (!window.IsVisible)
            {
                entering.Add(window);
            }
            else
            {
                window.Show();
            }
        }

        BeginVisibilityTransition(entering.Count);
        foreach (var window in entering)
        {
            Motion.showWindowFallingIn(window, CompleteVisibilityTransition);
        }

        primary.Window.Activate();
        primary.Window.Topmost = true;
        primary.Window.Topmost = false;
        // Summon's z-order kick above can momentarily lift the primary over the bar; reclaim the top.
        _bar?.ReassertTopmost();
        primary.FocusSurface();
    }

    /// One gesture both summons and banishes the application: with a Clavis window focused, every window
    /// rises up out of the screen and hides; otherwise they are all summoned to the foreground.
    private void ToggleVisibility()
    {
        if (!_revealed || InVisibilityTransition)
        {
            return;
        }

        if (_windows.Values.Any(host => host.Window.IsActive))
        {
            HideAll();
        }
        else
        {
            Summon();
        }
    }

    private void HideAll()
    {
        if (InVisibilityTransition)
        {
            return;
        }

        var sliding = _windows.Values
            .Select(host => host.Window)
            .Where(window => window.IsVisible && window.WindowState != WindowState.Minimized)
            .ToList();

        BeginVisibilityTransition(sliding.Count);
        foreach (var host in _windows.Values)
        {
            var window = host.Window;
            if (sliding.Contains(window))
            {
                Motion.riseOutWindow(window, () =>
                {
                    window.Hide();
                    CompleteVisibilityTransition();
                });
            }
            else
            {
                window.Hide();
            }
        }
    }

    // Window entrance: a secondary window falls in from the top of the screen, matching the primary window's
    // drop-in. Close still fades out (CloseSecondaryWindow / CloseWithFade).
    private static void ShowWithFade(Window window)
    {
        window.Show();
        Motion.fallInWindow(window);
    }
}
