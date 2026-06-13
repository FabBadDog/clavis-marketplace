namespace FabioSoft.Clavis.Rendering

open System
open System.Globalization

/// One usage-limit window: an abstract budget (Used/Total in some Unit) bounded by a time span
/// (WindowStart..ResetsAt). Declared here, not pulled from the Session contracts, so the rendering
/// library carries no contract dependency - the plugin maps AgentLimitWindow across.
type LimitWindow =
    { Name: string
      Used: float
      Total: float
      Unit: string
      WindowStart: DateTimeOffset
      ResetsAt: DateTimeOffset }

/// A window resolved against the wall clock. The two fractions (clamped to 0..1) place the dot; Delta is
/// the signed pace gap - spend fraction minus elapsed fraction - so positive is overspending (below the
/// even-burn diagonal) and negative is a surplus (above it).
type WindowUsage =
    { SpentFraction: float
      TimeFraction: float
      Delta: float }

/// The first ceiling a set of windows imposes. Several limits cap each other: you hit the one with the
/// least remaining budget before any larger headroom matters, so the smallest remaining wins. Carries the
/// binding window's name and unit for the readout label.
type LimitCeiling =
    { Effective: float
      BindingName: string
      Unit: string }

[<RequireQualifiedAccess>]
module LimitWindow =

    let private clamp01 value =
        max 0.0 (min 1.0 value)

    /// Resolve a window's spend against its elapsed time at `now`. Degenerate inputs (non-positive total,
    /// zero-or-negative span, now outside the window) are clamped rather than throwing, so the caller never
    /// has to pre-validate provider data.
    let resolve (window: LimitWindow) (now: DateTimeOffset) =

        let rawSpent = if window.Total <= 0.0 then 0.0 else window.Used / window.Total
        let spanTicks = (window.ResetsAt - window.WindowStart).Ticks
        let timeFraction =
            if spanTicks <= 0L then
                1.0
            else
                clamp01 (float (now - window.WindowStart).Ticks / float spanTicks)
        let spentFraction = clamp01 rawSpent

        { SpentFraction = spentFraction
          TimeFraction = timeFraction
          Delta = spentFraction - timeFraction }

    /// Budget still available in a window, never negative.
    let remaining (window: LimitWindow) =
        max 0.0 (window.Total - window.Used)

    /// The binding ceiling across windows: the one with the least remaining budget. None for an empty set.
    let ceiling windows =
        match windows with
        | [] -> None
        | _ ->
            let binding = windows |> List.minBy remaining
            Some
                { Effective = remaining binding
                  BindingName = binding.Name
                  Unit = binding.Unit }

    /// Format a remaining-time span as a compact countdown: days+hours, hours+minutes, or minutes.
    let formatDuration (span: TimeSpan) =
        let clamped = if span < TimeSpan.Zero then TimeSpan.Zero else span
        if clamped.TotalDays >= 1.0 then
            $"{int clamped.TotalDays}d {clamped.Hours:D2}h"
        elif clamped.TotalHours >= 1.0 then
            $"{int clamped.TotalHours}h {clamped.Minutes:D2}m"
        else
            $"{clamped.Minutes}m"

    /// Format an abstract budget amount compactly: 20000 -> "20k", 1500000 -> "1.5M".
    let formatAmount amount =
        let value = max 0.0 amount
        let scaled divisor suffix =
            (value / divisor).ToString("0.#", CultureInfo.InvariantCulture) + suffix
        if value >= 1_000_000.0 then
            scaled 1_000_000.0 "M"
        elif value >= 1_000.0 then
            scaled 1_000.0 "k"
        else
            string (int (Math.Round value))
