module FabioSoft.Nucleus.WpfHost.Tests.FocusRingTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.WpfHost
open Xunit

[<Theory>]
[<InlineData(3, 0, true, 1)>]
[<InlineData(3, 1, true, 2)>]
[<InlineData(3, 2, false, 1)>]
[<InlineData(3, 1, false, 0)>]
let ``Advance moves to the neighbouring tab stop within the window`` (stopCount, currentIndex, forward, expected) =

    // Act
    let result = FocusRing.Advance(stopCount, currentIndex, forward)

    // Assert
    %result.HasValue.Should().BeTrue()
    %result.Value.Should().Be(expected)

[<Theory>]
[<InlineData(3, 2, true)>] // past the last stop
[<InlineData(3, 0, false)>] // before the first stop
[<InlineData(0, 0, true)>] // a window with no stops
[<InlineData(0, 0, false)>]
let ``Advance returns null at a window boundary`` (stopCount, currentIndex, forward) =

    // Act
    let result = FocusRing.Advance(stopCount, currentIndex, forward)

    // Assert
    %result.HasValue.Should().BeFalse()

[<Fact>]
let ``NextWindowWithStops moves to the next window in the tab direction, wrapping`` () =

    // Arrange
    let windows = [| true; true; true |]

    // Act / Assert
    %FocusRing.NextWindowWithStops(windows, 0, true).Should().Be(1)
    %FocusRing.NextWindowWithStops(windows, 2, true).Should().Be(0)
    %FocusRing.NextWindowWithStops(windows, 0, false).Should().Be(2)

[<Fact>]
let ``NextWindowWithStops skips windows that have no stops`` () =

    // Arrange
    let windows = [| true; false; true |]

    // Act / Assert
    %FocusRing.NextWindowWithStops(windows, 0, true).Should().Be(2)
    %FocusRing.NextWindowWithStops(windows, 2, true).Should().Be(0)

[<Fact>]
let ``NextWindowWithStops stays put when it is the only window with stops`` () =

    // Arrange
    let windows = [| false; true; false |]

    // Act / Assert
    %FocusRing.NextWindowWithStops(windows, 1, true).Should().Be(1)

[<Fact>]
let ``NextWindowWithStops stays put when no window has stops`` () =

    // Arrange
    let windows = [| false; false |]

    // Act / Assert
    %FocusRing.NextWindowWithStops(windows, 0, true).Should().Be(0)
