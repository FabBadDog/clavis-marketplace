module FabioSoft.Nucleus.Workspaces.Tests.WorkspaceBarRowsTests

open System
open System.Collections.Generic
open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

let private workspace name slot =
    Workspace(Name = name, Slot = slot, AccentKey = AccentPalette.Keys[0])

let private build (workspaces: Workspace list) activeId =
    WorkspaceBarRows.Build(List<Workspace>(workspaces), activeId)

[<Fact>]
let ``tabs are ordered by slot with gaps preserved`` () =

    // Arrange - a closed slot 2 leaves a hole
    let rows = build [ workspace "c" 5; workspace "a" 1; workspace "b" 3 ] Guid.Empty

    // Assert - the numbers read 1, 3, 5; nobody shuffled left
    %(rows |> Seq.map (fun r -> r.SlotNumber) |> Seq.toList).Should().SequenceEqual([ "1"; "3"; "5" ])

[<Fact>]
let ``a workspace past the keyboard range shows no number`` () =

    // Act
    let rows = build [ workspace "overflow" 0; workspace "keyed" 1 ] Guid.Empty

    // Assert
    %rows[0].SlotNumber.Should().Be("1")
    %rows[1].SlotNumber.Should().Be("")

[<Fact>]
let ``only the active tab is marked active`` () =

    // Arrange
    let active = workspace "active" 1

    // Act
    let rows = build [ active; workspace "other" 2 ] active.WorkspaceId

    // Assert
    %(rows |> Seq.filter (fun r -> r.IsActive) |> Seq.length).Should().Be(1)

[<Fact>]
let ``a long title is truncated and the full one moves to the tooltip`` () =

    // Arrange
    let long = String.replicate 60 "x"

    // Act
    let rows = build [ Workspace(Name = long, Slot = 1, AccentKey = AccentPalette.Keys[0]) ] Guid.Empty

    // Assert - a fixed-width tab must lose characters rather than push its neighbours along
    %rows[0].Title.Length.Should().Be(WorkspaceBarRows.MaxTitleLength)
    %rows[0].Title.EndsWith("…").Should().BeTrue()
    %rows[0].Tooltip.Should().Be(long)

[<Fact>]
let ``a short title is left alone`` () =

    // Act
    let rows = build [ workspace "Reviews" 1 ] Guid.Empty

    // Assert
    %rows[0].Title.Should().Be("Reviews")
    %rows[0].Tooltip.Should().Be("Reviews")

[<Theory>]
[<InlineData("idle", "TextDimBrush", false)>]
[<InlineData("working", "GreenBrush", true)>]
[<InlineData("waiting", "WarnBrush", false)>]
let ``the dot maps activity to colour, and only working pulses``
    (activity: string, expectedBrush: string, expectedBreathing: bool) =

    // Arrange
    let busy = Workspace(Name = "w", Slot = 1, AccentKey = AccentPalette.Keys[0], Activity = activity)

    // Act
    let rows = build [ busy ] Guid.Empty

    // Assert - waiting is the MORE urgent state and deliberately does not pulse; it draws the eye by colour so
    // it stays legible beside a breathing neighbour
    %rows[0].ActivityBrushKey.Should().Be(expectedBrush)
    %rows[0].IsBreathing.Should().Be(expectedBreathing)

[<Fact>]
let ``the dot never carries the workspace accent`` () =

    // Arrange - accents are identity, the dot is state; tinting the dot would destroy the activity signal
    let accents = set AccentPalette.Keys

    // Act
    let brushes =
        [ WorkspaceActivity.Idle; WorkspaceActivity.Working; WorkspaceActivity.Waiting ]
        |> List.map WorkspaceBarRows.ActivityBrushKey

    // Assert
    %(brushes |> List.exists accents.Contains).Should().BeFalse()

[<Fact>]
let ``an unknown accent still renders as a real one`` () =

    // Act
    let rows = build [ Workspace(Name = "odd", Slot = 1, AccentKey = "Nonsense") ] Guid.Empty

    // Assert
    %rows[0].AccentKey.Should().Be(AccentPalette.Keys[0])

[<Fact>]
let ``an empty list produces no tabs`` () =

    // Act & Assert
    %(build [] Guid.Empty).Should().BeEmpty()
