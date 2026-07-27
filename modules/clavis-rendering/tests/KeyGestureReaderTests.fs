module FabioSoft.Clavis.Rendering.Tests.KeyGestureReaderTests

open System.Windows.Input
open FabioSoft.Clavis.Rendering
open Faqt
open Faqt.Operators
open Xunit

// The point of isTextEditingGesture is that it asks about the KEY, not only the modifiers. Asking only about
// modifiers gets the common case right and then silently swallows every other non-text key - bare F-keys and
// Escape carry no modifier and produce no text, so a binding on them never fired while a text box had focus.

[<Theory>]
// Text-producing keys with at most Shift belong to the focused input.
[<InlineData(ModifierKeys.None, Key.A, true)>]
[<InlineData(ModifierKeys.Shift, Key.A, true)>]
[<InlineData(ModifierKeys.None, Key.D5, true)>]
[<InlineData(ModifierKeys.None, Key.Space, true)>]
[<InlineData(ModifierKeys.None, Key.Enter, true)>]
[<InlineData(ModifierKeys.None, Key.Back, true)>]
[<InlineData(ModifierKeys.None, Key.Delete, true)>]
[<InlineData(ModifierKeys.None, Key.OemPeriod, true)>]
// Caret movement is editing too, including Shift-select.
[<InlineData(ModifierKeys.None, Key.Left, true)>]
[<InlineData(ModifierKeys.Shift, Key.Left, true)>]
[<InlineData(ModifierKeys.None, Key.Home, true)>]
[<InlineData(ModifierKeys.None, Key.PageDown, true)>]
// Tab is classified here once rather than special-cased at the call site.
[<InlineData(ModifierKeys.None, Key.Tab, true)>]
// Function keys and Escape produce nothing, so a binding on them must fire while typing - the WP8 fix.
[<InlineData(ModifierKeys.None, Key.F1, false)>]
[<InlineData(ModifierKeys.Shift, Key.F1, false)>]
[<InlineData(ModifierKeys.None, Key.F11, false)>]
[<InlineData(ModifierKeys.None, Key.F12, false)>]
[<InlineData(ModifierKeys.None, Key.Escape, false)>]
// A command modifier always wins, whatever the key.
[<InlineData(ModifierKeys.Control, Key.Left, false)>]
[<InlineData(ModifierKeys.Control, Key.A, false)>]
[<InlineData(ModifierKeys.Alt, Key.A, false)>]
[<InlineData(ModifierKeys.Windows, Key.A, false)>]
[<InlineData(ModifierKeys.Control, Key.Tab, false)>]
let ``a focused text input only keeps the gestures it would actually consume``
    (modifiers: ModifierKeys, key: Key, expected: bool) =

    // Act & Assert
    %(KeyGestureReader.isTextEditingGesture modifiers key).Should().Be(expected)

[<Fact>]
let ``a key that produces no token at all is free for a binding`` () =

    // Act & Assert - no canonical token means nothing is typed, so nothing is swallowed
    %(KeyGestureReader.canonical ModifierKeys.None Key.LeftCtrl).Should().Be("")
    %(KeyGestureReader.isTextEditingGesture ModifierKeys.None Key.LeftCtrl).Should().BeFalse()

[<Fact>]
let ``every function key slot used for workspace switching stays available while typing`` () =

    // Act - F1 to F12 are the workspace shortcuts; all must survive a focused prompt
    let swallowed =
        [ Key.F1; Key.F2; Key.F3; Key.F4; Key.F5; Key.F6
          Key.F7; Key.F8; Key.F9; Key.F10; Key.F11; Key.F12 ]
        |> List.filter (KeyGestureReader.isTextEditingGesture ModifierKeys.None)

    // Assert
    %swallowed.Should().BeEmpty()
