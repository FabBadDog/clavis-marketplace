module FabioSoft.Nucleus.WpfHost.Tests.LayoutFileTests

open System
open System.Collections.Generic
open FabioSoft.Clavis.Rendering
open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

let private slot id kind state =
    { PanelId = id; PanelKind = kind; Title = "t"; SavedState = state }

let private bounds () = PersistedWindowState(10.0, 20.0, 800.0, 600.0, false)

let private leafOf kind state =
    DockingModel.leaf (Guid.NewGuid()) [| slot (Guid.NewGuid()) kind state |] 0

/// A v2 layout: one primary window (belonging to no workspace) plus one docking tree for the active workspace.
let private v2 workspaceId layout =
    let windowId = Guid.NewGuid()
    PersistedLayout(
        LayoutFile.CurrentVersion,
        workspaceId,
        [| PersistedWindow(windowId, WindowRole.Primary, Guid.Empty, bounds ()) |],
        [| PersistedWorkspaceLayout(windowId, workspaceId, layout) |])

[<Fact>]
let ``round-trips a layout with a split tree and per-panel state`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()
    let layout =
        DockingModel.split (Guid.NewGuid()) DockingModel.Horizontal [| 0.6; 0.4 |]
            [| leafOf "chat" ""
               leafOf "markdown" "# Hello" |]

    // Act
    let restored = LayoutFile.Deserialize(LayoutFile.Serialize(v2 workspaceId layout))

    // Assert
    %(isNull (box restored)).Should().BeFalse()
    %restored.ActiveWorkspaceId.Should().Be(workspaceId)
    %restored.Windows.Count.Should().Be(1)
    %restored.Windows[0].IsPrimary.Should().BeTrue()
    %restored.Windows[0].Bounds.Width.Should().Be(800.0)
    let tree = restored.Layouts[0].Layout
    %tree.Kind.Should().Be(DockingModel.Split)
    %tree.Children.Length.Should().Be(2)
    %tree.Sizes[0].Should().Be(0.6)
    %tree.Children[1].Panels[0].PanelKind.Should().Be("markdown")
    %tree.Children[1].Panels[0].SavedState.Should().Be("# Hello")

[<Fact>]
let ``geometry lives on the window and never in a per-workspace layout`` () =

    // Arrange - two workspaces sharing one window
    let windowId = Guid.NewGuid()
    let first = Guid.NewGuid()
    let second = Guid.NewGuid()
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion,
            first,
            [| PersistedWindow(windowId, WindowRole.Primary, Guid.Empty, bounds ()) |],
            [| PersistedWorkspaceLayout(windowId, first, leafOf "chat" "")
               PersistedWorkspaceLayout(windowId, second, leafOf "git-log" "") |])

    // Act
    let restored = LayoutFile.Deserialize(LayoutFile.Serialize saved)

    // Assert - one window, one set of bounds, two arrangements
    %restored.Windows.Count.Should().Be(1)
    %restored.Layouts.Count.Should().Be(2)
    %(restored.For(windowId, second)).Layout.Panels[0].PanelKind.Should().Be("git-log")

[<Fact>]
let ``round-trips a workspace layout's edge slide-ins`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()
    let windowId = Guid.NewGuid()
    let slide = PersistedSlideIn(Guid.NewGuid(), "git-log", "git log", "left", "saved-state")
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion,
            workspaceId,
            [| PersistedWindow(windowId, WindowRole.Primary, Guid.Empty, bounds ()) |],
            [| PersistedWorkspaceLayout(windowId, workspaceId, leafOf "chat" "",
                                        SlideIns = ResizeArray [ slide ]) |])

    // Act
    let restored = LayoutFile.Deserialize(LayoutFile.Serialize saved)

    // Assert
    let slides = restored.Layouts[0].SlideIns
    %slides.Count.Should().Be(1)
    %slides[0].Kind.Should().Be("git-log")
    %slides[0].Edge.Should().Be("left")
    %slides[0].SavedState.Should().Be("saved-state")

[<Fact>]
let ``a layout with no slide-ins round-trips to an empty list`` () =

    // Act
    let restored = LayoutFile.Deserialize(LayoutFile.Serialize(v2 (Guid.NewGuid()) (leafOf "chat" "")))

    // Assert
    %restored.Layouts[0].SlideIns.Count.Should().Be(0)

[<Fact>]
let ``discards a layout whose version is newer than this build understands`` () =

    // Arrange
    let saved = v2 (Guid.NewGuid()) (leafOf "chat" "")
    saved.Version <- LayoutFile.CurrentVersion + 1

    // Act & Assert
    %(isNull (box (LayoutFile.Deserialize(LayoutFile.Serialize saved)))).Should().BeTrue()

[<Fact>]
let ``a secondary window carries the workspace it was torn off in`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()
    let secondaryId = Guid.NewGuid()
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion,
            workspaceId,
            [| PersistedWindow(Guid.NewGuid(), WindowRole.Primary, Guid.Empty, bounds ())
               PersistedWindow(secondaryId, WindowRole.Panel, workspaceId, bounds ()) |],
            [| PersistedWorkspaceLayout(secondaryId, workspaceId, leafOf "code-editor" "") |])

    // Act
    let restored = LayoutFile.Deserialize(LayoutFile.Serialize saved)

    // Assert
    let secondary = restored.Windows |> Seq.find (fun w -> not w.IsPrimary)
    %secondary.WorkspaceId.Should().Be(workspaceId)
    %secondary.Role.Should().Be(WindowRole.Panel)
