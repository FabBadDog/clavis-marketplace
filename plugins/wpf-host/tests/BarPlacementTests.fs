module FabioSoft.Nucleus.WpfHost.Tests.BarPlacementTests

open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

// The monitor rectangle arrives in physical pixels and WPF positions in DIPs, so the DPI divide is the whole
// point of this type: getting it wrong is invisible at 100% scaling and puts the bar off-screen at 150%.

[<Fact>]
let ``the bar spans the full width of its monitor at the top`` () =

    // Act
    let rect = BarPlacement.Compute(ScreenRectangle(0, 0, 2560, 1440), 30.0, 1.0)

    // Assert
    %rect.Left.Should().Be(0.0)
    %rect.Top.Should().Be(0.0)
    %rect.Width.Should().Be(2560.0)
    %rect.Height.Should().Be(30.0)

[<Fact>]
let ``physical pixels are converted to DIPs at high DPI`` () =

    // Act - a 3840px-wide monitor at 150% is 2560 DIPs
    let rect = BarPlacement.Compute(ScreenRectangle(0, 0, 3840, 2160), 30.0, 1.5)

    // Assert
    %rect.Width.Should().Be(2560.0)

[<Fact>]
let ``a secondary monitor keeps its offset, scaled`` () =

    // Act - the monitor starts at x=2560 physical, 200% scaling
    let rect = BarPlacement.Compute(ScreenRectangle(2560, 0, 5120, 1440), 30.0, 2.0)

    // Assert
    %rect.Left.Should().Be(1280.0)
    %rect.Width.Should().Be(1280.0)

[<Theory>]
[<InlineData(0.0)>]
[<InlineData(-1.0)>]
let ``a nonsensical DPI factor falls back to 1.0 rather than dividing by zero`` (dpi: float) =

    // Act
    let rect = BarPlacement.Compute(ScreenRectangle(0, 0, 1920, 1080), 30.0, dpi)

    // Assert
    %rect.Width.Should().Be(1920.0)

[<Fact>]
let ``a zero-height bar takes no space`` () =

    // Act & Assert
    %(BarPlacement.Compute(ScreenRectangle(0, 0, 1920, 1080), 0.0, 1.0)).Height.Should().Be(0.0)

[<Fact>]
let ``an inverted monitor rectangle never yields a negative width`` () =

    // Act - an unplugged or not-yet-reported display
    let rect = BarPlacement.Compute(ScreenRectangle(100, 0, 0, 0), 30.0, 1.0)

    // Assert
    %rect.Width.Should().Be(0.0)

[<Fact>]
let ``reserving takes the bar's strip off the top of the work area`` () =

    // Act - 30 DIPs at 150% is 45 physical pixels
    let reserved = BarPlacement.Reserve(ScreenRectangle(0, 0, 3840, 2160), 30.0, 1.5)

    // Assert
    %reserved.Top.Should().Be(45)
    %reserved.Left.Should().Be(0)
    %reserved.Bottom.Should().Be(2160)

[<Fact>]
let ``a bar taller than the screen does not invert the work area`` () =

    // Act - a config error must not hand out a negative rectangle
    let reserved = BarPlacement.Reserve(ScreenRectangle(0, 0, 1920, 100), 500.0, 1.0)

    // Assert
    %reserved.Top.Should().Be(0)
    %reserved.Bottom.Should().Be(100)
