module FabioSoft.Nucleus.KeyMap.Tests.KeyMapPluginTests

open FabioSoft.Nucleus.Contracts
open FabioSoft.Nucleus.Plugins.KeyMap
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``id and default config are exposed`` () =
    let plugin = KeyMapPlugin()
    %plugin.Id.Should().Be("KeyMap")
    %plugin.DefaultConfig.SummonGesture.Should().Be("Ctrl+Shift+V")

[<Fact>]
let ``validate accepts a valid summon gesture and rejects a keyless one`` () =
    task {
        // Arrange
        let plugin = KeyMapPlugin()

        // Act
        let! valid = plugin.ValidateConfigAsync(KeyMapConfig())
        let! invalid = plugin.ValidateConfigAsync(KeyMapConfig(SummonGesture = "ctrl+shift"))

        // Assert
        %(valid :? ConfigValid).Should().BeTrue()
        %(invalid :? ConfigInvalid).Should().BeTrue()
    }
