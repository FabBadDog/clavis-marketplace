using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// Keyboard focus traversal for this window: it intercepts Tab / Shift+Tab, moves within the window using
// WPF's own tab order, and hands off to the FocusTraversal coordinator at the window boundary. Kept in its
// own partial so WindowHost.cs stays focused on lifecycle, regions, and slide-ins.
[ExcludeFromCodeCoverage] // WPF visual-tree walking and keyboard focus; no decision logic to unit test
internal sealed partial class WindowHost
{
    private bool TryHandleTab(Key key, KeyEventArgs e)
    {
        if (key != Key.Tab)
        {
            return false;
        }

        var modifiers = Keyboard.Modifiers;
        if (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift)
        {
            return false; // Ctrl+Tab and friends are not focus traversal
        }

        var forward = modifiers != ModifierKeys.Shift;

        // Tab is trapped inside an open slide-in: while focus sits in one, cycle its own controls instead of
        // escaping to a docked panel or another window. Escaping moves focus off the slide-in, which dismisses
        // it - so tabbing field-to-field through a slide-in form (e.g. the status-line settings) used to slide
        // it away mid-edit.
        if (TryTrapTabInSlideIn(forward))
        {
            e.Handled = true;
            return true;
        }

        FocusTraversal?.Traverse(this, forward: forward);
        e.Handled = true;
        return true;
    }

