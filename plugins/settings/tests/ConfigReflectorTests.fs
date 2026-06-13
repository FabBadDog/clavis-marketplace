module FabioSoft.Nucleus.Settings.Tests.ConfigReflectorTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.Settings
open FabioSoft.Nucleus.Plugins.Conversation
open Xunit

[<Fact>]
let ``reflects ConversationConfig properties`` () =

    // Act
    let properties = ConfigReflector.Reflect(typeof<ConversationConfig>)

    // Assert
    %(properties.Count > 0).Should().BeTrue()
    let initTimeout = properties |> Seq.tryFind (fun p -> p.Name = "InitTimeoutSeconds")
    %initTimeout.IsSome.Should().BeTrue()
    %initTimeout.Value.TypeName.Should().Be("int")

[<Fact>]
let ``reflects EventsPanelConfig properties`` () =

    // Act
    let properties = ConfigReflector.Reflect(typeof<FabioSoft.Nucleus.Plugins.EventsPanel.EventsPanelConfig>)

    // Assert
    let maxEntries = properties |> Seq.tryFind (fun p -> p.Name = "MaxEntries")
    %maxEntries.IsSome.Should().BeTrue()

[<Fact>]
let ``returns default values`` () =

    // Act
    let properties = ConfigReflector.Reflect(typeof<ConversationConfig>)

    // Assert
    let initTimeout = properties |> Seq.find (fun p -> p.Name = "InitTimeoutSeconds")
    %(initTimeout.DefaultValue :?> int).Should().Be(240)

[<Fact>]
let ``returns empty list for type with no public properties`` () =

    // Act
    let properties = ConfigReflector.Reflect(typeof<SettingsConfig>)

    // Assert
    %properties.Count.Should().Be(0)
