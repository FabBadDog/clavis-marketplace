module FabioSoft.Nucleus.WpfHost.Tests.WorkspaceStoreTests

open System
open FabioSoft.Clavis.Rendering
open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

let private slot id kind state =
    { PanelId = id; PanelKind = kind; Title = "t"; SavedState = state }

[<Fact>]
let ``round-trips a workspace with a split layout and per-panel state`` () =

    // Arrange
    let groupOne = Guid.NewGuid()
    let groupTwo = Guid.NewGuid()
    let layout =
        DockingModel.split (Guid.NewGuid()) DockingModel.Horizontal [| 0.6; 0.4 |]
            [| DockingModel.leaf groupOne [| slot (Guid.NewGuid()) "conversation" "" |] 0
               DockingModel.leaf groupTwo [| slot (Guid.NewGuid()) "markdown" "# Hello" |] 0 |]
    let bounds = PersistedWindowState(10.0, 20.0, 800.0, 600.0, false)
    let window = PersistedWindow(Guid.NewGuid(), true, bounds, layout)
    let workspace = WorkspaceLayout(WorkspaceStore.CurrentVersion, [| window |])

    // Act
    let restored = WorkspaceStore.Deserialize(WorkspaceStore.Serialize(workspace))

    // Assert
    %(isNull (box restored)).Should().BeFalse()
    %restored.Windows.Count.Should().Be(1)
    let restoredWindow = restored.Windows[0]
    %restoredWindow.IsPrimary.Should().BeTrue()
    %restoredWindow.Bounds.Width.Should().Be(800.0)
    %restoredWindow.Layout.Kind.Should().Be(DockingModel.Split)
    %restoredWindow.Layout.Children.Length.Should().Be(2)
    %restoredWindow.Layout.Sizes[0].Should().Be(0.6)
    let markdownSlot = restoredWindow.Layout.Children[1].Panels[0]
    %markdownSlot.PanelKind.Should().Be("markdown")
    %markdownSlot.SavedState.Should().Be("# Hello")

[<Fact>]
let ``round-trips a window's edge slide-ins`` () =

    // Arrange
    let bounds = PersistedWindowState(0.0, 0.0, 800.0, 600.0, false)
    let layout = DockingModel.leaf (Guid.NewGuid()) [| slot (Guid.NewGuid()) "conversation" "" |] 0
    let slide = PersistedSlideIn(Guid.NewGuid(), "git-log", "git log", "left", "saved-state")
    let window =
        PersistedWindow(Guid.NewGuid(), true, bounds, layout, SlideIns = ResizeArray [ slide ])
    let workspace = WorkspaceLayout(WorkspaceStore.CurrentVersion, [| window |])

    // Act
    let restored = WorkspaceStore.Deserialize(WorkspaceStore.Serialize(workspace))

    // Assert
    %(isNull (box restored)).Should().BeFalse()
    let slides = restored.Windows[0].SlideIns
    %slides.Count.Should().Be(1)
    %slides[0].Kind.Should().Be("git-log")
    %slides[0].Edge.Should().Be("left")
    %slides[0].SavedState.Should().Be("saved-state")

[<Fact>]
let ``a window with no slide-ins round-trips to an empty list`` () =

    // Arrange
    let bounds = PersistedWindowState(0.0, 0.0, 800.0, 600.0, false)
    let layout = DockingModel.leaf (Guid.NewGuid()) [| slot (Guid.NewGuid()) "conversation" "" |] 0
    let window = PersistedWindow(Guid.NewGuid(), true, bounds, layout)
    let workspace = WorkspaceLayout(WorkspaceStore.CurrentVersion, [| window |])

    // Act
    let restored = WorkspaceStore.Deserialize(WorkspaceStore.Serialize(workspace))

    // Assert
    %restored.Windows[0].SlideIns.Count.Should().Be(0)

[<Fact>]
let ``discards a layout whose version does not match the current version`` () =

    // Arrange: a layout serialized with an incompatible (future) version
    let bounds = PersistedWindowState(0.0, 0.0, 800.0, 600.0, false)
    let layout = DockingModel.leaf (Guid.NewGuid()) [| slot (Guid.NewGuid()) "conversation" "" |] 0
    let window = PersistedWindow(Guid.NewGuid(), true, bounds, layout)
    let json = WorkspaceStore.Serialize(WorkspaceLayout(WorkspaceStore.CurrentVersion + 1, [| window |]))

    // Act
    let restored = WorkspaceStore.Deserialize(json)

    // Assert
    %(isNull (box restored)).Should().BeTrue()
