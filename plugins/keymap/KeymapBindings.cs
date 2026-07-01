namespace FabioSoft.Nucleus.Plugins.KeyMap;

/// Pure operations over a binding set: the built-in defaults (seeded on first run), assignment and
/// removal, and same-scope duplicate detection. No bus, no I/O. Gestures are normalized through
/// KeyGesture so equality is canonical.
public static class KeymapBindings
{
    /// The default bindings shipped on first run, migrating the formerly hardcoded shortcuts. Panel-scope
    /// entries name the panel kind they apply to.
    public static IReadOnlyList<KeyBinding> Defaults { get; } =
    [
        App("Ctrl+Shift+P", "ToggleCommandPalette"),
        // The panel picker (SelectPanel) lists every user-openable panel kind; Ctrl+P mirrors the
        // command palette's Ctrl+Shift+P so the two pickers sit on the same gesture family.
        App("Ctrl+P", "SelectPanel"),
        App("Ctrl+E", "ToggleShortcutHelp"),
        App("Ctrl+W", "CloseActiveWindow"),
        Sys("Ctrl+Shift+V", "ToggleClavis"),

        // One toggle gesture per panel kind: the same key opens the panel and closes it again. These run
        // through the palette router as TogglePanel commands, so a binding whose kind has no plugin present
        // simply opens nothing (the registry logs a benign "no kind registered") rather than erroring.
        App("Ctrl+D", "TogglePanel events"),
        App("Ctrl+G", "TogglePanel git-log"),
        App("Ctrl+K", "TogglePanel keymap"),
        App("Ctrl+U", "TogglePanel usage-limits"),
        App("Ctrl+O", "TogglePanel code-editor"),
        App("Ctrl+M", "TogglePanel markdown-panels"),

        // Esc dismisses the focused panel. Scoped per kind so it only applies when that panel holds focus,
        // and only for kinds that have no intrinsic Esc behaviour of their own (the events panel and the
        // chat keep theirs - clearing search / aborting input).
        Panel("Escape", "CloseActivePanel", "git-log"),
        Panel("Escape", "CloseActivePanel", "keymap"),
        Panel("Escape", "CloseActivePanel", "usage-limits"),
        Panel("Escape", "CloseActivePanel", "code-editor"),

        // The events panel searches by typing, so it claims no plain character keys; only the arrow-based
        // severity navigation and the scroll chords are bound (Ctrl+Up/Down scroll the chat too). Ctrl is a
        // text-safe modifier (KeyGestureReader.isTextSafe), so the chat scroll works while the prompt input
        // holds focus without hijacking Shift-select-by-line in a multi-line prompt.
        Panel("Left", "events.severity.left", "events"),
        Panel("Right", "events.severity.right", "events"),
        Panel("Ctrl+Up", "events.scroll.up", "events"),
        Panel("Ctrl+Down", "events.scroll.down", "events"),
        Panel("Ctrl+Up", "conversation.scroll.up", "conversation"),
        Panel("Ctrl+Down", "conversation.scroll.down", "conversation")
    ];

    /// Merge persisted bindings over the built-in defaults. Each default command keeps its default gesture
    /// unless the user rebound that exact command (same scope+panel), in which case the user's gesture wins;
    /// persisted bindings for commands that are not defaults (user aliases, agent/panel commands) are kept
    /// too. So a default command renamed or added between builds is still bound from the defaults rather
    /// than silently lost behind a stale persisted entry. Defaults lead the result, so when a stale
    /// persisted gesture collides with a live default's gesture the default wins (first match wins).
    public static IReadOnlyList<KeyBinding> Merge(IReadOnlyList<KeyBinding> persisted)
    {
        var merged = new List<KeyBinding>(Defaults.Count + persisted.Count);

        foreach (var defaultBinding in Defaults)
        {
            var rebound = persisted.FirstOrDefault(binding =>
                SameCommand(binding, defaultBinding.Scope, defaultBinding.PanelKind, defaultBinding.Command));
            merged.Add(rebound ?? defaultBinding);
        }

        foreach (var binding in persisted)
        {
            var isDefaultCommand = Defaults.Any(defaultBinding =>
                SameCommand(binding, defaultBinding.Scope, defaultBinding.PanelKind, defaultBinding.Command));
            if (!isDefaultCommand)
            {
                merged.Add(binding);
            }
        }

        return merged;
    }

    /// Assign a gesture to a command in a scope (and panel kind). Removes any prior gesture bound to the
    /// same command in that scope+panel (so the binding moves), and any other command on the same gesture
    /// in that scope+panel (so a gesture maps to one command). Returns the new set.
    public static IReadOnlyList<KeyBinding> Set(
        IReadOnlyList<KeyBinding> bindings, string command, string scope, string panelKind, string gesture)
    {
        var normalized = KeyGesture.TryNormalize(gesture);
        if (normalized is null)
        {
            return bindings;
        }

        var kept = bindings.Where(binding =>
            !(SameContext(binding, scope, panelKind)
              && (string.Equals(binding.Command, command, StringComparison.Ordinal)
                  || string.Equals(binding.Gesture, normalized, StringComparison.Ordinal))));

        return [.. kept, new KeyBinding(normalized, command, NormalizeScope(scope), panelKind ?? "")];
    }

    /// Remove the binding identified by gesture within a scope (and panel kind).
    public static IReadOnlyList<KeyBinding> Remove(
        IReadOnlyList<KeyBinding> bindings, string gesture, string scope, string panelKind)
    {
        var normalized = KeyGesture.TryNormalize(gesture) ?? gesture;
        return
        [
            .. bindings.Where(binding =>
                !(SameContext(binding, scope, panelKind)
                  && string.Equals(binding.Gesture, normalized, StringComparison.Ordinal)))
        ];
    }

    /// Gestures bound to more than one distinct command within the same scope+panel. These are allowed
    /// but reported so the plugin can warn; at resolution the first match wins.
    public static IReadOnlyList<string> Conflicts(IReadOnlyList<KeyBinding> bindings) =>
    [
        .. bindings
            .GroupBy(binding => (NormalizeScope(binding.Scope), (binding.PanelKind ?? "").ToLowerInvariant(), binding.Gesture))
            .Where(group => group.Select(binding => binding.Command).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(group => group.Key.Item3)
    ];

    private static bool SameCommand(KeyBinding binding, string scope, string panelKind, string command) =>
        SameContext(binding, scope, panelKind)
        && string.Equals(binding.Command, command, StringComparison.Ordinal);

    private static bool SameContext(KeyBinding binding, string scope, string panelKind) =>
        string.Equals(NormalizeScope(binding.Scope), NormalizeScope(scope), StringComparison.Ordinal)
        && string.Equals(binding.PanelKind ?? "", panelKind ?? "", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeScope(string? scope) => (scope ?? "").ToLowerInvariant() switch
    {
        KeymapScope.System => KeymapScope.System,
        KeymapScope.Panel => KeymapScope.Panel,
        _ => KeymapScope.Application
    };

    private static KeyBinding App(string gesture, string command) =>
        new(gesture, command, KeymapScope.Application, "");

    private static KeyBinding Sys(string gesture, string command) =>
        new(gesture, command, KeymapScope.System, "");

    private static KeyBinding Panel(string gesture, string command, string panelKind) =>
        new(gesture, command, KeymapScope.Panel, panelKind);
}
