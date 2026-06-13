module FabioSoft.Nucleus.WpfHost.Tests.WindowSnapTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.WpfHost
open Xunit

let private snapDistance = 12

let private noWindows: ScreenRectangle[] = Array.empty

let private noWorkAreas: ScreenRectangle[] = Array.empty

[<Fact>]
let ``with no neighbours and no work areas the window does not move`` () =

    // Arrange
    let proposed = ScreenRectangle(300, 300, 500, 500)

    // Act
    let result = WindowSnap.Compute(proposed, noWindows, noWorkAreas, snapDistance)

    // Assert
    %result.Should().Be(proposed)

[<Fact>]
let ``a near edge snaps the window flush against the neighbour, preserving its size`` () =

    // Arrange - the dragged window's left edge (208) sits 8 px from the neighbour's right edge (200)
    let neighbour = ScreenRectangle(0, 0, 200, 300)
    let proposed = ScreenRectangle(208, 0, 408, 300)

    // Act
    let result = WindowSnap.Compute(proposed, [| neighbour |], noWorkAreas, snapDistance)

    // Assert - the left edge snaps onto 200 and the 200 px width is preserved
    %result.Should().Be(ScreenRectangle(200, 0, 400, 300))

[<Fact>]
let ``the window snaps to a monitor work-area edge`` () =

    // Arrange - the right edge (1910) is 10 px from the work-area right edge (1920)
    let workArea = ScreenRectangle(0, 0, 1920, 1040)
    let proposed = ScreenRectangle(1500, 100, 1910, 500)

    // Act
    let result = WindowSnap.Compute(proposed, noWindows, [| workArea |], snapDistance)

    // Assert
    %result.Should().Be(ScreenRectangle(1510, 100, 1920, 500))

[<Fact>]
let ``a window stacked above snaps the dragged window's top to its bottom`` () =

    // Arrange - the dragged window's top (305) is 5 px below the neighbour's bottom (300)
    let above = ScreenRectangle(0, 0, 400, 300)
    let proposed = ScreenRectangle(0, 305, 400, 505)

    // Act
    let result = WindowSnap.Compute(proposed, [| above |], noWorkAreas, snapDistance)

    // Assert
    %result.Should().Be(ScreenRectangle(0, 300, 400, 500))

[<Fact>]
let ``a shorter side-by-side window snaps its bottom to line up with the taller neighbour`` () =

    // Arrange - X (height 200) on the left; the shorter Y (height 100) is docked to its right with tops
    // aligned, then dragged down so its bottom (195) nears X's bottom (200) - the "easy to line up" case.
    let x = ScreenRectangle(0, 0, 200, 200)
    let proposed = ScreenRectangle(200, 95, 400, 195)

    // Act
    let result = WindowSnap.Compute(proposed, [| x |], noWorkAreas, snapDistance)

    // Assert - Y's bottom snaps onto 200, keeping its 100 px height (so the top follows to 100)
    %result.Should().Be(ScreenRectangle(200, 100, 400, 200))

[<Fact>]
let ``a window far on the perpendicular axis does not pull the dragged window`` () =

    // Arrange - W's left edge (205) is only 5 px from the dragged window's left (200), but W sits far
    // below (top 500), outside the dragged window's horizontal band, so its vertical edges must not snap.
    let farBelow = ScreenRectangle(205, 500, 405, 600)
    let proposed = ScreenRectangle(200, 0, 400, 100)

    // Act
    let result = WindowSnap.Compute(proposed, [| farBelow |], noWorkAreas, snapDistance)

    // Assert
    %result.Should().Be(proposed)

[<Fact>]
let ``a corner near a work-area corner snaps on both axes`` () =

    // Arrange
    let workArea = ScreenRectangle(0, 0, 1000, 1000)
    let proposed = ScreenRectangle(8, 9, 208, 209)

    // Act
    let result = WindowSnap.Compute(proposed, noWindows, [| workArea |], snapDistance)

    // Assert
    %result.Should().Be(ScreenRectangle(0, 0, 200, 200))

[<Fact>]
let ``the nearer of two candidate edges wins`` () =

    // Arrange - the dragged window's left (210) is 10 px from one neighbour's right (200) and 3 px from
    // another's right (207); the closer one (207) must win.
    let far = ScreenRectangle(0, 0, 200, 100)
    let near = ScreenRectangle(0, 0, 207, 100)
    let proposed = ScreenRectangle(210, 0, 410, 100)

    // Act
    let result = WindowSnap.Compute(proposed, [| far; near |], noWorkAreas, snapDistance)

    // Assert
    %result.Left.Should().Be(207)

[<Theory>]
// Exactly at the snap distance the left edge pulls onto the work-area edge (0); one pixel beyond it does not.
[<InlineData(12, 0)>]
[<InlineData(13, 13)>]
[<InlineData(-12, 0)>]
[<InlineData(-13, -13)>]
[<InlineData(0, 0)>]
let ``the snap engages only within the snap distance`` (startLeft: int) (expectedLeft: int) =

    // Arrange
    let workArea = ScreenRectangle(0, 0, 1000, 1000)
    let proposed = ScreenRectangle(startLeft, 100, startLeft + 100, 300)

    // Act
    let result = WindowSnap.Compute(proposed, noWindows, [| workArea |], snapDistance)

    // Assert
    %result.Left.Should().Be(expectedLeft)
