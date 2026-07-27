module FabioSoft.Nucleus.Workspaces.Tests.AccentPaletteTests

open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``the palette is four identity accents`` () =

    // Act & Assert
    %AccentPalette.Keys.Should().HaveLength(4)

[<Fact>]
let ``an empty set gets the first accent`` () =

    // Act & Assert
    %AccentPalette.Assign(Seq.empty).Should().Be(AccentPalette.Keys[0])

[<Fact>]
let ``the first four assignments never collide`` () =

    // Act - assign four in a row, each seeing the ones before it
    let assigned =
        [ 1 .. 4 ]
        |> List.fold (fun taken _ -> taken @ [ AccentPalette.Assign taken ]) []

    // Assert
    %(assigned |> List.distinct |> List.length).Should().Be(4)

[<Fact>]
let ``the fifth assignment reuses the least-used accent`` () =

    // Arrange - every accent used once, plus a second use of the first
    let taken = List.ofSeq AccentPalette.Keys @ [ AccentPalette.Keys[0] ]

    // Act
    let next = AccentPalette.Assign taken

    // Assert
    %next.Should().Be(AccentPalette.Keys[1])

[<Fact>]
let ``an unrecognised accent in use does not disturb the count`` () =

    // Act
    let next = AccentPalette.Assign [ "SomethingElseBrush" ]

    // Assert
    %next.Should().Be(AccentPalette.Keys[0])

[<Fact>]
let ``next wraps around the palette`` () =

    // Act & Assert
    %AccentPalette.Next(AccentPalette.Keys[0]).Should().Be(AccentPalette.Keys[1])
    %AccentPalette.Next(AccentPalette.Keys[3]).Should().Be(AccentPalette.Keys[0])
    %AccentPalette.Next("unknown").Should().Be(AccentPalette.Keys[0])

[<Theory>]
[<InlineData("")>]
[<InlineData(null)>]
[<InlineData("NotAnAccent")>]
let ``an unknown persisted accent reads back as the first`` (stored: string) =

    // Act & Assert
    %AccentPalette.OrDefault(stored).Should().Be(AccentPalette.Keys[0])

[<Fact>]
let ``a known persisted accent reads back unchanged`` () =

    // Act & Assert
    %AccentPalette.OrDefault(AccentPalette.Keys[2]).Should().Be(AccentPalette.Keys[2])
