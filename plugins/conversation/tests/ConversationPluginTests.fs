module FabioSoft.Nucleus.Conversation.Tests.ConversationPluginTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.Conversation
open Xunit

let private plugin = ConversationPlugin()

[<Fact>]
let ``Id is Conversation`` () =

    %plugin.Id.Should().Be("Conversation")

[<Fact>]
let ``DefaultConfig is not null`` () =

    %plugin.DefaultConfig.Should().NotBeNull()

[<Fact>]
let ``DefaultConfig has expected defaults`` () =

    // Act
    let config = plugin.DefaultConfig

    // Assert
    %config.InitTimeoutSeconds.Should().Be(240)
