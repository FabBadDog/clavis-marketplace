module FabioSoft.Nucleus.AgentGateway.Tests.ActivityRingTests

open System
open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Contracts
open FabioSoft.Nucleus.Plugins.AgentGateway
open Xunit

let private activity (source: string) =
    let metadata = MessageMetadata(Guid.NewGuid(), Nullable(), DateTimeOffset.UtcNow, source, Nullable())
    BusActivity(metadata, typeof<string>, box "payload", Unchecked.defaultof<DeadLetterReason>)

[<Fact>]
let ``the ring keeps only the most recent entries up to capacity`` () =

    // Arrange
    let ring = ActivityRing(3)
    for index in 1..5 do
        ring.Record(activity $"source-{index}")

    // Act
    let recent = ring.Recent(null, 100) |> Seq.toList

    // Assert
    %recent.Length.Should().Be(3)
    %recent[0].Source.Should().Be("source-3")
    %recent[2].Source.Should().Be("source-5")

[<Fact>]
let ``Recent filters by a case-insensitive substring of type or source`` () =

    // Arrange
    let ring = ActivityRing(10)
    ring.Record(activity "alpha")
    ring.Record(activity "beta")
    ring.Record(activity "alphabet")

    // Act
    let matched = ring.Recent("ALPHA", 100) |> Seq.toList

    // Assert
    %matched.Length.Should().Be(2)
    %(matched |> List.forall (fun entry -> entry.Source.Contains("alpha"))).Should().BeTrue()

[<Fact>]
let ``Recent caps the returned count at the limit, newest last`` () =

    // Arrange
    let ring = ActivityRing(10)
    for index in 1..5 do
        ring.Record(activity $"source-{index}")

    // Act
    let recent = ring.Recent(null, 2) |> Seq.toList

    // Assert
    %recent.Length.Should().Be(2)
    %recent[0].Source.Should().Be("source-4")
    %recent[1].Source.Should().Be("source-5")

[<Fact>]
let ``Recent records the payload type name`` () =

    // Arrange
    let ring = ActivityRing(10)
    ring.Record(activity "any")

    // Act
    let recent = ring.Recent(null, 100) |> Seq.toList

    // Assert
    %recent[0].Type.Should().Be("String")
