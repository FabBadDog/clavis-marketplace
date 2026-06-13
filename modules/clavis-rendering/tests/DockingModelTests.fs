module FabioSoft.Clavis.Rendering.Tests.DockingModelTests

open System
open FabioSoft.Clavis.Rendering
open Faqt
open Faqt.Operators
open Xunit

let private gid () = Guid.NewGuid()

let private slot id =
    { PanelId = id; PanelKind = "k"; Title = "t"; SavedState = "" }

[<Fact>]
let ``addPanel into the active group appends a slot and activates it`` () =

    // Arrange
    let groupId = gid ()
    let root = DockingModel.leaf groupId [||] 0
    let panelId = gid ()

    // Act
    let result = DockingModel.addPanel DockTarget.IntoActiveGroup groupId (gid ()) (slot panelId) root

    // Assert
    %result.Panels.Length.Should().Be(1)
    %result.Panels[0].PanelId.Should().Be(panelId)
    %result.ActiveIndex.Should().Be(0)

[<Fact>]
let ``splitting a leaf to the right yields a horizontal split with the original first`` () =

    // Arrange
    let groupId = gid ()
    let root = DockingModel.leaf groupId [| slot (gid ()) |] 0
    let newGroup = gid ()
    let target = DockTarget.SplitGroup(groupId, DockDirection.Right, 0.3)

    // Act
    let result = DockingModel.addPanel target groupId newGroup (slot (gid ())) root

    // Assert
    %(DockingModel.isLeaf result).Should().BeFalse()
    %result.Orientation.Should().Be(DockingModel.Horizontal)
    %result.Children.Length.Should().Be(2)
    %result.Children[0].GroupId.Should().Be(groupId)
    %result.Children[1].GroupId.Should().Be(newGroup)
    %result.Sizes[1].Should().Be(0.3)

[<Fact>]
let ``splitting a leaf to the top puts the new leaf first in a vertical split`` () =

    // Arrange
    let groupId = gid ()
    let root = DockingModel.leaf groupId [| slot (gid ()) |] 0
    let newGroup = gid ()
    let target = DockTarget.SplitGroup(groupId, DockDirection.Top, 0.25)

    // Act
    let result = DockingModel.addPanel target groupId newGroup (slot (gid ())) root

    // Assert
    %result.Orientation.Should().Be(DockingModel.Vertical)
    %result.Children[0].GroupId.Should().Be(newGroup)
    %result.Sizes[0].Should().Be(0.25)

[<Fact>]
let ``removing one of two tabs keeps the group and clamps the active index`` () =

    // Arrange
    let groupId = gid ()
    let first = gid ()
    let second = gid ()
    let root = DockingModel.leaf groupId [| slot first; slot second |] 1

    // Act
    let result = DockingModel.removePanel first root

    // Assert
    %result.Panels.Length.Should().Be(1)
    %result.Panels[0].PanelId.Should().Be(second)
    %result.ActiveIndex.Should().Be(0)

[<Fact>]
let ``removing the only panel in a split child collapses the split to the sibling`` () =

    // Arrange
    let groupOne = gid ()
    let groupTwo = gid ()
    let panelOne = gid ()
    let panelTwo = gid ()
    let root =
        DockingModel.split (gid ()) DockingModel.Horizontal [| 0.5; 0.5 |]
            [| DockingModel.leaf groupOne [| slot panelOne |] 0
               DockingModel.leaf groupTwo [| slot panelTwo |] 0 |]

    // Act
    let result = DockingModel.removePanel panelOne root

    // Assert
    %(DockingModel.isLeaf result).Should().BeTrue()
    %result.GroupId.Should().Be(groupTwo)
    %result.Panels[0].PanelId.Should().Be(panelTwo)

[<Fact>]
let ``removing the last panel leaves an empty leaf`` () =

    // Arrange
    let groupId = gid ()
    let panelId = gid ()
    let root = DockingModel.leaf groupId [| slot panelId |] 0

    // Act
    let result = DockingModel.removePanel panelId root

    // Assert
    %(DockingModel.isLeaf result).Should().BeTrue()
    %result.Panels.Length.Should().Be(0)

[<Fact>]
let ``setActiveIndex updates the matching group`` () =

    // Arrange
    let groupId = gid ()
    let root = DockingModel.leaf groupId [| slot (gid ()); slot (gid ()) |] 0

    // Act
    let result = DockingModel.setActiveIndex groupId 1 root

    // Assert
    %result.ActiveIndex.Should().Be(1)

[<Fact>]
let ``setSizes updates the matching split`` () =

    // Arrange
    let splitId = gid ()
    let root =
        DockingModel.split splitId DockingModel.Horizontal [| 0.5; 0.5 |]
            [| DockingModel.leaf (gid ()) [||] 0; DockingModel.leaf (gid ()) [||] 0 |]

    // Act
    let result = DockingModel.setSizes splitId [| 0.7; 0.3 |] root

    // Assert
    %result.Sizes[0].Should().Be(0.7)

[<Fact>]
let ``groupContaining finds the leaf holding a panel`` () =

    // Arrange
    let groupTwo = gid ()
    let panelId = gid ()
    let root =
        DockingModel.split (gid ()) DockingModel.Vertical [| 0.5; 0.5 |]
            [| DockingModel.leaf (gid ()) [||] 0
               DockingModel.leaf groupTwo [| slot panelId |] 0 |]

    // Act
    let found = DockingModel.groupContaining panelId root

    // Assert
    %(found = Some groupTwo).Should().BeTrue()

[<Fact>]
let ``findSlot returns the slot for a panel anywhere in the tree`` () =

    // Arrange
    let panelId = gid ()
    let root =
        DockingModel.split (gid ()) DockingModel.Vertical [| 0.5; 0.5 |]
            [| DockingModel.leaf (gid ()) [||] 0
               DockingModel.leaf (gid ()) [| slot panelId |] 0 |]

    // Act
    let found = DockingModel.findSlot panelId root

    // Assert
    %(found |> Option.map (fun slot -> slot.PanelId) = Some panelId).Should().BeTrue()

[<Fact>]
let ``slots enumerates every panel across the tree`` () =

    // Arrange
    let root =
        DockingModel.split (gid ()) DockingModel.Horizontal [| 0.5; 0.5 |]
            [| DockingModel.leaf (gid ()) [| slot (gid ()) |] 0
               DockingModel.leaf (gid ()) [| slot (gid ()); slot (gid ()) |] 0 |]

    // Act
    let count = DockingModel.slots root |> List.length

    // Assert
    %count.Should().Be(3)
