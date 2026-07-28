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

    /// Panel kinds to open on a launch with no saved layout, so the window is never blank on a first run.
    /// The host names no panel kind of its own - which kinds make a sensible empty state is marketplace
    /// policy, carried here as configuration. A saved layout always wins over this.
    public IReadOnlyList<string> DefaultPanels { get; init; } = ["chat"];

    /// Panel kinds that were renamed, mapped to what they are called now, so a layout saved under the old
    /// name still restores. Without this a retired kind comes back as a slot nothing can resolve - a tab stuck
    /// on its compile placeholder forever. Which kinds were renamed is marketplace history, so it lives here
    /// as configuration rather than in the host's code.
    public IReadOnlyDictionary<string, string> RetiredPanelKinds { get; init; } =
        new Dictionary<string, string> { ["conversation"] = "chat" };

    /// Whether to show the workspace bar - the chromeless strip across the top of the screen.
    public bool ShowWorkspaceBar { get; init; } = true;

    /// The bar's height in device-independent pixels. A maximized window's work area is reduced by this, so a
    /// maximized Clavis window does not end up sitting underneath the always-on-top strip.
    public double WorkspaceBarHeight { get; init; } = 30;

    /// Reserve desktop space via SHAppBarMessage instead of relying on Topmost. Off, and a designed seam rather
    /// than a feature: a leaked AppBar registration survives a crash and permanently steals desktop space until
    /// logoff - and this application deliberately crashes on a non-viable startup (StartupViability).
    public bool ReserveScreenSpace { get; init; }
}

/// A panel kind to surface as an edge-anchored slide-in by default, paired with the edge it anchors to
/// ("left", "right", "top", "bottom").
public sealed record DefaultSlidePanel(string Kind, string Edge);
