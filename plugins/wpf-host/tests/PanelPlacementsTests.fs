module FabioSoft.Nucleus.WpfHost.Tests.PanelPlacementsTests

open System
open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

let private chrome workspaceId = PlaceableWindow(Guid.NewGuid(), workspaceId, true)

let private tornOff workspaceId = PlaceableWindow(Guid.NewGuid(), workspaceId, false)

let private fallback = Guid.NewGuid()

[<Fact>]
let ``a workspace's panel goes to that workspace's window, not where the kind last sat`` () =

    // Arrange
    let mine = chrome (Guid.NewGuid())
    let theirs = chrome (Guid.NewGuid())

    // Act
    let placed =
        PanelPlacements.PlacementWindow([| theirs; mine |], mine.WorkspaceId, theirs.WindowId, fallback)

    // Assert
    %placed.Should().Be(mine.WindowId)

[<Fact>]
let ``the remembered window is kept while it belongs to the panel's own workspace`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()
    let window = chrome workspaceId
    let torn = tornOff workspaceId

    // Act
    let placed =
        PanelPlacements.PlacementWindow([| window; torn |], workspaceId, torn.WindowId, fallback)

    // Assert
    %placed.Should().Be(torn.WindowId)

[<Fact>]
let ``a panel belonging to no workspace keeps the remembered window`` () =

    // Arrange
    let elsewhere = chrome (Guid.NewGuid())

    // Act
    let placed =
        PanelPlacements.PlacementWindow([| elsewhere |], Guid.Empty, elsewhere.WindowId, fallback)

    // Assert
    %placed.Should().Be(elsewhere.WindowId)

[<Fact>]
let ``a workspace with no window yet falls back`` () =

    // Arrange
    let elsewhere = chrome (Guid.NewGuid())

    // Act
    let placed =
        PanelPlacements.PlacementWindow([| elsewhere |], Guid.NewGuid(), elsewhere.WindowId, fallback)

    // Assert
    %placed.Should().Be(fallback)

[<Fact>]
let ``a remembered window that no longer exists falls back to the workspace's own`` () =

    // Arrange
    let mine = chrome (Guid.NewGuid())

    // Act
    let placed = PanelPlacements.PlacementWindow([| mine |], mine.WorkspaceId, Guid.NewGuid(), fallback)

    // Assert
    %placed.Should().Be(mine.WindowId)

[<Fact>]
let ``four workspaces opening the same kind each land in their own window`` () =

    // Arrange
    let windows = [| for _ in 1..4 -> chrome (Guid.NewGuid()) |]

    // Act - each open remembers where the previous one landed, which is the shape that collapsed them
    let placed =
        windows
        |> Array.fold
            (fun (remembered, landed) window ->
                let target =
                    PanelPlacements.PlacementWindow(windows, window.WorkspaceId, remembered, fallback)

                target, landed @ [ target ])
            (Guid.Empty, [])
        |> snd

    // Assert
    %placed.Should().SequenceEqual(windows |> Array.map _.WindowId)
