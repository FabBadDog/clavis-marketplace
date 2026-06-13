module FabioSoft.Nucleus.Conversation.Tests.ContextWindowTests

open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``standard model gets the default window`` () =
    %ContextWindow.ForModel("claude-opus-4-8").Should().Be(200_000)

[<Fact>]
let ``one-million-token variant gets the extended window`` () =
    %ContextWindow.ForModel("claude-opus-4-8[1m]").Should().Be(1_000_000)

[<Fact>]
let ``unknown model falls back to the default window`` () =
    %ContextWindow.ForModel(null).Should().Be(200_000)
