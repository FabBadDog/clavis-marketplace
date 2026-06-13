module FabioSoft.Clavis.Placeholders.Tests.CompletionTests

open System.Collections.Generic
open FabioSoft.Contracts.Placeholders
open FabioSoft.Clavis.Placeholders
open Faqt
open Faqt.Operators
open Xunit

let private descriptors: IReadOnlyList<PlaceholderDescriptor> =
    [| PlaceholderDescriptor("git.branch", "value", "main", "current branch")
       PlaceholderDescriptor("git.repo", "value", "clavis", "origin repo")
       PlaceholderDescriptor("agent.name", "value", "claude", "agent name") |]

let private components = PlaceholderComponents.All
let private formats = PlaceholderFormats.Known

let private labels (result: CompletionResult) =
    result.Items |> Seq.map (fun item -> item.Label) |> List.ofSeq

[<Fact>]
let ``complete offers namespaces and components at the token head`` () =

    // Act
    let result = PlaceholderCompletion.Complete("{", 1, descriptors, components, formats)

    // Assert
    %result.ReplaceStart.Should().Be(1)
    %(labels result).Should().Contain("git.")
    %(labels result).Should().Contain("agent.")
    %(labels result).Should().Contain("bar")

[<Fact>]
let ``complete filters value keys by prefix`` () =

    // Act
    let result = PlaceholderCompletion.Complete("{git.", 5, descriptors, components, formats)

    // Assert
    %result.ReplaceStart.Should().Be(1)
    %(labels result).Should().Contain("git.branch")
    %(labels result).Should().Contain("git.repo")
    %(labels result |> List.contains "agent.name").Should().BeFalse()

[<Fact>]
let ``complete offers formats after a value colon`` () =

    // Arrange
    let text = "{agent.name:"

    // Act
    let result = PlaceholderCompletion.Complete(text, text.Length, descriptors, components, formats)

    // Assert
    %result.ReplaceStart.Should().Be(12)
    %(labels result).Should().Contain("uppercase")

[<Fact>]
let ``complete offers value keys after a component colon`` () =

    // Arrange
    let text = "{bar:git."

    // Act
    let result = PlaceholderCompletion.Complete(text, text.Length, descriptors, components, formats)

    // Assert
    %result.ReplaceStart.Should().Be(5)
    %(labels result).Should().Contain("git.branch")

[<Fact>]
let ``complete returns nothing outside a token`` () =
    %PlaceholderCompletion.Complete("plain text", 10, descriptors, components, formats).Items.Count.Should().Be(0)

[<Fact>]
let ``complete returns nothing after a closed token`` () =
    let text = "{git.branch} "
    %PlaceholderCompletion.Complete(text, text.Length, descriptors, components, formats).Items.Count.Should().Be(0)
