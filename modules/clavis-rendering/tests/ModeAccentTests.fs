module FabioSoft.Clavis.Rendering.Tests.ModeAccentTests

open FabioSoft.Clavis.Rendering
open Faqt
open Faqt.Operators
open Xunit

[<Theory>]
[<InlineData("auto", "YellowBrush")>]
[<InlineData("acceptEdits", "SecondaryAccentBrush")>]
[<InlineData("accept", "SecondaryAccentBrush")>]
[<InlineData("plan", "GreenBrush")>]
let ``an accented mode maps to its theme brush key`` (mode: string) (expected: string) =
    %(ModeAccent.resourceKey mode).Should().Be(Some expected)

[<Theory>]
[<InlineData("")>]
[<InlineData("default")>]
[<InlineData("none")>]
let ``the default and empty modes have no accent`` (mode: string) =
    %(ModeAccent.resourceKey mode).Should().Be(None)

[<Fact>]
let ``an unknown value falls back to the neutral dim key`` () =
    %(ModeAccent.resourceKey "claude-opus-4-8").Should().Be(Some "TextDimBrush")

[<Fact>]
let ``the C#-friendly form returns null for a no-accent mode`` () =
    %(ModeAccent.resourceKeyOrNull "default").Should().BeNull()

[<Fact>]
let ``the C#-friendly form returns the key for an accented mode`` () =
    %(ModeAccent.resourceKeyOrNull "plan").Should().Be("GreenBrush")
