module FabioSoft.Nucleus.KeyMap.Tests.KeyGestureTests

open FabioSoft.Nucleus.Plugins.KeyMap
open Faqt
open Faqt.Operators
open FsCheck.Xunit
open Xunit

[<Theory>]
[<InlineData("ctrl+shift+p", "Ctrl+Shift+P")>]
[<InlineData("CTRL+e", "Ctrl+E")>]
[<InlineData("shift+ctrl+p", "Ctrl+Shift+P")>]
[<InlineData("ctrl+alt+space", "Ctrl+Alt+Space")>]
[<InlineData("control + e", "Ctrl+E")>]
[<InlineData("1", "1")>]
[<InlineData("/", "/")>]
[<InlineData("return", "Enter")>]
[<InlineData("esc", "Escape")>]
[<InlineData("pgup", "PageUp")>]
[<InlineData("f5", "F5")>]
[<InlineData("win+up", "Win+Up")>]
let ``normalize produces the canonical chord`` (raw: string) (expected: string) =
    %KeyGesture.TryNormalize(raw).Should().Be(expected)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("ctrl")>]
[<InlineData("ctrl+shift")>]
[<InlineData(null)>]
let ``normalize returns null when there is no key`` (raw: string) =
    %(isNull (KeyGesture.TryNormalize(raw))).Should().BeTrue()

[<Property>]
let ``normalize is idempotent`` (raw: string) =
    match KeyGesture.TryNormalize(raw) with
    | null -> true
    | canonical -> KeyGesture.TryNormalize(canonical) = canonical

[<Fact>]
let ``compose orders modifiers ctrl alt shift win`` () =
    %KeyGesture.Compose(true, true, true, true, "A").Should().Be("Ctrl+Alt+Shift+Win+A")
