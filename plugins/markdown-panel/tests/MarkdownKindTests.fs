module FabioSoft.Nucleus.MarkdownPanel.Tests.MarkdownKindTests

open System
open FabioSoft.Nucleus.Plugins.MarkdownPanel
open Faqt
open Faqt.Operators
open FsCheck.Xunit
open Xunit

[<Fact>]
let ``ForDefinition prefixes the id`` () =
    %MarkdownKind.ForDefinition("abc").Should().Be("markdown:abc")

[<Fact>]
let ``DefinitionId strips the prefix`` () =
    %MarkdownKind.DefinitionId("markdown:abc").Should().Be("abc")

[<Fact>]
let ``DefinitionId is null for a non-display kind`` () =
    %MarkdownKind.DefinitionId("git-log").Should().BeNull()

[<Fact>]
let ``DefinitionId is null for the manager kind`` () =
    %MarkdownKind.DefinitionId(MarkdownKind.ManagerKind).Should().BeNull()

[<Property>]
let ``ForDefinition and DefinitionId round-trip`` (id: Guid) =
    let text = id.ToString()
    MarkdownKind.DefinitionId(MarkdownKind.ForDefinition text) = text
