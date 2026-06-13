namespace FabioSoft.Clavis.Rendering

open System.Diagnostics.CodeAnalysis
open System.Windows.Input

/// Turns a live WPF key press into the canonical gesture string the keymap uses (e.g. "Ctrl+Shift+P").
/// Lives in the shared rendering assembly so both the WpfHost resolver and the command palette's
/// shortcut-capture produce the SAME spelling that the KeyMap plugin's KeyGesture normalizer emits:
/// modifiers in fixed order Ctrl, Alt, Shift, Win, then the key token. Returns "" for a modifier-only
/// press or an unmappable key.
[<RequireQualifiedAccess>]
[<ExcludeFromCodeCoverage>] // thin WPF-input mapping
module KeyGestureReader =

    let private keyToken (key: Key) =
        let code = int key

        match key with
        | _ when code >= int Key.A && code <= int Key.Z -> string (char (int 'A' + (code - int Key.A)))
        | _ when code >= int Key.D0 && code <= int Key.D9 -> string (code - int Key.D0)
        | _ when code >= int Key.NumPad0 && code <= int Key.NumPad9 -> string (code - int Key.NumPad0)
        | _ when code >= int Key.F1 && code <= int Key.F24 -> "F" + string (code - int Key.F1 + 1)
        | Key.Space -> "Space"
        | Key.Enter -> "Enter"
        | Key.Tab -> "Tab"
        | Key.Escape -> "Escape"
        | Key.Up -> "Up"
        | Key.Down -> "Down"
        | Key.Left -> "Left"
        | Key.Right -> "Right"
        | Key.PageUp -> "PageUp"
        | Key.PageDown -> "PageDown"
        | Key.Home -> "Home"
        | Key.End -> "End"
        | Key.Insert -> "Insert"
        | Key.Delete -> "Delete"
        | Key.Back -> "Backspace"
        | Key.OemQuestion -> "/"
        | Key.OemOpenBrackets -> "["
        | Key.OemCloseBrackets -> "]"
        | Key.OemMinus -> "-"
        | Key.OemPlus -> "="
        | Key.OemComma -> ","
        | Key.OemPeriod -> "."
        | Key.OemSemicolon -> ";"
        | Key.OemQuotes -> "'"
        | Key.OemTilde -> "`"
        | Key.OemPipe -> "\\"
        | _ -> ""

    let canonical (modifiers: ModifierKeys) (key: Key) =
        match keyToken key with
        | "" -> ""
        | token ->
            let parts = System.Collections.Generic.List<string>(5)
            if modifiers.HasFlag ModifierKeys.Control then parts.Add "Ctrl"
            if modifiers.HasFlag ModifierKeys.Alt then parts.Add "Alt"
            if modifiers.HasFlag ModifierKeys.Shift then parts.Add "Shift"
            if modifiers.HasFlag ModifierKeys.Windows then parts.Add "Win"
            parts.Add token
            System.String.Join("+", parts)

    /// True when the gesture carries a modifier that does not itself produce text (Ctrl or Win), so it is
    /// safe to resolve even while a text input has focus. Plain and Shift-only gestures are left for the
    /// focused control to type.
    let isTextSafe (modifiers: ModifierKeys) =
        modifiers.HasFlag ModifierKeys.Control || modifiers.HasFlag ModifierKeys.Windows
