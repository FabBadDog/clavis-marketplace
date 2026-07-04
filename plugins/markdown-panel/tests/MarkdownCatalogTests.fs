module FabioSoft.Nucleus.MarkdownPanel.Tests.MarkdownCatalogTests

open System
open FabioSoft.Nucleus.Plugins.MarkdownPanel
open Faqt
open Faqt.Operators
open FsCheck.Xunit
open Xunit

let private one = [| MarkdownDefinition("a", "Alpha", "body-a") |]

[<Fact>]
let ``Add appends the new definition`` () =

    // Act
    let result = MarkdownCatalog.Add(one, "b", "Beta", "body-b")

    // Assert
    %result.Count.Should().Be(2)
    %result[1].Should().Be(MarkdownDefinition("b", "Beta", "body-b"))

[<Fact>]
let ``Add normalizes a blank title to the default`` () =

    // Act
    let result = MarkdownCatalog.Add(Array.empty<MarkdownDefinition>, "b", "   ", "")

    // Assert
    %result[0].Title.Should().Be(MarkdownCatalog.DefaultTitle)

[<Fact>]
let ``Update replaces title and body of the matching id only`` () =

    // Arrange
    let two = [| MarkdownDefinition("a", "Alpha", "body-a"); MarkdownDefinition("b", "Beta", "body-b") |]

    // Act
    let result = MarkdownCatalog.Update(two, "a", "Renamed", "new-body")

    // Assert
    %result[0].Should().Be(MarkdownDefinition("a", "Renamed", "new-body"))
    %result[1].Should().Be(MarkdownDefinition("b", "Beta", "body-b"))

[<Fact>]
let ``Update is a no-op when the id is unknown`` () =

    // Act
    let result = MarkdownCatalog.Update(one, "missing", "X", "Y")

    // Assert
    %result.Count.Should().Be(1)
    %result[0].Should().Be(one[0])

[<Fact>]
let ``Delete removes the matching definition`` () =
    %MarkdownCatalog.Delete(one, "a").Count.Should().Be(0)

[<Fact>]
let ``Find returns the matching definition`` () =
    %MarkdownCatalog.Find(one, "a").Should().Be(one[0])

[<Fact>]
let ``Find returns null when absent`` () =
    %MarkdownCatalog.Find(one, "missing").Should().BeNull()

[<Theory>]
[<InlineData("  spaced  ", "spaced")>]
[<InlineData("", "Untitled")>]
[<InlineData(null, "Untitled")>]
let ``NormalizeTitle trims and defaults`` (input: string) (expected: string) =
    %MarkdownCatalog.NormalizeTitle(input).Should().Be(expected)

[<Property>]
let ``NormalizeTitle is idempotent`` (title: string) =
    MarkdownCatalog.NormalizeTitle(MarkdownCatalog.NormalizeTitle title) = MarkdownCatalog.NormalizeTitle title

[<Property>]
let ``Add then Delete restores the original set`` (id: Guid) =
    let key = id.ToString()
    MarkdownCatalog.Delete(MarkdownCatalog.Add(Array.empty<MarkdownDefinition>, key, "t", "b"), key).Count = 0

[<Fact>]
let ``IsTitleTaken is true when another definition has the same normalized title`` () =

    // Arrange
    let two = [| MarkdownDefinition("a", "Alpha", "body-a"); MarkdownDefinition("b", "Beta", "body-b") |]

    // Act
    let result = MarkdownCatalog.IsTitleTaken(two, "a", "  beta  ")

    // Assert
    %result.Should().BeTrue()

[<Fact>]
let ``IsTitleTaken is false when the title matches the definition's own current title`` () =
    %MarkdownCatalog.IsTitleTaken(one, "a", "Alpha").Should().BeFalse()

[<Fact>]
let ``IsTitleTaken is false when no other definition shares the title`` () =
    %MarkdownCatalog.IsTitleTaken(one, "a", "Unique").Should().BeFalse()

[<Fact>]
let ``NextDefaultTitle returns Untitled when nothing collides`` () =
    %MarkdownCatalog.NextDefaultTitle(one).Should().Be("Untitled")

[<Fact>]
let ``NextDefaultTitle skips to the next free suffix when earlier defaults are taken`` () =

    // Arrange
    let taken = [| MarkdownDefinition("a", "Untitled", "b"); MarkdownDefinition("b", "Untitled 2", "b") |]

    // Act
    let result = MarkdownCatalog.NextDefaultTitle(taken)

    // Assert
    %result.Should().Be("Untitled 3")
