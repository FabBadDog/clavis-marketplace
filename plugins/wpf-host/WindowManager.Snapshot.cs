using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FabioSoft.Clavis.Rendering;
using FabioSoft.Nucleus.Contracts;

namespace FabioSoft.Nucleus.Plugins.WpfHost;


/// Builds the LayoutSnapshot answer: every open window and live panel, plus what holds focus.
internal sealed partial class WindowManager
{
    private LayoutSnapshot BuildSnapshot()
    {
        var windows = _windows.Values
            .Select(host => new WindowSnapshot(host.WindowId, "CLAVIS", host.IsPrimary, host.WindowId == _focusedWindowId))
            .ToArray();

        var focusedPanelId = GetFocused()?.Surface.ActivePanelId ?? Guid.Empty;
        var panels = new List<PanelSnapshot>();

        foreach (var host in _windows.Values)
        {
            var isFocusedWindow = host.WindowId == _focusedWindowId;
            var activePanelId = host.Surface.ActivePanelId;

            foreach (var (slot, isActiveTab) in LayoutTree.EnumerateSlotsWithVisibility(host.Surface.Capture()))
            {
                panels.Add(new PanelSnapshot(
                    slot.PanelId, slot.PanelKind, slot.Title, host.WindowId,
                    isFocused: isFocusedWindow && slot.PanelId == activePanelId,
                    isVisible: isActiveTab,
                    placement: TabMode));
            }

            foreach (var slide in host.SlideInDetails)
            {
                panels.Add(new PanelSnapshot(
                    slide.InstanceId, slide.Kind, slide.Title, host.WindowId,
                    isFocused: false,
                    isVisible: slide.IsOpen,
                    placement: SlideMode));
            }
        }

        return new LayoutSnapshot([.. windows], [.. panels], _focusedWindowId, focusedPanelId);
    }

    /// Register each configured panel kind as an edge slide-in in the primary window. Seeded before the
    /// saved layout is restored, so a layout that already docks a kind as a tab overrides its default; a
    /// kind absent from the layout (slide-ins are not persisted) keeps the slide-in placement, so opening
    /// it - via its status-bar glyph or the palette - reveals it from the configured edge.
}
