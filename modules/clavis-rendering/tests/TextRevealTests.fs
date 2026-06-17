module FabioSoft.Clavis.Rendering.Tests.TextRevealTests

open FabioSoft.Clavis.Rendering
open Faqt
open Faqt.Operators
open Xunit

[<Theory>]
[<InlineData(0.0, 16.0, 0)>]
[<InlineData(16.0, 0.0, 0)>]
[<InlineData(8.0, 16.0, 1)>]
[<InlineData(16.0, 16.0, 1)>]
[<InlineData(50.0, 16.0, 3)>]
[<InlineData(60.0, 16.0, 4)>]
let ``lineCount estimates wrapped lines from height`` (blockHeight: float) (lineHeight: float) (expected: int) =
    // Act & Assert
    %(TextReveal.lineCount blockHeight lineHeight).Should().Be(expected)

[<Theory>]
[<InlineData(0, true)>]
[<InlineData(1, true)>]
[<InlineData(3, true)>]
[<InlineData(4, false)>]
[<InlineData(10, false)>]
let ``isShort holds up to the short-line limit`` (lines: int) (expected: bool) =
    // Act & Assert
    %(TextReveal.isShort lines).Should().Be(expected)

[<Theory>]
[<InlineData(0, 0.0)>]
[<InlineData(-5, 0.0)>]
[<InlineData(10, 700.0)>]   // below the floor: clamped up to TypewriterMinMs
[<InlineData(50, 1000.0)>]  // mid-range: steady per-character pace (50 * 20)
[<InlineData(200, 2800.0)>] // above the ceiling: clamped down to TypewriterMaxMs
let ``typewriterDuration paces per character within its floor and ceiling`` (charCount: int) (expected: float) =
    // Act & Assert
    %(TextReveal.typewriterDuration charCount).Should().Be(expected)
