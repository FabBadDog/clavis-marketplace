module FabioSoft.Clavis.IntegrationTests.KeymapTests

open System
open System.IO
open Faqt
open Faqt.Operators
open FabioSoft.Clavis.TestKit
open FabioSoft.Contracts.Keymap
open FabioSoft.Nucleus.Plugins.Configuration
open FabioSoft.Nucleus.Plugins.KeyMap
open Xunit

// KeyMap broadcasts only via the Configuration plugin's ConfigChanged echo, so the two are booted together.
// Configuration first, so its GetConfig/SaveConfig handlers exist before KeyMap asks for its config.
let private bootKeymap () =
    let directory = Path.Combine(Path.GetTempPath(), $"clavis-keymap-{Guid.NewGuid():N}")
    let configuration = ConfigurationPlugin()
    let keymap = KeyMapPlugin()

    let configurationConfig =
        ConfigurationConfig(Path.Combine(directory, "configuration.yaml"), Path.Combine(directory, "state.yaml"))

    Harness.boot
        [ (fun bus -> configuration.ActivateAsync(bus, configurationConfig))
          (fun bus -> keymap.ActivateAsync(bus, KeyMapConfig())) ]

[<Fact>]
let ``seeds and broadcasts default bindings on first run`` () =
    task {
        // Arrange / Act
        use! harness = bootKeymap ()
        let! changed = harness.WaitFor<KeymapChanged>(fun message -> message.Bindings.Count > 0)

        // Assert
        %changed.Bindings.Count.Should().BeGreaterThan(0)
    }

[<Fact>]
let ``setting a binding persists and re-broadcasts it`` () =
    task {
        // Arrange
        use! harness = bootKeymap ()
        let! _ = harness.WaitFor<KeymapChanged>(fun message -> message.Bindings.Count > 0)

        // Act
        harness.Send(SetKeyBinding("MyTestCommand", KeymapScope.Application, "", "Ctrl+Alt+J"))
        let! changed =
            harness.WaitFor<KeymapChanged>(fun message ->
                message.Bindings |> Seq.exists (fun binding -> binding.Command = "MyTestCommand"))

        // Assert
        %(changed.Bindings |> Seq.exists (fun binding -> binding.Command = "MyTestCommand"))
            .Should()
            .BeTrue()
    }
