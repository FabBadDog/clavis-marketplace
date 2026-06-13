namespace FabioSoft.Nucleus.Plugins.KeyMap;

/// Pure parsing/normalization of a key gesture string into a canonical chord spelling. The canonical
/// form lists modifiers in a fixed order (Ctrl, Alt, Shift, Win) followed by the key token, joined by
/// '+', e.g. "Ctrl+Shift+P", "Ctrl+Alt+Space", "1", "/". The WpfHost resolver produces the SAME spelling
/// from live WPF input, so a normalized YAML/user gesture compares equal to a pressed one.
public static class KeyGesture
{
    private const string Ctrl = "Ctrl";
    private const string Alt = "Alt";
    private const string Shift = "Shift";
    private const string Win = "Win";

    private static readonly Dictionary<string, string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = "Space",
        ["enter"] = "Enter",
        ["return"] = "Enter",
        ["esc"] = "Escape",
        ["escape"] = "Escape",
        ["tab"] = "Tab",
        ["up"] = "Up",
        ["down"] = "Down",
        ["left"] = "Left",
        ["right"] = "Right",
        ["pageup"] = "PageUp",
        ["pgup"] = "PageUp",
        ["pagedown"] = "PageDown",
        ["pgdn"] = "PageDown",
        ["home"] = "Home",
        ["end"] = "End",
        ["ins"] = "Insert",
        ["insert"] = "Insert",
        ["del"] = "Delete",
        ["delete"] = "Delete",
        ["back"] = "Backspace",
        ["backspace"] = "Backspace"
    };

    /// Normalize a raw gesture string to its canonical form, or return null when it has no usable key.
    public static string? TryNormalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var hasCtrl = false;
        var hasAlt = false;
        var hasShift = false;
        var hasWin = false;
        string? key = null;

        foreach (var token in raw.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    hasCtrl = true;
                    break;
                case "alt" or "option":
                    hasAlt = true;
                    break;
                case "shift":
                    hasShift = true;
                    break;
                case "win" or "super" or "cmd" or "meta":
                    hasWin = true;
                    break;
                default:
                    // More than one non-modifier token is ambiguous; the last one wins (lenient).
                    key = NormalizeKey(token);
                    break;
            }
        }

        if (key is null)
        {
            return null;
        }

        return Compose(hasCtrl, hasAlt, hasShift, hasWin, key);
    }

    /// Assemble a canonical gesture from already-classified modifiers and a normalized key token. Shared
    /// shape used by both this module and the host resolver.
    public static string Compose(bool ctrl, bool alt, bool shift, bool win, string keyToken)
    {
        var parts = new List<string>(5);
        if (ctrl)
        {
            parts.Add(Ctrl);
        }

        if (alt)
        {
            parts.Add(Alt);
        }

        if (shift)
        {
            parts.Add(Shift);
        }

        if (win)
        {
            parts.Add(Win);
        }

        parts.Add(keyToken);
        return string.Join("+", parts);
    }

    private static string NormalizeKey(string token)
    {
        if (token.Length == 1)
        {
            var character = token[0];
            return char.IsLetter(character) ? char.ToUpperInvariant(character).ToString() : token;
        }

        if (NamedKeys.TryGetValue(token, out var named))
        {
            return named;
        }

        if (token.Length is >= 2 and <= 3
            && (token[0] == 'f' || token[0] == 'F')
            && int.TryParse(token.AsSpan(1), out var functionNumber)
            && functionNumber is >= 1 and <= 24)
        {
            return $"F{functionNumber}";
        }

        // Unknown multi-character key: title-case so spelling is stable.
        return char.ToUpperInvariant(token[0]) + token[1..].ToLowerInvariant();
    }
}
