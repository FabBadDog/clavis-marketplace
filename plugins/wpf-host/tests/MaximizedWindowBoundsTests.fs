module FabioSoft.Nucleus.WpfHost.Tests.MaximizedWindowBoundsTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.WpfHost
open Xunit

[<Theory>]
// Primary monitor at origin, taskbar at the bottom (40 px): full width, height minus the taskbar.
[<InlineData(0, 0, 1920, 1080, 0, 0, 1920, 1040, 0, 0, 1920, 1040)>]
// Taskbar on the left (60 px): the maximized window starts at x=60 and loses that width.
[<InlineData(0, 0, 1920, 1080, 60, 0, 1920, 1080, 60, 0, 1860, 1080)>]
// Secondary monitor to the right: placement is relative to the monitor origin, so x/y are 0.
[<InlineData(1920, 0, 3840, 1080, 1920, 0, 3840, 1040, 0, 0, 1920, 1040)>]
// Monitor with a negative origin (left of the primary): origin offset still resolves to 0,0.
[<InlineData(-1920, 0, 0, 1080, -1920, 0, 0, 1040, 0, 0, 1920, 1040)>]
let ``Compute constrains maximized placement to the work area relative to the monitor origin``
    (monitorLeft, monitorTop, monitorRight, monitorBottom,
     workLeft, workTop, workRight, workBottom,
     expectedX, expectedY, expectedWidth, expectedHeight) =

    // Arrange
    let monitor = ScreenRectangle(monitorLeft, monitorTop, monitorRight, monitorBottom)
    let work = ScreenRectangle(workLeft, workTop, workRight, workBottom)

    // Act
    let placement = MaximizedWindowBounds.Compute(monitor, work)

    // Assert
    %placement.X.Should().Be(expectedX)
    %placement.Y.Should().Be(expectedY)
    %placement.Width.Should().Be(expectedWidth)
    %placement.Height.Should().Be(expectedHeight)

[<Fact>]
let ``ScreenRectangle derives width and height from its edges`` () =

    // Arrange
    let rectangle = ScreenRectangle(10, 20, 110, 220)

    // Assert
    %rectangle.Width.Should().Be(100)
    %rectangle.Height.Should().Be(200)
