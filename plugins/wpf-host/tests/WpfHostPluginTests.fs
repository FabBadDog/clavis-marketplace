module FabioSoft.Nucleus.WpfHost.Tests.WpfHostPluginTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Contracts
open FabioSoft.Nucleus.Plugins.WpfHost
open Xunit

let private plugin = WpfHostPlugin()

[<Fact>]
let ``Id is WpfHost`` () =

    %plugin.Id.Should().Be("WpfHost")

[<Fact>]
let ``DefaultConfig is not null`` () =

    %plugin.DefaultConfig.Should().NotBeNull()

[<Fact>]
let ``DefaultConfig has expected defaults`` () =

    // Act
    let config = plugin.DefaultConfig

    // Assert
    %config.UiScaleFactor.Should().Be(1.6)
    %config.DefaultWidth.Should().Be(740.0)
    %config.DefaultHeight.Should().Be(640.0)
    %config.MinWidth.Should().Be(400.0)
    %config.MinHeight.Should().Be(260.0)

[<Fact>]
let ``ValidateConfig accepts valid config`` () =

    // Act
    let result = plugin.ValidateConfigAsync(WpfHostConfig()).Result

    // Assert
    %result.Should().BeOfType<ConfigValid>()

[<Fact>]
let ``ValidateConfig rejects scale factor too low`` () =

    // Act
    let result = plugin.ValidateConfigAsync(WpfHostConfig(UiScaleFactor = 0.1)).Result

    // Assert
    %result.Should().BeOfType<ConfigInvalid>()

[<Fact>]
let ``ValidateConfig rejects scale factor too high`` () =

    // Act
    let result = plugin.ValidateConfigAsync(WpfHostConfig(UiScaleFactor = 5.0)).Result

    // Assert
    %result.Should().BeOfType<ConfigInvalid>()

[<Fact>]
let ``ValidateConfig rejects width less than min`` () =

    // Act
    let result = plugin.ValidateConfigAsync(WpfHostConfig(DefaultWidth = 300.0, MinWidth = 400.0)).Result

    // Assert
    %result.Should().BeOfType<ConfigInvalid>()

[<Fact>]
let ``ValidateConfig rejects height less than min`` () =

    // Act
    let result = plugin.ValidateConfigAsync(WpfHostConfig(DefaultHeight = 200.0, MinHeight = 260.0)).Result

    // Assert
    %result.Should().BeOfType<ConfigInvalid>()
