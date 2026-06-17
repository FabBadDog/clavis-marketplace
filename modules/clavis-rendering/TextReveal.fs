namespace FabioSoft.Clavis.Rendering

open System

/// Pure timing and measurement behind the conversation text reveal (MarkdownPresenter): classifying a rendered
/// block as short (the typewriter) or long (the line fall), and pacing the typewriter. No WPF here, so it is
/// unit-tested directly; the MarkdownPresenter applies the timings. Durations are the design language's
/// text-reveal tunings.
[<RequireQualifiedAccess>]
module TextReveal =

    /// The wrapped-line count at or below which a block reveals with the typewriter; above it uses the line fall.
    [<Literal>]
    let ShortLineLimit = 3

    /// The typewriter types at a steady pace - this many milliseconds per character - so a longer block
    /// visibly takes longer to type than a short one, instead of every length collapsing into the same flat
    /// window (which reads as an instant pop on short lines).
    [<Literal>]
    let TypewriterMsPerChar = 20.0

    /// Floor and ceiling on the typewriter duration: a single word still types over a perceptible moment, and
    /// a near-three-line block tops out rather than dragging.
    [<Literal>]
    let TypewriterMinMs = 700.0

    [<Literal>]
    let TypewriterMaxMs = 2800.0

    /// The line fall always finishes in this window: the first line fades, the rest slide into place beneath it.
    [<Literal>]
    let LineFallTotalMs = 1000.0

    /// Estimate how many wrapped lines a rendered block occupies from its laid-out height and the body line
    /// height. A zero/negative line height or empty block reports zero lines; any positive block is at least
    /// one line.
    let lineCount (blockHeight: float) (lineHeight: float) =
        if lineHeight <= 0.0 || blockHeight <= 0.0 then
            0
        else
            max 1 (int (Math.Round(blockHeight / lineHeight)))

    /// True when a block of this many wrapped lines reveals with the typewriter rather than the line fall.
    let isShort (lines: int) = lines <= ShortLineLimit

    /// How long the typewriter takes to type `charCount` characters: a steady per-character pace clamped to
    /// [TypewriterMinMs, TypewriterMaxMs]. Empty text reveals instantly (zero window).
    let typewriterDuration (charCount: int) =
        if charCount <= 0 then
            0.0
        else
            float charCount * TypewriterMsPerChar
            |> max TypewriterMinMs
            |> min TypewriterMaxMs
