module FabioSoft.Clavis.IntegrationTests.ConfigurationTests

open System
open System.IO
open Faqt
open Faqt.Operators
open FabioSoft.Clavis.TestKit
open FabioSoft.Contracts.Services
open FabioSoft.Nucleus.Plugins.Configuration
open Xunit

let private bootConfiguration () =
    // A fresh temp directory per harness so tests never share state or touch the real ~/.clavis.
    let directory = Path.Combine(Path.GetTempPath(), $"clavis-config-{Guid.NewGuid():N}")
    let plugin = ConfigurationPlugin()
    let config = ConfigurationConfig(Path.Combine(directory, "configuration.yaml"), Path.Combine(directory, "state.yaml"))
    Harness.boot [ (fun bus -> plugin.ActivateAsync(bus, config)) ]

[<Fact>]
let ``returns ConfigNotFound for an unknown plugin`` () =
    task {
        // Arrange
        use! harness = bootConfiguration ()

        // Act
        harness.Send(GetConfig "ghost-plugin")
        let! result = harness.WaitFor<ConfigNotFound>(fun message -> message.PluginId = "ghost-plugin")

        // Assert
        %result.PluginId.Should().Be("ghost-plugin")
    }

[<Fact>]
let ``returns StateNotFound for a plugin with no saved state`` () =
    task {
        // Arrange
        use! harness = bootConfiguration ()

        // Act
        harness.Send(GetState "ghost-plugin")
        let! result = harness.WaitFor<StateNotFound>(fun message -> message.PluginId = "ghost-plugin")

        // Assert
        %result.PluginId.Should().Be("ghost-plugin")
    }

[<Fact>]
let ``saving a config acknowledges, broadcasts the change, and round-trips on a later get`` () =
    task {
        // Arrange
        use! harness = bootConfiguration ()
        let raw = "model: opus\nverbose: true"

        // Act
        harness.Send(SaveConfig("my-plugin", raw))
        let! _ = harness.WaitFor<ConfigSaved>(fun message -> message.PluginId = "my-plugin")
        let! changed = harness.WaitFor<ConfigChanged>(fun message -> message.PluginId = "my-plugin")
        harness.Send(GetConfig "my-plugin")
        let! found = harness.WaitFor<ConfigFound>(fun message -> message.PluginId = "my-plugin")

        // Assert
        // ConfigChanged echoes the exact bytes the plugin saved; GetConfig returns the section
        // re-serialized from the sectioned store, so the data round-trips but the formatting may differ.
        %changed.RawConfig.Should().Be(raw)
        %found.RawConfig.Should().Contain("model: opus")
        %found.RawConfig.Should().Contain("verbose: true")
    }
