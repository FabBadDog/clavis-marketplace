module FabioSoft.Nucleus.Settings.Tests.SettingsPluginTests

open FabioSoft.Nucleus.Contracts
open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.Settings
open Xunit

let private plugin = SettingsPlugin()

[<Fact>]
let ``Id is Settings`` () =

    %plugin.Id.Should().Be("Settings")

[<Fact>]
let ``the default config passes validation`` () =
    // Act
    let result = plugin.ValidateConfigAsync(plugin.DefaultConfig).Result

    // Assert
    %result.Should().BeOfType<ConfigValid>()
