using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// The single definition of "a control keyboard focus may land on" - shared by Tab traversal and the focus
// ring so they never disagree (you can focus exactly the things that get a ring). Genuine interactive
// controls qualify: text inputs, buttons, and selectors (list/combo). Generic containers - Border, Grid,
// ScrollViewer, ContentControl, TabItem, plain ItemsControl - are excluded even when focusable, so focus is
// never stranded on chrome that has no keyboard action of its own. The one exception is a panel focus root:
// a focusable element tagged with its panel kind (e.g. the events panel's Border, where typing searches and
// the arrows filter). That is a real keyboard surface, so Tab must be able to land on it - otherwise a
// window hosting only such a panel has no stop and cross-window Tab cannot reach it.
[ExcludeFromCodeCoverage] // WPF focusability rules; no decision logic to unit test
internal static class InteractiveStop
{
    public static bool Is(FrameworkElement element) =>
        element.Focusable
        && element.IsEnabled
        && element.IsVisible
        && KeyboardNavigation.GetIsTabStop(element)
        && (element is TextBoxBase or ButtonBase or Selector or PasswordBox or Slider
            || IsPanelFocusRoot(element));

    // A panel's keyboard surface marks itself with a non-empty string Tag naming its kind - the same marker
    // the host uses to resolve panel-scoped bindings against the focused panel.
    private static bool IsPanelFocusRoot(FrameworkElement element) =>
        element.Tag is string tag && tag.Length > 0;
}
