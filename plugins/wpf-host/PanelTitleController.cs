using System;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// Drives the contextual left title from the active panel: the chat panel shows the dynamic branch strip (the
// title-bar-left region presenter the conversation contributes into), and every other docked panel kind
// shows its title text. Switches cross-fade (quick). Mirrors FocusVisualController's focus-driven cadence -
// the active panel changes on focus/tab clicks, which raise the window's GotKeyboardFocus.
[ExcludeFromCodeCoverage] // WPF focus tracking + animation; no decision logic to unit test
internal sealed class PanelTitleController
{
    private const string ChatKind = "conversation";

    private readonly Window _window;
    private readonly DockingSurface _surface;
    private readonly FrameworkElement _branch;
    private readonly TextBlock _title;
    private string _shownKind = ChatKind;

    public PanelTitleController(Window window, DockingSurface surface, FrameworkElement branch, TextBlock title)
    {
        _window = window;
        _surface = surface;
        _branch = branch;
        _title = title;

        // Default to the chat title shown (the primary window opens on the conversation).
        _branch.Opacity = 1;
        _title.Opacity = 0;

        _window.GotKeyboardFocus += (_, _) => Schedule();
        _window.Activated += (_, _) => Schedule();
    }

    private void Schedule() =>
        _window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Update));

    private void Update()
    {
        var kind = _surface.ActivePanelKind;
        if (kind == _shownKind)
        {
            return;
        }
        _shownKind = kind;

        if (kind == ChatKind)
        {
            Motion.crossfade(_title, _branch);
        }
        else if (!string.IsNullOrEmpty(kind))
        {
            _title.Text = _surface.ActivePanelTitle;
            Motion.crossfade(_branch, _title);
        }
        else
        {
            Motion.fadeTo(_branch, 0.0);
            Motion.fadeTo(_title, 0.0);
        }
    }
}
