module FabioSoft.Nucleus.EventsPanel.Tests.EventsPanelPluginTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.EventsPanel
open Xunit

let private plugin = EventsPanelPlugin()

[<Fact>]
let ``Id is EventsPanel`` () =

    %plugin.Id.Should().Be("EventsPanel")

[<Fact>]
let ``DefaultConfig is not null`` () =

    %plugin.DefaultConfig.Should().NotBeNull()

[<Fact>]
let ``DefaultConfig has expected defaults`` () =

    // Act
    let config = plugin.DefaultConfig

    // Assert
    %config.MaxEntries.Should().Be(10_000)
