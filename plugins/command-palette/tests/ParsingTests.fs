module FabioSoft.Nucleus.CommandPalette.Tests.ParsingTests

open System
open System.Collections.Generic
open FabioSoft.Nucleus.Plugins.CommandPalette
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``parse splits name and named arguments`` () =

    // Act
    let parsed = CommandLineParser.Parse("LogEntry Level=Debug Source=\"Command Palette\" Message=hi")

    // Assert
    %parsed.Name.Should().Be("LogEntry")
    %parsed.Named["Level"].Should().Be("Debug")
    %parsed.Named["Source"].Should().Be("Command Palette")
    %parsed.Named["Message"].Should().Be("hi")
    %parsed.Positional.Count.Should().Be(0)

[<Fact>]
let ``parse collects positional arguments and strips quotes`` () =

    // Act
    let parsed = CommandLineParser.Parse("cmd   \"a b\"   c")

    // Assert
    %parsed.Name.Should().Be("cmd")
    %parsed.Positional.Should().SequenceEqual([ "a b"; "c" ])

[<Fact>]
let ``parse exposes raw arguments text after the name`` () =

    // Act
    let parsed = CommandLineParser.Parse("clear  foo bar")

    // Assert
    %parsed.ArgumentsText.Should().Be("foo bar")

[<Fact>]
let ``parse of empty input yields empty name`` () =

    // Act
    let parsed = CommandLineParser.Parse("   ")

    // Assert
    %parsed.Name.Should().Be("")
    %parsed.Positional.Count.Should().Be(0)

[<Fact>]
let ``parse keeps equals inside quotes as part of the value`` () =

    // Act
    let parsed = CommandLineParser.Parse("cmd note=\"a=b\"")

    // Assert
    %parsed.Named["note"].Should().Be("a=b")

[<Fact>]
let ``resolve replaces known placeholders and leaves unknown`` () =

    // Arrange
    let map =
        dict [ "Now", Func<string>(fun () -> "NOW") ]
        |> Dictionary
        :> IReadOnlyDictionary<string, Func<string>>

    // Act
    let result = Placeholders.Resolve("t={Now} u={Unknown}", map)

    // Assert
    %result.Should().Be("t=NOW u={Unknown}")

[<Fact>]
let ``default placeholders include Now and Guid`` () =

    // Assert
    %Placeholders.Default.ContainsKey("Now").Should().BeTrue()
    %Placeholders.Default.ContainsKey("Guid").Should().BeTrue()
