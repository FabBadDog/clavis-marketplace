module FabioSoft.Clavis.Placeholders.Tests.ParseTests

open FabioSoft.Clavis.Placeholders
open Faqt
open Faqt.Operators
open Xunit

let private engine = PlaceholderEngine()

[<Fact>]
let ``parse keeps literal text and a value token`` () =

    // Act
    let segments = engine.Parse("dir {git.branch} end")

    // Assert
    %segments.Count.Should().Be(3)
    %(segments[0] :?> LiteralSegment).Text.Should().Be("dir ")
    let value = segments[1] :?> ValueSegment
    %value.Key.Should().Be("git.branch")
    %value.Format.Should().BeNull()
    %(segments[2] :?> LiteralSegment).Text.Should().Be(" end")

[<Fact>]
let ``parse reads a value with a format`` () =

    // Act
    let segments = engine.Parse("{agent.name:uppercase}")

    // Assert
    let value = segments[0] :?> ValueSegment
    %value.Key.Should().Be("agent.name")
    %value.Format.Should().Be("uppercase")

[<Fact>]
let ``parse keeps a dotnet format intact past the first colon`` () =

    // Act
    let segments = engine.Parse("{time.now:HH:mm}")

    // Assert
    let value = segments[0] :?> ValueSegment
    %value.Key.Should().Be("time.now")
    %value.Format.Should().Be("HH:mm")

[<Fact>]
let ``parse reads a componentSegment with arg and value`` () =

    // Act
    let segments = engine.Parse("{microstat(arrow-up):turn.runtime}")

    // Assert
    let componentSegment = segments[0] :?> ComponentSegment
    %componentSegment.Component.Should().Be("microstat")
    %componentSegment.Arg.Should().Be("arrow-up")
    %componentSegment.ValueKey.Should().Be("turn.runtime")
    %componentSegment.ValueFormat.Should().BeNull()

[<Fact>]
let ``parse composes a componentSegment value and format`` () =

    // Act
    let segments = engine.Parse("{badge:time.now:HH:mm}")

    // Assert
    let componentSegment = segments[0] :?> ComponentSegment
    %componentSegment.Component.Should().Be("badge")
    %componentSegment.Arg.Should().BeNull()
    %componentSegment.ValueKey.Should().Be("time.now")
    %componentSegment.ValueFormat.Should().Be("HH:mm")

[<Fact>]
let ``parse reads a value-less componentSegment`` () =

    // Act
    let segments = engine.Parse("{limitPlane}")

    // Assert
    let componentSegment = segments[0] :?> ComponentSegment
    %componentSegment.Component.Should().Be("limitPlane")
    %componentSegment.ValueKey.Should().BeNull()
