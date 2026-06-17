using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// Per-window glue between keyboard focus and the FocusOverlay. It watches where keyboard focus lands in
// this window and paints the Clavis focus visuals: a ring (or an underline for the chat input) around the
// focused control, and corner brackets around the panel that holds it. Only the active window paints; an
// inactive window clears its overlay, so exactly one focus is ever shown.
[ExcludeFromCodeCoverage] // WPF focus tracking and overlay positioning; no decision logic to unit test
internal sealed class FocusVisualController
{
    private readonly Window _window;
    private readonly FocusOverlay _overlay = new();
    private readonly DockingSurface _surface;
    private readonly TextBox? _chatInput;

    private FrameworkElement? _tracked;
    private Rect _lastControl = Rect.Empty;
    private Rect _lastPanel = Rect.Empty;

    public FocusVisualController(Window window, Panel overlayRoot, DockingSurface surface, TextBox? chatInput)
    {
        _window = window;
        _surface = surface;
        _chatInput = chatInput;

        // Added last so it layers above the chrome, help overlay, and slide-ins.
        overlayRoot.Children.Add(_overlay);

        _window.GotKeyboardFocus += (_, _) => ScheduleRender();
        _window.LostKeyboardFocus += (_, _) => ScheduleRender();
        _window.Activated += (_, _) => ScheduleRender();
        _window.Deactivated += (_, _) => Clear();
        _window.SizeChanged += (_, _) => ScheduleRender();
    }

    // Defer to an Input-priority tick so focus and layout have settled before we measure bounds.
    private void ScheduleRender() => _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Render));

    private void Render()
    {
        if (!_window.IsActive
            || Keyboard.FocusedElement is not FrameworkElement focused
            || !_window.IsAncestorOf(focused)
            || !InteractiveStop.Is(focused))
        {
            Clear();
            return;
        }

        var controlRect = BoundsIn(focused);
        if (controlRect.Width <= 0 || controlRect.Height <= 0)
        {
            Clear();
            return;
        }

        var focusChanged = !ReferenceEquals(_tracked, focused);
        Track(focused);

        var panel = BracketTarget(focused);
        var panelRect = panel is null ? Rect.Empty : BoundsIn(panel);

        if (!focusChanged && controlRect == _lastControl && panelRect == _lastPanel)
        {
            return; // nothing moved since the last paint
        }

        _lastControl = controlRect;
        _lastPanel = panelRect;

        if (ReferenceEquals(focused, _chatInput) || focused is TextBox or ComboBox or Button)
        {
            // The chat input recolours its framing lines, and the shared text inputs / combos / buttons swap
            // their own gray border for the clavis accent on focus (InputTextBox / InputComboBox / ActionButton).
            // For those the ring would be a second outline outside the border (a double border), so the control
            // owns its cue and the ring stays off.
            _overlay.HideControl();
        }
        else
        {
            _overlay.ShowControl(controlRect);
        }

        if (panel is null)
        {
            _overlay.HidePanel();
        }
        else
        {
            _overlay.ShowPanelBrackets(panelRect);
        }
    }

    private void Clear()
    {
        Track(null);
        _lastControl = Rect.Empty;
        _lastPanel = Rect.Empty;
        _overlay.Clear();
    }

    private Rect BoundsIn(FrameworkElement element) =>
        element.TransformToVisual(_overlay).TransformBounds(new Rect(element.RenderSize));

    // The panel to frame with corner brackets: an open slide-in (it floats over the surface) always, or the
    // docking surface's active group only when the surface is split into several tiles. A single full-window
    // panel is already marked by the window's active border, so brackets there would only add noise. Null
    // when focus is in chrome that is not a panel (e.g. a title-bar button).
    private FrameworkElement? BracketTarget(DependencyObject focused)
    {
        for (DependencyObject? node = focused; node is not null; node = ElementTree.ParentOf(node))
        {
            if (node is SlideInHost { IsOpen: true } slide)
            {
                return slide;
            }

            if (ReferenceEquals(node, _surface))
            {
                return _surface.IsSplit ? _surface.ActivePanelContainer : null;
            }
        }

        return null;
    }

    // Follow the focused element while it moves (its panel scrolls, the window resizes). Bounded to the one
    // tracked element rather than a global LayoutUpdated; Render short-circuits when nothing actually moved.
    private void Track(FrameworkElement? element)
    {
        if (ReferenceEquals(_tracked, element))
        {
            return;
        }

        if (_tracked is not null)
        {
            _tracked.LayoutUpdated -= OnTrackedLayoutUpdated;
        }

        _tracked = element;

        if (_tracked is not null)
        {
            _tracked.LayoutUpdated += OnTrackedLayoutUpdated;
        }
    }

    private void OnTrackedLayoutUpdated(object? sender, EventArgs e) => Render();
}