    // When keyboard focus is inside an open slide-in, move it to the next/previous interactive control within
    // that same slide-in, wrapping at its ends. Returns false when focus is not in an open slide-in, so the
    // normal within-window / cross-window traversal runs.
    private bool TryTrapTabInSlideIn(bool forward)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return false;
        }

        var slide = _slideHosts.Values.FirstOrDefault(host => host.IsOpen && host.IsAncestorOf(focused));
        if (slide is null)
        {
            return false;
        }

        var stops = new List<FrameworkElement>();
        Collect(slide, stops);
        if (stops.Count == 0)
        {
            return false;
        }

        var current = IndexOfFocused(stops);
        var next = current < 0
            ? (forward ? 0 : stops.Count - 1)
            : ((current + (forward ? 1 : -1)) % stops.Count + stops.Count) % stops.Count;
        Keyboard.Focus(stops[next]);
        return true;
    }

    /// Move keyboard focus to the next/previous interactive control inside this window, in tab order.
    /// Returns false at the window boundary - past the last stop going forward, past the first going back -
    /// so the coordinator can cross into another window instead of wrapping in place. Owning the ordering
    /// (rather than WPF's MoveFocus) makes the boundary deterministic: MoveFocus cycles within a top-level
    /// scope, so it never reports a boundary to hand off from.
    public bool TryMoveFocusWithin(bool forward)
    {
        var stops = OrderedTabStops();
        if (stops.Count == 0)
        {
            return false;
        }

        var current = IndexOfFocused(stops);
        if (current < 0)
        {
            // Focus is on the bare window body, not a known stop: enter at the leading edge.
            Keyboard.Focus(stops[forward ? 0 : stops.Count - 1]);
            return true;
        }

        var next = FocusRing.Advance(stops.Count, current, forward);
        if (next is null)
        {
            return false; // at the boundary - let the coordinator cross to the next window
        }

        Keyboard.Focus(stops[next.Value]);
        return true;
    }

    /// Focus this window's first (forward) or last (backward) interactive control. Used when traversal wraps
    /// because this is the only window that can hold focus; the window is already active.
    public bool WrapFocus(bool forward)
    {
        var stops = OrderedTabStops();
        if (stops.Count == 0)
        {
            return false;
        }

        Keyboard.Focus(stops[forward ? 0 : stops.Count - 1]);
        return true;
    }

    /// True if this window has any control Tab can land on. A cheap structural probe with no focus
    /// movement, so the coordinator can skip windows - and panels - with nothing interactive.
    public bool HasFocusableStops => OrderedTabStops().Count > 0;

    /// Bring this window forward and place keyboard focus on its first/last interactive control - the
    /// cross-window landing. Focus is deferred to an Input-priority tick after Activate so it lands once
    /// activation has settled; a bare Activate()+Focus() loses the race to WPF restoring the previously
    /// focused element.
    public void EnterFromAdjacentWindow(bool forward)
    {
        Window.Activate();
        Window.Dispatcher.BeginInvoke(
            DispatcherPriority.Input,
            new Action(() =>
            {
                var stops = OrderedTabStops();
                if (stops.Count > 0)
                {
                    Keyboard.Focus(stops[forward ? 0 : stops.Count - 1]);
                }
            }));
    }

    /// This window's interactive tab stops, in visual-tree (and so default tab) order. One walk answers
    /// "where is focus now", "what is the next stop", and "does this window have any stop at all".
    private List<FrameworkElement> OrderedTabStops()
    {
        var stops = new List<FrameworkElement>();
        Collect(Window.Content as DependencyObject, stops);
        return stops;
    }

    private static void Collect(DependencyObject? node, List<FrameworkElement> stops)
    {
        if (node is null)
        {
            return;
        }

        // A parked slide-in is disabled (IsEnabled=false) and translated off-screen; its controls must not
        // count as reachable stops, so the whole subtree is skipped.
        if (node is SlideInHost { IsOpen: false })
        {
            return;
        }

        if (node is FrameworkElement element && InteractiveStop.Is(element))
        {
            stops.Add(element);
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Collect(VisualTreeHelper.GetChild(node, i), stops);
        }
    }

    private static int IndexOfFocused(List<FrameworkElement> stops)
    {
        if (Keyboard.FocusedElement is not DependencyObject focused)
        {
            return -1;
        }

        for (var i = 0; i < stops.Count; i++)
        {
            if (ReferenceEquals(stops[i], focused))
            {
                return i;
            }
        }

        // Focus may sit on a template child of a stop (e.g. inside a ComboBox); match by containment.
        if (focused is Visual)
        {
            for (var i = 0; i < stops.Count; i++)
            {
                if (stops[i].IsAncestorOf(focused))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    // The shared FrameBrush (#4A4A52) when the window is not focused; a brighter cool tone when it is.
    private static readonly Color InactiveBorderColor = Color.FromRgb(0x4A, 0x4A, 0x52);
    private static readonly Color ActiveBorderColor = Color.FromRgb(0x5E, 0x7E, 0x8E);

    // The chat input's framing lines: FrameBrush grey at rest, clavis (#9FD5F0) while it holds focus.
    private static readonly Color ActiveLineColor = Color.FromRgb(0x9F, 0xD5, 0xF0);

    // The active window brightens its 1px border and its title dot; an inactive window dims both, so the
    // focused window reads at a glance. A per-window mutable brush replaces the shared (frozen) FrameBrush
    // so its colour can animate.
    private void SetupWindowActiveVisuals()
    {
        _borderBrush = new SolidColorBrush(InactiveBorderColor);
        Window.BorderBrush = _borderBrush;
        Window.Activated += (_, _) => ApplyWindowActive(true);
        Window.Deactivated += (_, _) => ApplyWindowActive(false);
    }

    // Brighten the border and the title dot while active; dim both when not. This marks the focused window
    // across a multi-window workspace without any added chrome.
    private void ApplyWindowActive(bool active)
    {
        if (_borderBrush is not null)
        {
            _borderBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(
                    active ? ActiveBorderColor : InactiveBorderColor,
                    new Duration(TimeSpan.FromMilliseconds(220))));
        }

        if (_statusDot is null)
        {
            return;
        }

        // The dot is steady - bright when focused, dim when not. Pulsing is reserved for activity (work in
        // progress), so a merely-focused window must never breathe.
        _statusDot.BeginAnimation(UIElement.OpacityProperty, null);
        _statusDot.Opacity = active ? 0.9 : 0.3;
    }

    // The chat input's focus cue: its top framing line turns clavis while the input holds keyboard focus and
    // fades back to frame grey when it does not - instead of a focus ring. The status bar is now a separate
    // window-chrome row, so it keeps its own static frame line and is no longer part of this cue. Mutable
    // brush so the colour can animate.
    private void WireInputFocusLines(TextBox input, Border inputRow)
    {
        var topLine = new SolidColorBrush(InactiveBorderColor);
        inputRow.BorderBrush = topLine;

        void Recolor(bool focused)
        {
            var target = focused ? ActiveLineColor : InactiveBorderColor;
            var duration = new Duration(TimeSpan.FromMilliseconds(160));
            topLine.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, duration));
        }

        input.GotKeyboardFocus += (_, _) => Recolor(true);
        input.LostKeyboardFocus += (_, _) => Recolor(false);
    }

    // Let the input grow with its content but never past 60% of the chat output's height (the conversation
    // panel area); beyond that it scrolls internally. Re-applied as the area resizes.
    private static void CapInputHeightToChat(TextBox input, FrameworkElement chatArea)
    {
        void Apply() => input.MaxHeight = Math.Max(0.0, chatArea.ActualHeight * 0.6);

        chatArea.SizeChanged += (_, _) => Apply();
        chatArea.Loaded += (_, _) => Apply();
    }

    // A click on a bare button fires its action but must not move the focus ring. We let the click focus the
    // button (so it activates normally), then restore focus to where it was on the next tick. Clicking a
    // "more than a button" control (text box, combo, list) is left alone so it takes focus as usual.
    private void OnButtonClickPreserveFocus(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject hit || Keyboard.FocusedElement is not { } prior)
        {
            return;
        }

        // Preserving focus is a cosmetic nicety layered on top of the click; an unexpected element shape
        // in the upward walk must never take the whole window down, so it is contained and logged.
        try
        {
            if (FocusKeepingButton(hit) is not { } button)
            {
                return;
            }

            // Restore focus only AFTER the click has fired - never on a mid-press dispatcher tick. Scheduling
            // the restore on mouse-down (the old behaviour) let it run between this button's mouse-down and
            // mouse-up; moving focus off a half-pressed button makes WPF abandon the press, so the Click
            // intermittently never fires and the button's action (e.g. Save) silently does nothing. Hooking
            // the button's own Click runs the restore strictly after the action, so every click lands.
            RoutedEventHandler? restore = null;
            restore = (_, _) =>
            {
                button.Click -= restore;
                Window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Keyboard.Focus(prior)));
            };
            button.Click += restore;
        }
        catch (Exception exception)
        {
            _bus.LogError("WpfHost", $"Focus preservation on click failed: {exception.Message}");
        }
    }

    // Walk up from the clicked element: the first interactive ancestor decides. A focusable button keeps
    // focus put (returned so its Click can drive the restore); a text/selection control takes focus normally.
    private static ButtonBase? FocusKeepingButton(DependencyObject hit)
    {
        for (DependencyObject? node = hit; node is not null; node = ElementTree.ParentOf(node))
        {
            switch (node)
            {
                case TextBoxBase:
                case PasswordBox:
                case Selector: // ListBox, ComboBox, TabControl, ...
                    return null;
                case ButtonBase { Focusable: true } button:
                    return button;
            }
        }

        return null;
    }
}
