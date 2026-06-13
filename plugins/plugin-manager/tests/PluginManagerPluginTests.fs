module FabioSoft.Nucleus.PluginManager.Tests.PluginManagerPluginTests

open FabioSoft.Nucleus.Contracts
open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.PluginManager
open Xunit

let private plugin = PluginManagerPlugin()

[<Fact>]
let ``Id is PluginManager`` () =

    %plugin.Id.Should().Be("PluginManager")

[<Fact>]
let ``the default config passes validation`` () =
    // Act
    let result = plugin.ValidateConfigAsync(plugin.DefaultConfig).Result

    // Assert
    %result.Should().BeOfType<ConfigValid>()
