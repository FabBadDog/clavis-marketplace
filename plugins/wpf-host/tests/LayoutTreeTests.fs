module FabioSoft.Nucleus.WpfHost.Tests.LayoutTreeTests

open System
open FabioSoft.Clavis.Rendering
open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

let private slot id kind =
    { PanelId = id; PanelKind = kind; Title = "t"; SavedState = "" }

let private leafOf activeIndex slots =
    DockingModel.leaf (Guid.NewGuid()) slots activeIndex

[<Fact>]
let ``enumerates every slot of a nested split tree in order`` () =

    // Arrange
    let first = Guid.NewGuid()
    let second = Guid.NewGuid()
    let third = Guid.NewGuid()
    let tree =
        DockingModel.split (Guid.NewGuid()) DockingModel.Horizontal [| 0.5; 0.5 |]
            [| leafOf 0 [| slot first "chat" |]
               DockingModel.split (Guid.NewGuid()) DockingModel.Vertical [| 0.5; 0.5 |]
                   [| leafOf 0 [| slot second "git-log" |]
                      leafOf 0 [| slot third "events" |] |] |]

    // Act
    let ids = LayoutTree.EnumerateSlots tree |> Seq.map _.PanelId |> List.ofSeq

    // Assert
    %ids.Should().SequenceEqual([ first; second; third ])

[<Fact>]
let ``enumerating a leaf with no panels yields nothing`` () =
    // Act
    let slots = LayoutTree.EnumerateSlots(leafOf 0 [||])

    // Assert
    %slots.Should().BeEmpty()

[<Fact>]
let ``tags only the active tab of each leaf group as visible`` () =

    // Arrange - two tab groups, each showing its second tab
    let hidden = Guid.NewGuid()
    let shown = Guid.NewGuid()
    let tree =
        DockingModel.split (Guid.NewGuid()) DockingModel.Horizontal [| 0.5; 0.5 |]
            [| leafOf 1 [| slot hidden "chat"; slot shown "git-log" |]
               leafOf 0 [| slot (Guid.NewGuid()) "events" |] |]

    // Act
    let visible =
        LayoutTree.EnumerateSlotsWithVisibility tree
        |> Seq.filter (fun (struct (_, isActiveTab)) -> isActiveTab)
        |> Seq.map (fun (struct (slot, _)) -> slot.PanelId)
        |> List.ofSeq

    // Assert
    %visible.Should().Contain(shown)
    %visible.Should().NotContain(hidden)

[<Fact>]
let ``folds the saved state of each panel into its slot`` () =

    // Arrange
    let known = Guid.NewGuid()
    let unknown = Guid.NewGuid()
    let tree = leafOf 0 [| slot known "events"; slot unknown "git-log" |]
    let state = readOnlyDict [ known, "severity=warn" ]

    // Act
    let folded = LayoutTree.FoldState(tree, state)

    // Assert
    let byId = folded.Panels |> Array.map (fun slot -> slot.PanelId, slot.SavedState) |> Map.ofArray
    %byId[known].Should().Be("severity=warn")
    %byId[unknown].Should().Be("")

[<Fact>]
let ``folding preserves the shape of a nested tree`` () =

    // Arrange
    let tree =
        DockingModel.split (Guid.NewGuid()) DockingModel.Vertical [| 0.3; 0.7 |]
            [| leafOf 0 [| slot (Guid.NewGuid()) "chat" |]
               leafOf 0 [| slot (Guid.NewGuid()) "events" |] |]

    // Act
    let folded = LayoutTree.FoldState(tree, readOnlyDict [])

    // Assert
    %folded.Kind.Should().Be(tree.Kind)
    %folded.Orientation.Should().Be(DockingModel.Vertical)
    %folded.Sizes.Should().SequenceEqual([| 0.3; 0.7 |])
    %folded.Children.Length.Should().Be(2)

[<Theory>]
// a 200x100 window at (0,0) has its centre at (100,50)
[<InlineData(0.0, 0.0, true)>]          // centre inside the desktop
[<InlineData(-300.0, 0.0, false)>]      // centre left of the desktop
[<InlineData(0.0, -200.0, false)>]      // centre above the desktop
[<InlineData(1900.0, 0.0, false)>]      // centre right of the desktop
[<InlineData(0.0, 1100.0, false)>]      // centre below the desktop
[<InlineData(0.0, 1000.0, true)>]       // centre at y=1050, still just inside a 1080-tall desktop
let ``a window counts as on screen only when its centre falls inside the desktop``
    (left: float) (top: float) (expected: bool) =

    // Arrange - a single 1920x1080 desktop at the origin
    let bounds = PersistedWindowState(left, top, 200.0, 100.0, false)

    // Act
    let onScreen = LayoutTree.IsCenterWithin(bounds, 0.0, 0.0, 1920.0, 1080.0)

    // Assert
    %onScreen.Should().Be(expected)

[<Fact>]
let ``a window on an unplugged monitor to the left is not on screen`` () =

    // Arrange - the saved layout sits on a monitor that used to extend to negative coordinates
    let bounds = PersistedWindowState(-1720.0, 100.0, 800.0, 600.0, false)

    // Act
    let onScreen = LayoutTree.IsCenterWithin(bounds, 0.0, 0.0, 1920.0, 1080.0)

    // Assert
    %onScreen.Should().BeFalse()

[<Fact>]
let ``renames a retired panel kind throughout a nested tree`` () =

    // Arrange
    let renamed = Guid.NewGuid()
    let untouched = Guid.NewGuid()
    let tree =
        DockingModel.split (Guid.NewGuid()) DockingModel.Horizontal [| 0.5; 0.5 |]
            [| leafOf 0 [| slot renamed "conversation" |]
               leafOf 0 [| slot untouched "git-log" |] |]
    let renames = Collections.Generic.Dictionary<string, string>(dict [ "conversation", "chat" ])

    // Act
    let result = LayoutTree.RenameKinds(tree, renames)
    let kinds = LayoutTree.EnumerateSlots result |> Seq.map (fun s -> s.PanelKind) |> Seq.toList

    // Assert
    %kinds.Should().SequenceEqual([ "chat"; "git-log" ])

[<Fact>]
let ``renaming preserves every other slot field`` () =

    // Arrange
    let panelId = Guid.NewGuid()
    let tree = leafOf 0 [| { PanelId = panelId; PanelKind = "conversation"; Title = "Chat"; SavedState = "blob" } |]

    // Act
    let result = LayoutTree.RenameKinds(tree, Collections.Generic.Dictionary<string, string>(dict [ "conversation", "chat" ]))
    let only = LayoutTree.EnumerateSlots result |> Seq.exactlyOne

    // Assert
    %only.PanelId.Should().Be(panelId)
    %only.Title.Should().Be("Chat")
    %only.SavedState.Should().Be("blob")

[<Fact>]
let ``an empty rename map returns the tree untouched`` () =

    // Arrange
    let tree = leafOf 0 [| slot (Guid.NewGuid()) "conversation" |]

    // Act
    let result = LayoutTree.RenameKinds(tree, Collections.Generic.Dictionary<string, string>())

    // Assert
    %Object.ReferenceEquals(result, tree).Should().BeTrue()
