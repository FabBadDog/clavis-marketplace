using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// One `DockingSurface` per workspace inside one window, created lazily and kept alive once created.
///
/// **N surfaces, not one surface captured and restored.** Swapping a single surface would tear down and rebuild
/// every panel view on each switch: scroll positions lost, `PanelClosed` fired so the registry disposes the
/// instances (git-log timers restart, the chat view is rebuilt) - on a gesture pressed dozens of times an hour.
/// Keeping background panels alive is also the whole point: "workspace 3 is working" can only mean something if
/// workspace 3's panels are still running while you look at workspace 1.
[ExcludeFromCodeCoverage] // WPF visual-tree hosting and animation; the pure rules it serves are tested elsewhere
internal sealed class WorkspaceSurfaces
{
    private readonly Dictionary<Guid, DockingSurface> _byWorkspace = [];
    private readonly Panel _host;

    public WorkspaceSurfaces(Panel host, Guid initialWorkspaceId)
    {
        _host = host;
        ActiveWorkspaceId = initialWorkspaceId;
        Active = Create(initialWorkspaceId);
        Active.Visibility = Visibility.Visible;
    }

    /// The surface currently on screen. `WindowHost.Surface` forwards to this, so every existing call site that
    /// says "the surface" keeps meaning "the one the user is looking at".
    public DockingSurface Active { get; private set; }

    public Guid ActiveWorkspaceId { get; private set; }

    public IReadOnlyCollection<DockingSurface> All => _byWorkspace.Values;

    /// Raised for every surface as it is created, so the window and the manager can wire the same handlers to
    /// each one. The first surface is created in the constructor, before anything can subscribe - callers pick
    /// that one up by iterating `All` right after subscribing.
    public event EventHandler<DockingSurface>? Created;

    public DockingSurface For(Guid workspaceId) =>
        _byWorkspace.TryGetValue(workspaceId, out var existing) ? existing : Create(workspaceId);

    public bool Has(Guid workspaceId) => _byWorkspace.ContainsKey(workspaceId);

    /// Re-key the initial surface onto a real workspace. Until the workspace list arrives the window has one
    /// surface under `Guid.Empty`, and the saved layout has already been restored into it - so the first
    /// activation must *adopt* that surface rather than switch to a fresh empty one, which would show a blank
    /// window and orphan the panels the user just watched load.
    public bool Adopt(Guid workspaceId)
    {
        if (workspaceId == Guid.Empty || _byWorkspace.ContainsKey(workspaceId)
            || !_byWorkspace.Remove(Guid.Empty, out var unassigned))
        {
            return false;
        }

        _byWorkspace[workspaceId] = unassigned;
        ActiveWorkspaceId = workspaceId;
        Active = unassigned;
        return true;
    }

    /// Bring a workspace's surface on screen, creating it on first ask. Returns false when it is already
    /// active, so a repeated activation neither re-animates nor re-materialises anything.
    public bool Activate(Guid workspaceId)
    {
        if (workspaceId == ActiveWorkspaceId && _byWorkspace.ContainsKey(workspaceId))
        {
            return false;
        }

        var incoming = For(workspaceId);
        var outgoing = Active;

        ActiveWorkspaceId = workspaceId;
        Active = incoming;

        if (ReferenceEquals(incoming, outgoing))
        {
            return true;
        }

        // A hard cut is a bug per the design language, so the two surfaces cross-fade. The outgoing one is
        // collapsed rather than left transparent: an invisible-but-present surface still takes hit tests and
        // still offers tab stops.
        incoming.Opacity = 0;
        incoming.Visibility = Visibility.Visible;
        Motion.crossfade(outgoing, incoming);
        outgoing.Visibility = Visibility.Collapsed;
        return true;
    }

    private DockingSurface Create(Guid workspaceId)
    {
        var surface = new DockingSurface { Visibility = Visibility.Collapsed };
        _byWorkspace[workspaceId] = surface;
        _host.Children.Add(surface);
        Created?.Invoke(this, surface);
        return surface;
    }
}
