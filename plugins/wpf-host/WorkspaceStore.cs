using FabioSoft.Clavis.Rendering;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// These persisted types expose a parameterless constructor and settable properties (alongside a positional
// constructor for code) because YamlDotNet builds objects that way - it has no constructor-based binding.
// The F# layout types they nest (LayoutNode, PanelSlot) are [<CLIMutable>], so they round-trip the same way.

/// A window's on-screen rectangle plus whether it was maximised. The single source of truth for window
/// geometry: every persisted window carries one (PersistedWindow.Bounds); there is no separate per-window
/// state file.
public sealed record PersistedWindowState
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }

    public PersistedWindowState() { }

    public PersistedWindowState(double left, double top, double width, double height, bool isMaximized) =>
        (Left, Top, Width, Height, IsMaximized) = (left, top, width, height, isMaximized);
}

/// One edge slide-in that was anchored to a window: which panel, on which edge, and its saved state blob.
/// Restored parked (hidden) on the same edge of the same window, so a slide-in returns as a slide-in.
public sealed record PersistedSlideIn
{
    public Guid PanelId { get; set; }
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string Edge { get; set; } = "";
    public string SavedState { get; set; } = "";

    public PersistedSlideIn() { }

    public PersistedSlideIn(Guid panelId, string kind, string title, string edge, string savedState) =>
        (PanelId, Kind, Title, Edge, SavedState) = (panelId, kind, title, edge, savedState);
}

/// One persisted window: its identity, role, on-screen bounds, the docking layout tree (which panels live
/// where, plus each panel's saved state blob folded into the slots), and any edge slide-ins it carried.
public sealed record PersistedWindow
{
    public Guid WindowId { get; set; }
    public bool IsPrimary { get; set; }
    public PersistedWindowState Bounds { get; set; } = new();
    public LayoutNode Layout { get; set; } = null!;
    public List<PersistedSlideIn> SlideIns { get; set; } = [];

    public PersistedWindow() { }

    public PersistedWindow(Guid windowId, bool isPrimary, PersistedWindowState bounds, LayoutNode layout) =>
        (WindowId, IsPrimary, Bounds, Layout) = (windowId, isPrimary, bounds, layout);
}

/// The whole workspace: every open window and its layout, persisted across restarts.
public sealed record WorkspaceLayout
{
    public int Version { get; set; }
    public List<PersistedWindow> Windows { get; set; } = [];

    public WorkspaceLayout() { }

    public WorkspaceLayout(int version, IEnumerable<PersistedWindow> windows) =>
        (Version, Windows) = (version, windows.ToList());
}

/// Serialises the workspace layout to and from YAML. The text is persisted as this plugin's configuration
/// (config/WpfHost.yaml via the Configuration plugin); WindowManager owns the bus round-trip, so this type
/// is pure (de)serialization.
public static class WorkspaceStore
{
    public const int CurrentVersion = 1;

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string Serialize(WorkspaceLayout layout) => Serializer.Serialize(layout);

    public static WorkspaceLayout? Deserialize(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        var layout = Deserializer.Deserialize<WorkspaceLayout>(yaml);

        // A layout from an incompatible schema version is discarded (treated as "no saved layout") rather
        // than half-materialised: deserialization silently tolerates missing/unknown fields, so a future
        // version bump must explicitly invalidate old configs instead of loading a partial layout.
        return layout is not null && layout.Version == CurrentVersion ? layout : null;
    }
}
