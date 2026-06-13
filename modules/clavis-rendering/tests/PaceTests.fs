module FabioSoft.Clavis.Rendering.Tests.PaceTests

open System
open FabioSoft.Clavis.Rendering
open Faqt
open Faqt.Operators
open Xunit

let private start = DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero)

let private at (hours: float) =
    start + TimeSpan.FromHours(hours)

[<Theory>]
[<InlineData(0.95, "on pace")>]
[<InlineData(1.0, "on pace")>]
[<InlineData(1.07, "on pace")>]
[<InlineData(0.92, "under")>]
[<InlineData(0.5, "under")>]
[<InlineData(1.08, "over")>]
[<InlineData(1.49, "over")>]
[<InlineData(1.5, "far over")>]
[<InlineData(3.0, "far over")>]
let ``classify maps the burn ratio to the right band`` ratio expectedLabel =
    %(Pace.classify ratio).ToString().Should().Be(expectedLabel)

[<Fact>]
let ``compute at one fifth elapsed with double spend is far over`` () =

    // Arrange
    let resetsAt = at 5.0

    // Act
    let result = Pace.compute 40.0 100.0 start resetsAt (at 1.0)

    // Assert
    %result.SpentFraction.Should().Be(0.4)
    %result.TimeFraction.Should().Be(0.2)
    %result.Expected.Should().Be(20.0)
    %result.Delta.Should().Be(20.0)
    %result.Status.Should().Be(FarOver)

[<Fact>]
let ``compute with spend matching elapsed is on pace with zero delta`` () =

    // Act
    let result = Pace.compute 50.0 100.0 start (at 5.0) (at 2.5)

    // Assert
    %result.Delta.Should().Be(0.0)
    %result.Ratio.Should().Be(1.0)
    %result.Status.Should().Be(OnPace)

[<Fact>]
let ``compute with spend below elapsed is under pace with negative delta`` () =

    // Act
    let result = Pace.compute 10.0 100.0 start (at 5.0) (at 2.5)

    // Assert
    %result.Delta.Should().Be(-40.0)
    %result.Status.Should().Be(Under)

[<Fact>]
let ``compute reports time ahead of the even burn`` () =

    // Act - 40% spent is 2h of a 5h budget, but only 1h elapsed: 1h of budget ahead
    let result = Pace.compute 40.0 100.0 start (at 5.0) (at 1.0)

    // Assert
    %result.TimeAhead.Should().Be(TimeSpan.FromHours(1.0))

[<Fact>]
let ``compute clamps a non-positive total to an empty, on-pace window`` () =

    // Act
    let result = Pace.compute 0.0 0.0 start (at 5.0) (at 1.0)

    // Assert
    %result.SpentFraction.Should().Be(0.0)
    %result.Ratio.Should().Be(1.0)
    %result.Status.Should().Be(OnPace)

[<Fact>]
let ``compute treats any spend at zero elapsed as far over`` () =

    // Act - now equals window start, so no time has elapsed
    let result = Pace.compute 5.0 100.0 start (at 5.0) start

    // Assert
    %result.TimeFraction.Should().Be(0.0)
    %result.Status.Should().Be(FarOver)

[<Fact>]
let ``compute with zero spend at zero elapsed is on pace`` () =
    %(Pace.compute 0.0 100.0 start (at 5.0) start).Status.Should().Be(OnPace)

[<Fact>]
let ``compute clamps a degenerate span to a fully elapsed window`` () =

    // Act - resetsAt before windowStart
    let result = Pace.compute 30.0 100.0 start (at -1.0) (at 1.0)

    // Assert
    %result.TimeFraction.Should().Be(1.0)
    %result.TimeAhead.Should().Be(TimeSpan.Zero)

[<Fact>]
let ``compute clamps elapsed time past the reset to a full window`` () =

    // Act - now is after resetsAt
    let result = Pace.compute 80.0 100.0 start (at 5.0) (at 9.0)

    // Assert
    %result.TimeFraction.Should().Be(1.0)
    %result.Status.Should().Be(Under)

[<Fact>]
let ``compute clamps over-budget spend to a full spent fraction`` () =

    // Act - utilization above the total (overage)
    let result = Pace.compute 120.0 100.0 start (at 5.0) (at 5.0)

    // Assert
    %result.SpentFraction.Should().Be(1.0)
    %result.Delta.Should().Be(20.0)

[<Theory>]
[<InlineData(0, "0m")>]
[<InlineData(12, "12m")>]
[<InlineData(60, "1h 00m")>]
[<InlineData(245, "4h 05m")>]
[<InlineData(1440, "1d 00h")>]
[<InlineData(5790, "4d 00h")>]
let ``formatDuration renders a compact countdown`` minutes expected =
    %(Pace.formatDuration (TimeSpan.FromMinutes(float minutes))).Should().Be(expected)

[<Fact>]
let ``formatDuration clamps a negative span to zero`` () =
    %(Pace.formatDuration (TimeSpan.FromMinutes(-5.0))).Should().Be("0m")

[<Fact>]
let ``PaceStatus renders its label`` () =
    %Under.ToString().Should().Be("under")
    %OnPace.ToString().Should().Be("on pace")
    %Over.ToString().Should().Be("over")
    %FarOver.ToString().Should().Be("far over")
