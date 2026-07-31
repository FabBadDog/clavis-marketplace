using FabioSoft.Clavis.Rendering;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

// These persisted types expose a parameterless constructor and settable properties (alongside a positional
// constructor for code) because YamlDotNet builds objects that way - it has no constructor-based binding.
// The F# layout types they nest (LayoutNode, PanelSlot) are [<CLIMutable>], so they round-trip the same way.

/// What a window is for. A string rather than a bool so a third role can be added without another flag - the
/// workspace bar is coming, and "is it the primary?" cannot express three answers.
public static class WindowRole
{
    public const string Primary = "primary";
    public const string Panel = "panel";
}

/// A window's on-screen rectangle plus whether it was maximised. The single source of truth for window
/// geometry: every persisted window carries one, and geometry is deliberately **not** part of a per-workspace
/// layout - otherwise the primary window's bounds would be duplicated once per workspace and the copies would
/// drift.
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

/// One persisted window: identity, role, on-screen bounds, and which workspace it belongs to. A secondary
/// window belongs to the workspace it was torn off in, so it hides and shows with that workspace; the primary
/// belongs to none (Guid.Empty) because it carries the chrome for all of them.
public sealed record PersistedWindow
{
    public Guid WindowId { get; set; }
    public string Role { get; set; } = WindowRole.Panel;
    public Guid WorkspaceId { get; set; }
    public PersistedWindowState Bounds { get; set; } = new();

    public bool IsPrimary => Role == WindowRole.Primary;

    public PersistedWindow() { }

    public PersistedWindow(Guid windowId, string role, Guid workspaceId, PersistedWindowState bounds) =>
        (WindowId, Role, WorkspaceId, Bounds) = (windowId, role, workspaceId, bounds);
}

/// The docking tree one workspace has inside one window, plus that pair's edge slide-ins. A window holds one
/// of these per workspace it has been used in, which is what lets a workspace keep its own arrangement of
/// panels while the window itself keeps one set of bounds.
public sealed record PersistedWorkspaceLayout
{
    public Guid WindowId { get; set; }
    public Guid WorkspaceId { get; set; }
    public LayoutNode Layout { get; set; } = null!;
    public List<PersistedSlideIn> SlideIns { get; set; } = [];

    public PersistedWorkspaceLayout() { }

    public PersistedWorkspaceLayout(Guid windowId, Guid workspaceId, LayoutNode layout) =>
        (WindowId, WorkspaceId, Layout) = (windowId, workspaceId, layout);
}

/// The whole layout: every open window, every (window, workspace) docking tree, and which workspace was
/// active. ActiveWorkspaceId is persisted **here** rather than read back from the Workspaces plugin, so the
/// layout is self-sufficient and the reveal keeps waiting on exactly the two answers it always did - adding a
/// third precondition to the reveal gate would add a third way for boot to hang.
public sealed record PersistedLayout
{
    public int Version { get; set; }
    public Guid ActiveWorkspaceId { get; set; }
    public List<PersistedWindow> Windows { get; set; } = [];
    public List<PersistedWorkspaceLayout> Layouts { get; set; } = [];

    public PersistedLayout() { }

    public PersistedLayout(
        int version,
        Guid activeWorkspaceId,
        IEnumerable<PersistedWindow> windows,
        IEnumerable<PersistedWorkspaceLayout> layouts) =>
        (Version, ActiveWorkspaceId, Windows, Layouts) =
            (version, activeWorkspaceId, windows.ToList(), layouts.ToList());

    /// The layouts belonging to one workspace, in window order.
    public IEnumerable<PersistedWorkspaceLayout> For(Guid workspaceId) =>
        Layouts.Where(layout => layout.WorkspaceId == workspaceId);

    public PersistedWorkspaceLayout? For(Guid windowId, Guid workspaceId) =>
        Layouts.FirstOrDefault(layout => layout.WindowId == windowId && layout.WorkspaceId == workspaceId);
}

/// The version-1 shape, kept only to read a layout written before workspaces existed. Every v1 window carried
/// its own layout and slide-ins inline, and there was no workspace anywhere.
public sealed record LayoutV1Window
{
    public Guid WindowId { get; set; }
    public bool IsPrimary { get; set; }
    public PersistedWindowState Bounds { get; set; } = new();
    public LayoutNode Layout { get; set; } = null!;
    public List<PersistedSlideIn> SlideIns { get; set; } = [];
}

public sealed record LayoutV1
{
    public int Version { get; set; }
    public List<LayoutV1Window> Windows { get; set; } = [];
}

