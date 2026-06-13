module FabioSoft.Nucleus.KeyMap.Tests.KeymapFileTests

open FabioSoft.Contracts.Keymap
open FabioSoft.Nucleus.Plugins.KeyMap
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``parse of empty yaml is empty`` () =
    %KeymapFile.Parse(null).Should().HaveLength(0)
    %KeymapFile.Parse("").Should().HaveLength(0)

[<Fact>]
let ``serialize then parse round-trips the defaults`` () =

    // Act
    let yaml = KeymapFile.Serialize(KeymapBindings.Defaults)
    let parsed = KeymapFile.Parse(yaml)

    // Assert
    %parsed.Count.Should().Be(KeymapBindings.Defaults.Count)
    let palette = parsed |> Seq.tryFind (fun b -> b.Command = "ToggleCommandPalette")
    %(palette |> Option.map _.Gesture).Should().Be(Some "Ctrl+Shift+P")

[<Fact>]
let ``parse skips entries without a command or with an unparseable key`` () =

    // Arrange
    let yaml =
        "bindings:\n"
        + "  - key: Ctrl+E\n    command: ToggleShortcutHelp\n    scope: application\n"
        + "  - key: Ctrl+X\n    scope: application\n"
        + "  - key: \"\"\n    command: Orphan\n    scope: application\n"

    // Act
    let parsed = KeymapFile.Parse(yaml)

    // Assert
    %parsed.Count.Should().Be(1)
    %parsed[0].Command.Should().Be("ToggleShortcutHelp")

[<Fact>]
let ``parse keeps the panel kind for panel-scoped bindings`` () =

    // Arrange
    let yaml = "bindings:\n  - key: \"1\"\n    command: events.filter.all\n    scope: panel\n    panel: events\n"

    // Act
    let parsed = KeymapFile.Parse(yaml)

    // Assert
    %parsed[0].Scope.Should().Be(KeymapScope.Panel)
    %parsed[0].PanelKind.Should().Be("events")

[<Fact>]
let ``serialize starter contains the default commands`` () =

    // Act
    let yaml = KeymapFile.SerializeStarter()

    // Assert
    %yaml.Should().Contain("ToggleCommandPalette")
    %yaml.Should().Contain("ToggleClavis")
