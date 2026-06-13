module FabioSoft.Nucleus.ClaudeBridge.Tests.UsageReportMappingTests

open System
open FabioSoft.Claude
open FabioSoft.Nucleus.Plugins.ClaudeBridge
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``ToReport maps utilization onto a used-of-100 percentage window`` () =

    // Arrange
    let start = DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero)
    let windows =
        [| { Name = "5-Hour"; Utilization = 40.0; WindowStart = start; ResetsAt = start.AddHours 5.0 } |]

    // Act
    let report = UsageReportMapping.ToReport(windows)

    // Assert
    %report.Windows.Count.Should().Be(1)
    let window = report.Windows[0]
    %window.Name.Should().Be("5-Hour")
    %window.Used.Should().Be(40.0)
    %window.Total.Should().Be(100.0)
    %window.Unit.Should().Be("%")
    %window.WindowStart.Should().Be(start)
    %window.ResetsAt.Should().Be(start.AddHours 5.0)

[<Fact>]
let ``ToReport carries every window through in order`` () =

    // Arrange
    let start = DateTimeOffset(2026, 6, 3, 12, 0, 0, TimeSpan.Zero)
    let windows =
        [| { Name = "5-Hour"; Utilization = 40.0; WindowStart = start; ResetsAt = start.AddHours 5.0 }
           { Name = "Weekly"; Utilization = 32.0; WindowStart = start; ResetsAt = start.AddDays 7.0 } |]

    // Act
    let report = UsageReportMapping.ToReport(windows)

    // Assert
    %report.Windows.Count.Should().Be(2)
    %report.Windows[1].Name.Should().Be("Weekly")
    %report.Windows[1].Used.Should().Be(32.0)
