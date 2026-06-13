module FabioSoft.Clavis.Placeholders.Tests.FormatTests

open FabioSoft.Clavis.Placeholders
open Faqt
open Faqt.Operators
open Xunit

[<Theory>]
[<InlineData("claude", "uppercase", "CLAUDE")>]
[<InlineData("CLAUDE", "lowercase", "claude")>]
[<InlineData("  x  ", "trim", "x")>]
[<InlineData("", "default(-)", "-")>]
[<InlineData("set", "default(-)", "set")>]
[<InlineData("ab", "pad(4)", "ab  ")>]
let ``apply named transforms`` (raw: string) (format: string) (expected: string) =
    %PlaceholderFormats.Apply(raw, format).Should().Be(expected)

[<Fact>]
let ``apply leaves a value unchanged with no format`` () =
    %PlaceholderFormats.Apply("value", null).Should().Be("value")

[<Fact>]
let ``apply a dotnet time format when the value parses as a date`` () =
    %PlaceholderFormats.Apply("2026-06-09T12:48:31", "HH:mm").Should().Be("12:48")

[<Fact>]
let ``apply a numeric format when the value parses as a number`` () =
    %PlaceholderFormats.Apply("0.4237", "F2").Should().Be("0.42")

[<Fact>]
let ``apply leaves an unparseable value verbatim for a typed format`` () =
    %PlaceholderFormats.Apply("hello", "F2").Should().Be("hello")
