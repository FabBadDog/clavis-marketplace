namespace FabioSoft.Nucleus.Plugins.WpfHost;

public sealed record WpfHostConfig(
    double UiScaleFactor = 1.6,
    double DefaultWidth = 740,
    double DefaultHeight = 640,
    double MinWidth = 400,
    double MinHeight = 260)
{
    /// Panel kinds to register as edge slide-ins by default, so opening one (e.g. via its status-bar glyph
    /// or the command palette) reveals it as a slide-in rather than a docked tab. Any number of panels can
    /// be defaulted, each to its own edge. A saved layout that already docks a kind as a tab overrides its
    /// default placement (slide-ins themselves are not persisted, so an undocked kind keeps this default).
    public IReadOnlyList<DefaultSlidePanel> DefaultSlidePanels { get; init; } =
    [
        new DefaultSlidePanel("usage-limits", "right"),
        new DefaultSlidePanel("git-log", "left"),
        new DefaultSlidePanel("keymap", "bottom"),
        // markdown is intentionally absent: a note you edit persists better as a docked tab (the default
        // placement for any kind not listed here).
    ];
}

/// A panel kind to surface as an edge-anchored slide-in by default, paired with the edge it anchors to
/// ("left", "right", "top", "bottom").
public sealed record DefaultSlidePanel(string Kind, string Edge);
