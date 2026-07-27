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

    /// True when a gesture would be consumed by a focused text input as editing or caret input, so a keymap
    /// binding must yield to it.
    ///
    /// This asks about the **key**, not just the modifiers. Asking only about modifiers ("is it Ctrl or Win?")
    /// gets the common case right and then silently swallows every other non-text key: bare F1-F24 and Escape
    /// carry no modifier, produce no text, and were being handed to the text box, which ignores them - so the
    /// binding simply never fired while the prompt had focus.
    ///
    /// Text-producing keys (letters, digits, punctuation, Space, Enter, Backspace, Delete, Tab) and caret
    /// movement (arrows, Home, End, PageUp/Down) with at most Shift qualify. Function keys, Escape, and
    /// anything carrying Ctrl, Alt or Win do not, so a shortcut on those fires while typing. Tab is classified
    /// here once rather than special-cased at the call site.
    let isTextEditingGesture (modifiers: ModifierKeys) (key: Key) =
        let carriesCommandModifier =
            modifiers.HasFlag ModifierKeys.Control
            || modifiers.HasFlag ModifierKeys.Alt
            || modifiers.HasFlag ModifierKeys.Windows

        if carriesCommandModifier then
            false
        else
            let code = int key

            match key with
            | _ when code >= int Key.F1 && code <= int Key.F24 -> false
            | Key.Escape -> false
            | _ when code >= int Key.A && code <= int Key.Z -> true
            | _ when code >= int Key.D0 && code <= int Key.D9 -> true
            | _ when code >= int Key.NumPad0 && code <= int Key.NumPad9 -> true
            | Key.Space
            | Key.Enter
            | Key.Tab
            | Key.Back
            | Key.Delete
            | Key.Up
            | Key.Down
            | Key.Left
            | Key.Right
            | Key.Home
            | Key.End
            | Key.PageUp
            | Key.PageDown -> true
            // Punctuation and anything else that yields a token is text; a key with no token produces nothing
            // and so is free for a binding.
            | _ -> keyToken key <> ""
