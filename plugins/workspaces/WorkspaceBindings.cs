using System.Collections.Generic;
using System.Linq;

namespace FabioSoft.Nucleus.Plugins.Workspaces;

/// The default gestures for this plugin's own commands, declared to the keymap rather than hardcoded there.
///
/// **Application scope, never system.** A system-scope binding registers an OS global hotkey on the primary
/// window, which would take F1-F12 away from every application on the machine - F1 is help almost everywhere
/// and F12 is devtools. `GlobalHotkey.TryVirtualKey` maps F1-F24, so declaring these as system would silently
/// "work" and be a disaster. Keyboard switching therefore needs a focused Clavis window; the bar's click covers
/// the case where everything is hidden.
public static class WorkspaceBindings
{
    /// F1-F11 activate-or-create the workspace in that slot; F12 toggles the overview. Eleven is the cap by
    /// construction - there is no second bank on Shift, and workspace 12+ is reachable by click or the overview.
    public static IReadOnlyList<KeyBinding> Defaults { get; } =
    [
        .. Enumerable.Range(1, WorkspaceSet.SlotCount)
            .Select(slot => new KeyBinding($"F{slot}", $"ActivateWorkspaceSlot {slot}", KeymapScope.Application, "")),
        new KeyBinding("F12", "TogglePanel workspace-overview", KeymapScope.Application, "")
    ];
}
