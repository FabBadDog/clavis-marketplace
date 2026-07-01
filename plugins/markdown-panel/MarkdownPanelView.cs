using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.MarkdownPanel;

/// A display-only markdown panel bound to one definition: it renders that definition's placeholder-resolved
/// body, refreshed live by the plugin as placeholder values change. Editing lives in the manager, not here.
/// Animation is off - the body re-renders whenever values tick, and an entrance animation on every render
/// would flicker.
[ExcludeFromCodeCoverage] // WPF construction
internal sealed class MarkdownPanelView
{
    private readonly MarkdownPresenter _presenter = new() { Animate = false };
    private string _rendered = "";

    public MarkdownPanelView()
    {
        var scroll = new ScrollViewer
        {
            Content = _presenter,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12, 10, 12, 10)
        };

        var root = new Grid();
        root.Children.Add(scroll);
        root.SetResourceReference(Panel.BackgroundProperty, "BlackBrush");
        Element = root;
    }

    public FrameworkElement Element { get; }

    /// Show the resolved markdown, skipping the work when it is unchanged (values tick often).
    public void Render(string markdown)
    {
        if (markdown == _rendered)
        {
            return;
        }

        _rendered = markdown;
        _presenter.Markdown = markdown;
    }
}
