module FabioSoft.Clavis.IntegrationTests.CommandPaletteTests

open Faqt
open Faqt.Operators
open FabioSoft.Clavis.TestKit
open FabioSoft.Contracts.Keymap
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Contracts
open FabioSoft.Nucleus.Plugins.CommandPalette
open Xunit

let private bootPalette () =
    let plugin = CommandPalettePlugin()
    Harness.boot [ (fun bus -> plugin.ActivateAsync(bus, CommandPaletteConfig())) ]

[<Fact>]
let ``runs a message command with an argument and publishes the constructed message`` () =
    task {
        // Arrange
        use! harness = bootPalette ()

        // Act: the router resolves the type, constructs it from the positional argument, and publishes it.
        harness.Send(RunCommand "TogglePanel git-log")
        let! toggled = harness.WaitFor<TogglePanel>(fun message -> message.Kind = "git-log")

        // Assert
        %toggled.Kind.Should().Be("git-log")
    }

[<Fact>]
let ``errors when a command line matches nothing`` () =
    task {
        // Arrange
        use! harness = bootPalette ()

        // Act
        harness.Send(RunCommand "definitely-not-a-real-command")
        let! error =
            harness.WaitFor<LogEntry>(fun entry ->
                entry.Level = LogLevel.Error && entry.Message.Contains("did not match"))

        // Assert
        %error.Message.Should().Contain("did not match")
    }