/// Bringing a version-1 layout forward, and binding a layout with no workspace to a real one. Pure, so both
/// are testable without a window.
public static class LayoutMigration
{
    /// A layout written before workspaces existed belongs to whichever workspace turns out to be active, which
    /// the host does not know while reading the file. It is therefore migrated with **Guid.Empty** as the
    /// workspace - an explicit "unassigned" marker, not an orphan - and `Adopt` binds it once the workspace
    /// list arrives. Treating it as an orphan instead would silently discard everyone's existing layout.
    public static PersistedLayout FromVersion1(LayoutV1 legacy) =>
        new(
            LayoutFile.CurrentVersion,
            Guid.Empty,
            legacy.Windows.Select(window => new PersistedWindow(
                window.WindowId,
                window.IsPrimary ? WindowRole.Primary : WindowRole.Panel,
                Guid.Empty,
                window.Bounds)),
            legacy.Windows.Select(window => new PersistedWorkspaceLayout(
                window.WindowId, Guid.Empty, window.Layout)
            {
                SlideIns = window.SlideIns
            }));

    /// Bind every unassigned (Guid.Empty) workspace reference to a real workspace. The primary window keeps
    /// Guid.Empty on purpose - it belongs to no single workspace - so only secondary windows and layouts are
    /// rebound. A no-op once everything is already assigned, and for an empty workspace id.
    public static PersistedLayout Adopt(PersistedLayout layout, Guid workspaceId)
    {
        if (workspaceId == Guid.Empty)
        {
            return layout;
        }

        return layout with
        {
            ActiveWorkspaceId = layout.ActiveWorkspaceId == Guid.Empty ? workspaceId : layout.ActiveWorkspaceId,
            Windows =
            [
                .. layout.Windows.Select(window =>
                    window.IsPrimary || window.WorkspaceId != Guid.Empty
                        ? window
                        : window with { WorkspaceId = workspaceId })
            ],
            Layouts =
            [
                .. layout.Layouts.Select(entry =>
                    entry.WorkspaceId == Guid.Empty ? entry with { WorkspaceId = workspaceId } : entry)
            ]
        };
    }

    /// Drop layouts (and the secondary windows that only exist for them) whose workspace no longer exists.
    /// Applied on the first workspace list, so a layout left behind by a workspace closed in an earlier
    /// session does not linger. Unassigned entries are left alone - Adopt handles those.
    public static PersistedLayout DropOrphans(PersistedLayout layout, IReadOnlyCollection<Guid> liveWorkspaces)
    {
        bool IsLive(Guid workspaceId) => workspaceId == Guid.Empty || liveWorkspaces.Contains(workspaceId);

        return layout with
        {
            Windows = [.. layout.Windows.Where(window => window.IsPrimary || IsLive(window.WorkspaceId))],
            Layouts = [.. layout.Layouts.Where(entry => IsLive(entry.WorkspaceId))]
        };
    }

    /// Whether a workspace should be seeded with the configured default panels: it has nothing saved that can
    /// actually be restored onto a window that exists.
    ///
    /// The question is deliberately "is there an entry", not "does the entry have panels". An entry with no
    /// panels is a workspace whose chat was closed, and that is taken at its word - seeding it again would make
    /// closing a panel impossible. But an entry naming a window that is gone restores nothing, and until now
    /// that produced a blank surface with no chat and no way to type, which no gesture in the UI could explain
    /// or undo.
    public static bool NeedsDefaultPanels(
        PersistedLayout? layout, Guid workspaceId, IReadOnlyCollection<Guid> liveWindows) =>
        layout is null
        || !layout.For(workspaceId).Any(entry => entry.Layout is not null && liveWindows.Contains(entry.WindowId));
}

/// Serialises the window layout to and from YAML. The text is persisted as this plugin's runtime state
/// (the WpfHost section of state.yaml via the Configuration plugin); WindowManager owns the bus round-trip, so this type
/// is pure (de)serialization.
public static class LayoutFile
{
    public const int CurrentVersion = 2;

    private const int LegacyVersion = 1;

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static string Serialize(PersistedLayout layout) => Serializer.Serialize(layout);

    /// Read a saved layout. Version 1 is migrated forward rather than discarded - it is everyone's existing
    /// layout. Any other version is discarded (treated as "no saved layout") rather than half-materialised,
    /// since deserialization silently tolerates missing fields.
    public static PersistedLayout? Deserialize(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        var version = ReadVersion(yaml);
        if (version == LegacyVersion)
        {
            var legacy = Deserializer.Deserialize<LayoutV1>(yaml);
            return legacy is null ? null : LayoutMigration.FromVersion1(legacy);
        }

        if (version != CurrentVersion)
        {
            return null;
        }

        var layout = Deserializer.Deserialize<PersistedLayout>(yaml);
        return layout is not null && layout.Version == CurrentVersion ? layout : null;
    }

    // The version has to be known before the body can be bound to a shape, so it is read on its own first.
    private static int ReadVersion(string yaml)
    {
        try
        {
            return Deserializer.Deserialize<VersionProbe>(yaml)?.Version ?? 0;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return 0;
        }
    }

    private sealed class VersionProbe
    {
        public int Version { get; set; }
    }
}
