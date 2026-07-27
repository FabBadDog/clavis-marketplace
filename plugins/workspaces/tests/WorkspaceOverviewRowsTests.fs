module FabioSoft.Nucleus.Workspaces.Tests.WorkspaceOverviewRowsTests

open System
open System.Collections.Generic
open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

let private now = DateTimeOffset(2026, 7, 27, 12, 0, 0, TimeSpan.Zero)

let private workspace name slot =
    Workspace(Name = name, Slot = slot, AccentKey = AccentPalette.Keys[0], WorkingDirectory = $"C:\\{name}")

let private build (workspaces: Workspace list) activeId =
    WorkspaceOverviewRows.Build(List<Workspace>(workspaces), activeId, now)

[<Fact>]
let ``rows are ordered by slot with gaps preserved`` () =

    // Arrange - slots 4, 1, 7 in creation order
    let workspaces = [ workspace "third" 7; workspace "first" 1; workspace "second" 4 ]

    // Act
    let rows = build workspaces Guid.Empty

    // Assert
    %(rows |> Seq.map (fun r -> r.Name) |> Seq.toList).Should().SequenceEqual([ "first"; "second"; "third" ])
    %(rows |> Seq.map (fun r -> r.SlotLabel) |> Seq.toList).Should().SequenceEqual([ "F1"; "F4"; "F7" ])

[<Fact>]
let ``a slotless workspace sorts last and shows no key hint`` () =

    // Arrange
    let workspaces = [ workspace "overflow" 0; workspace "keyed" 2 ]

    // Act
    let rows = build workspaces Guid.Empty

    // Assert - a blank cell rather than a misleading "F0"
    %rows[0].SlotLabel.Should().Be("F2")
    %rows[1].SlotLabel.Should().Be("")
    %rows[1].Name.Should().Be("overflow")

[<Fact>]
let ``the active row is marked and no other is`` () =

    // Arrange
    let active = workspace "active" 1
    let other = workspace "other" 2

    // Act
    let rows = build [ active; other ] active.WorkspaceId

    // Assert
    %(rows |> Seq.filter (fun r -> r.IsActive) |> Seq.length).Should().Be(1)
    %(rows |> Seq.find (fun r -> r.IsActive)).Name.Should().Be("active")

[<Fact>]
let ``an idle workspace shows no elapsed time`` () =

    // Arrange - idle by default, stamped well in the past
    let idle = Workspace(Name = "idle", Slot = 1, AccentKey = AccentPalette.Keys[0], ActivitySince = now.AddMinutes(-30.0))

    // Act
    let rows = build [ idle ] Guid.Empty

    // Assert - counting up how long it has done nothing is noise
    %rows[0].Elapsed.Should().Be("")

[<Fact>]
let ``a working workspace shows how long it has been going`` () =

    // Arrange
    let busy =
        Workspace(
            Name = "busy", Slot = 1, AccentKey = AccentPalette.Keys[0],
            Activity = WorkspaceActivity.Working, ActivityDetail = "Bash",
            ActivitySince = now.AddSeconds(-90.0))

    // Act
    let rows = build [ busy ] Guid.Empty

    // Assert
    %rows[0].Elapsed.Should().Be("1m")
    %rows[0].Detail.Should().Be("Bash")

[<Fact>]
let ``a workspace with no session is reported as such`` () =

    // Act
    let rows = build [ workspace "fresh" 1 ] Guid.Empty

    // Assert
    %rows[0].HasSession.Should().BeFalse()

[<Fact>]
let ``an unknown accent falls back to a real one`` () =

    // Arrange
    let odd = Workspace(Name = "odd", Slot = 1, AccentKey = "NotAnAccentBrush")

    // Act
    let rows = build [ odd ] Guid.Empty

    // Assert - the row must render as *an* accent rather than as nothing
    %rows[0].AccentKey.Should().Be(AccentPalette.Keys[0])

[<Theory>]
[<InlineData(0, "0s")>]
[<InlineData(45, "45s")>]
[<InlineData(60, "1m")>]
[<InlineData(3599, "59m")>]
[<InlineData(3600, "1h 0m")>]
[<InlineData(7860, "2h 11m")>]
let ``elapsed is coarse and glanceable`` (seconds: int, expected: string) =

    // Act & Assert
    %(WorkspaceOverviewRows.Elapsed(TimeSpan.FromSeconds(float seconds))).Should().Be(expected)

[<Fact>]
let ``a negative span reads as zero rather than a minus sign`` () =

    // Act & Assert - a clock adjustment must not render "-3s"
    %(WorkspaceOverviewRows.Elapsed(TimeSpan.FromSeconds(-3.0))).Should().Be("0s")

[<Fact>]
let ``an empty set produces no rows`` () =

    // Act & Assert
    %(build [] Guid.Empty).Should().BeEmpty()
