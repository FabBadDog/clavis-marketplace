module FabioSoft.Nucleus.ResourceBroker.Tests.SchemeRouterTests

open FabioSoft.Nucleus.Plugins.ResourceBroker
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``an unregistered scheme resolves to Unknown carrying the scheme`` () =
    // Arrange
    let router = SchemeRouter()

    // Act
    let outcome = router.Resolve("file:///c:/notes.md")

    // Assert
    %outcome.Kind.Should().Be(RouteKind.Unknown)
    %outcome.Scheme.Should().Be("file")

[<Fact>]
let ``a registered scheme resolves to Handled carrying the lowercased scheme`` () =
    // Arrange
    let router = SchemeRouter()
    router.Register("file", "FileSystem")

    // Act
    let outcome = router.Resolve("file:///c:/notes.md")

    // Assert
    %outcome.Kind.Should().Be(RouteKind.Handled)
    %outcome.Scheme.Should().Be("file")

[<Fact>]
let ``registration and lookup are case-insensitive`` () =
    // Arrange
    let router = SchemeRouter()
    router.Register("FILE", "FileSystem")

    // Act / Assert
    %router.Resolve("FILE:///c:/notes.md").Kind.Should().Be(RouteKind.Handled)

[<Fact>]
let ``a different scheme stays Unknown when another is registered`` () =
    // Arrange
    let router = SchemeRouter()
    router.Register("file", "FileSystem")

    // Act
    let outcome = router.Resolve("https://example.com/page")

    // Assert
    %outcome.Kind.Should().Be(RouteKind.Unknown)
    %outcome.Scheme.Should().Be("https")

[<Fact>]
let ``an unparsable URI resolves to Invalid carrying a message`` () =
    // Arrange
    let router = SchemeRouter()

    // Act
    let outcome = router.Resolve("relative/path/without/scheme")

    // Assert
    %outcome.Kind.Should().Be(RouteKind.Invalid)
    %outcome.Message.Should().NotBeNull()
