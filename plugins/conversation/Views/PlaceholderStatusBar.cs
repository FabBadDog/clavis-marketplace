using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.Conversation.Views;

/// The conversation status bar: three placeholder strips (left / center / right) hosted in a
/// ResponsiveZoneBar that keeps them from overlapping as the window narrows - shrinking bars, dropping
/// chrome, scaling the text, and finally dropping the center then the left zone. The plugin pushes merged
/// placeholder values via Update on each snapshot, and the latest usage limit windows via SetLimitWindows so
/// a configured {limitPlane} draws its dots. The same control backs the status-line editor preview (with
/// hideUnresolved off) so the preview behaves exactly like the live bar.
internal sealed class PlaceholderStatusBar
{
    private readonly PlaceholderStrip _left = new();
    private readonly PlaceholderStrip _center = new();
    private readonly PlaceholderStrip _right = new();

    public PlaceholderStatusBar(
        string leftTemplate, string centerTemplate, string rightTemplate, bool hideUnresolved = true)
    {
        // The live status line never shows raw "{...}" tokens: until a provider plugin publishes a value the
        // segment renders as nothing. The editor preview passes hideUnresolved=false so authoring feedback
        // keeps the verbatim tokens visible.
        _left.HideUnresolvedValues = hideUnresolved;
        _center.HideUnresolvedValues = hideUnresolved;
        _right.HideUnresolvedValues = hideUnresolved;

        _left.SetTemplate(leftTemplate);
        _center.SetTemplate(centerTemplate);
        _right.SetTemplate(rightTemplate);

        Element = new ResponsiveZoneBar(_left, _center, _right);
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

    public void SetLimitWindows(IEnumerable<LimitWindow> windows)
    {
        // Each strip remembers the windows and re-applies them whenever it re-renders a {limitPlane}, so the
        // dots survive value/template changes; materialise once so all three see the same set.
        var snapshot = new List<LimitWindow>(windows);
        _left.SetLimitWindows(snapshot);
        _center.SetLimitWindows(snapshot);
        _right.SetLimitWindows(snapshot);
    }

    // Wire the click action for a {limitPlane} wherever it renders across the three zones (it sits in the
    // right zone by default, but the template is user-editable).
    public void SetLimitPlaneClick(Action handler)
    {
        _left.SetLimitPlaneClick(handler);
        _center.SetLimitPlaneClick(handler);
        _right.SetLimitPlaneClick(handler);
    }
}
