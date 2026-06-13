using FabioSoft.Clavis.Rendering;

namespace FabioSoft.Nucleus.Plugins.WpfHost;

/// Holds the latest keymap snapshot (and command descriptions) the host resolves gestures against. A
/// single instance is shared by every window so they all honour the same bindings. Resolution is
/// most-specific-wins: a focused panel's binding shadows an application binding, which shadows a system
/// binding. The host resolves synchronously in PreviewKeyDown so it can swallow the key event.
internal sealed class KeymapInput
{
    private const string PanelKind = "panel";

    private volatile IReadOnlyList<KeyBinding> _bindings = [];
    private volatile IReadOnlyDictionary<string, string> _descriptions = new Dictionary<string, string>();
    private volatile IReadOnlySet<string> _panelLocalCommands = new HashSet<string>();

    public void Update(IReadOnlyList<KeyBinding> bindings) => _bindings = bindings;

    public void UpdateCommands(IReadOnlyList<CommandDescriptor> commands)
    {
        var descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var panelLocal = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            descriptions[command.Name] = string.IsNullOrWhiteSpace(command.Description)
                ? command.DisplayName
                : command.Description;

            if (string.Equals(command.Kind, PanelKind, StringComparison.OrdinalIgnoreCase))
            {
                panelLocal.Add(command.Name);
            }
        }

        _descriptions = descriptions;
        _panelLocalCommands = panelLocal;
    }

    /// True when a command is a panel-local command (one a panel instance executes itself, e.g. the events
    /// filters), as opposed to a general command routed through the palette. A panel-scoped binding can
    /// carry either: the events filters are panel-local, but CloseActivePanel is a general host command
    /// that merely happens to be bound only while a panel is focused.
    public bool IsPanelLocalCommand(string command) => _panelLocalCommands.Contains(command);

    /// The binding a gesture resolves to in the given focused-panel context, or null if unbound. Panel
    /// scope (for the focused kind) wins over application, which wins over system; first match per scope.
    public KeyBinding? Resolve(string gesture, string focusedPanelKind)
    {
        var bindings = _bindings;

        if (!string.IsNullOrEmpty(focusedPanelKind))
        {
            var panel = bindings.FirstOrDefault(binding =>
                binding.Scope == KeymapScope.Panel
                && string.Equals(binding.PanelKind, focusedPanelKind, StringComparison.OrdinalIgnoreCase)
                && string.Equals(binding.Gesture, gesture, StringComparison.Ordinal));
            if (panel is not null)
            {
                return panel;
            }
        }

        return Match(bindings, KeymapScope.Application, gesture) ?? Match(bindings, KeymapScope.System, gesture);
    }

    /// The distinct system-scope gestures, for the host to register as OS global hotkeys.
    public IReadOnlyList<string> SystemGestures() =>
        [.. _bindings.Where(binding => binding.Scope == KeymapScope.System).Select(binding => binding.Gesture).Distinct()];

    /// The help-overlay rows for a window: every system + application binding, plus the panel bindings
    /// for the currently focused panel kind, paired with each command's human description.
    public IReadOnlyList<ShortcutHelpRow> BuildHelpRows(string focusedPanelKind)
    {
        var rows = new List<ShortcutHelpRow>();
        foreach (var binding in _bindings)
        {
            if (binding.Scope == KeymapScope.Panel
                && !string.Equals(binding.PanelKind, focusedPanelKind, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rows.Add(new ShortcutHelpRow(binding.Gesture, Description(binding.Command), binding.Scope));
        }

        return rows;
    }

    private static KeyBinding? Match(IReadOnlyList<KeyBinding> bindings, string scope, string gesture) =>
        bindings.FirstOrDefault(binding =>
            binding.Scope == scope && string.Equals(binding.Gesture, gesture, StringComparison.Ordinal));

    private string Description(string command) =>
        _descriptions.TryGetValue(command, out var description) ? description : command;
}
