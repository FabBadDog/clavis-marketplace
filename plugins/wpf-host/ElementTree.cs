using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// One safe upward step in the element tree, used by every "walk up from the focused/clicked element to its
// interactive ancestor" loop in this plugin. A hit-test or Keyboard.FocusedElement can yield a content
// element - a Run inside rendered markdown, a Hyperlink - which is NOT a Visual, so VisualTreeHelper.GetParent
// throws on it (it does not return null). Those walk the logical tree, which bridges back up to the hosting
// Visual (the TextBlock the Run sits in). Visuals walk the visual tree so template internals are crossed,
// falling back to the logical parent when the visual chain ends but the logical one continues (e.g. a popup).
internal static class ElementTree
{
    public static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or Visual3D
            ? VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
}
