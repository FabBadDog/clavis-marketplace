using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

/// The conversation status bar: three placeholder strips (left / center / right) over the shared engine.
/// Left and right dock to their edges; the center fills and centers. The plugin pushes merged placeholder
/// values via Update on each snapshot.
internal sealed class PlaceholderStatusBar
{
    private readonly PlaceholderStrip _left = new();
    private readonly PlaceholderStrip _center = new();
    private readonly PlaceholderStrip _right = new();

    public PlaceholderStatusBar(string leftTemplate, string centerTemplate, string rightTemplate)
    {
        // The status line never shows raw "{...}" tokens: until a provider plugin publishes a value the
        // segment renders as nothing. The template editor keeps verbatim rendering as authoring feedback.
        _left.HideUnresolvedValues = true;
        _center.HideUnresolvedValues = true;
        _right.HideUnresolvedValues = true;

        _left.SetTemplate(leftTemplate);
        _center.SetTemplate(centerTemplate);
        _right.SetTemplate(rightTemplate);

        var dock = new DockPanel { LastChildFill = true };

        var rightHost = _right.Element;
        DockPanel.SetDock(rightHost, Dock.Right);
        dock.Children.Add(rightHost);

        var leftHost = _left.Element;
        DockPanel.SetDock(leftHost, Dock.Left);
        dock.Children.Add(leftHost);

        dock.Children.Add(new Border
        {
            Child = _center.Element,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        Element = dock;
    }

    public FrameworkElement Element { get; }

    public void SetTemplates(string leftTemplate, string centerTemplate, string rightTemplate)
    {
        _left.SetTemplate(leftTemplate);
        _center.SetTemplate(centerTemplate);
        _right.SetTemplate(rightTemplate);
    }

    public void Update(IReadOnlyDictionary<string, string> values)
    {
        _left.SetValues(values);
        _center.SetValues(values);
        _right.SetValues(values);
    }
}
