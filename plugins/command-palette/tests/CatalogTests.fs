module FabioSoft.Nucleus.CommandPalette.Tests.CatalogTests

open System
open System.IO
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.CommandPalette
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``discover includes constructible contract messages and excludes abstract bases`` () =

    // Act
    let catalog = MessageCatalog.Discover()

    // Assert
    %(catalog |> Seq.contains typeof<LogEntry>).Should().BeTrue()
    %(catalog |> Seq.contains typeof<ApplicationShutdown>).Should().BeTrue()
    %(catalog |> Seq.contains typeof<AgentStreamEvent>).Should().BeFalse()

[<Fact>]
let ``resolve matches by simple name and full name and rejects the unknown`` () =

    // Arrange
    let catalog = MessageCatalog.Discover()

    // Act / Assert
    %MessageCatalog.Resolve(catalog, "LogEntry").Should().Be(typeof<LogEntry>)
    %MessageCatalog.Resolve(catalog, "FabioSoft.Nucleus.Contracts.LogEntry").Should().Be(typeof<LogEntry>)
    %(isNull (MessageCatalog.Resolve(catalog, "NoSuchMessage"))).Should().BeTrue()

[<Fact>]
let ``alias parse falls back to built-ins and merges file entries`` () =

    // Act
    let builtInOnly = AliasCatalog.Parse(null)
    let merged = AliasCatalog.Parse("aliases:\n  foo: Bar\n")

    // Assert
    %builtInOnly.ContainsKey("exit").Should().BeTrue()
    %builtInOnly.ContainsKey("restart").Should().BeTrue()
    %merged.ContainsKey("exit").Should().BeTrue()
    %merged["foo"].Should().Be("Bar")

[<Fact>]
let ``serialize starter contains the built-in aliases`` () =

    // Act
    let yaml = AliasCatalog.SerializeStarter()

    // Assert
    %yaml.Should().Contain("exit")

[<Theory>]
[<InlineData("appointments", "Manage the calendar (user)", CommandKind.Skill, "Manage the calendar", "user", "appointments")>]
[<InlineData("compact", "Free up context by summarizing", CommandKind.Agent, "Free up context by summarizing", "built-in", "compact")>]
[<InlineData("plugin-dev:create-plugin", "Scaffold a plugin", CommandKind.Agent, "Scaffold a plugin", "plugin-dev", "create-plugin")>]
[<InlineData("plugin-settings", "(plugin-dev) Configure plugin settings", CommandKind.Agent, "Configure plugin settings", "plugin-dev", "plugin-settings")>]
let ``from agent command classifies source and strips the plugin prefix into the display name``
    (name: string)
    (description: string)
    (expectedKind: CommandKind)
    (expectedDescription: string)
    (expectedSource: string)
    (expectedDisplayName: string) =

    // Act
    let item = CommandItem.FromAgentCommand(name, description)

    // Assert
    %item.Name.Should().Be(name)
    %item.Kind.Should().Be(expectedKind)
    %item.Description.Should().Be(expectedDescription)
    %item.Source.Should().Be(expectedSource)
    %item.DisplayName.Should().Be(expectedDisplayName)

[<Fact>]
let ``for message uses the description attribute and the contract group`` () =

    // Act
    let item = CommandItem.ForMessage(typeof<ApplicationShutdown>)

    // Assert
    %item.Kind.Should().Be(CommandKind.Message)
    %item.Description.Should().Be("Shut the application down")
    %item.Source.Should().Be("Nucleus")
    %item.DisplayName.Should().Be("ApplicationShutdown")

[<Fact>]
let ``for message falls back to a humanized name when no description attribute is present`` () =

    // Act
    let item = CommandItem.ForMessage(typeof<AgentTextDelta>)

    // Assert
    %item.Description.Should().Be("Agent Text Delta")
    %item.Source.Should().Be("Session")

[<Theory>]
[<InlineData("UserSubmittedPrompt", "User Submitted Prompt")>]
[<InlineData("LogEntry", "Log Entry")>]
[<InlineData("UiRegionContribution", "Ui Region Contribution")>]
let ``humanize splits pascal case into words`` (name: string) (expected: string) =
    %MessageDescription.Humanize(name).Should().Be(expected)

[<Fact>]
let ``validate config accepts the default and rejects an out-of-range width`` () =
    task {
        // Arrange
        let plugin = CommandPalettePlugin()

        // Act
        let! valid = plugin.ValidateConfigAsync(CommandPaletteConfig())
        let! invalid = plugin.ValidateConfigAsync(CommandPaletteConfig(100.0))

        // Assert
        %(valid :? ConfigValid).Should().BeTrue()
        %(invalid :? ConfigInvalid).Should().BeTrue()
    }
