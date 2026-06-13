module FabioSoft.Claude.Tests.UsageApiTests

open System
open FabioSoft.Claude
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``parseUsage returns both windows with derived window starts`` () =

    // Arrange
    let json =
        """{"five_hour":{"utilization":40,"resets_at":"2026-06-03T17:00:00+00:00"},"seven_day":{"utilization":32,"resets_at":"2026-06-10T00:00:00+00:00"}}"""

    // Act
    let windows = UsageApi.parseUsage(json).Should().BeOk().WhoseValue

    // Assert
    %windows.Length.Should().Be(2)
    %windows[0].Name.Should().Be("5-Hour")
    %windows[0].Utilization.Should().Be(40.0)
    %windows[0].ResetsAt.Should().Be(DateTimeOffset(2026, 6, 3, 17, 0, 0, TimeSpan.Zero))
    %windows[0].WindowStart.Should().Be(DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero))
    %windows[1].Name.Should().Be("Weekly")
    %windows[1].Utilization.Should().Be(32.0)
    %windows[1].WindowStart.Should().Be(DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero))

[<Fact>]
let ``parseUsage skips a section that is absent`` () =

    // Arrange
    let json = """{"five_hour":{"utilization":12,"resets_at":"2026-06-03T17:00:00+00:00"}}"""

    // Act
    let windows = UsageApi.parseUsage(json).Should().BeOk().WhoseValue

    // Assert
    %windows.Length.Should().Be(1)
    %windows[0].Name.Should().Be("5-Hour")

[<Fact>]
let ``parseUsage drops a window missing its reset timestamp`` () =

    // Arrange
    let json = """{"five_hour":{"utilization":12}}"""

    // Act
    let windows = UsageApi.parseUsage(json).Should().BeOk().WhoseValue

    // Assert
    %windows.Should().BeEmpty()

[<Fact>]
let ``parseUsage defaults a missing utilization to zero`` () =

    // Arrange
    let json = """{"five_hour":{"resets_at":"2026-06-03T17:00:00+00:00"}}"""

    // Act
    let windows = UsageApi.parseUsage(json).Should().BeOk().WhoseValue

    // Assert
    %windows[0].Utilization.Should().Be(0.0)

[<Fact>]
let ``parseUsage surfaces malformed json as an error`` () =
    %UsageApi.parseUsage("not json").Should().BeError()
