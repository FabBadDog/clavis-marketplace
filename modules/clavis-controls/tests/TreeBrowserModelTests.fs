module FabioSoft.Clavis.Controls.Tests.TreeBrowserModelTests

open FabioSoft.Clavis.Controls
open Faqt
open Faqt.Operators
open Xunit

type private FakeNode(isLeaf: bool) =
    interface ITreeNode with
        member _.IsLeaf = isLeaf

[<Fact>]
let ``a leaf selection is activatable`` () =

    // Arrange
    let node = FakeNode(true)

    // Act
    let result = TreeBrowserModel.activatable node

    // Assert
    %result.Should().BeSome()

[<Fact>]
let ``a non-leaf selection is not activatable`` () =

    // Arrange
    let node = FakeNode(false)

    // Act
    let result = TreeBrowserModel.activatable node

    // Assert
    %result.Should().BeNone()

[<Fact>]
let ``a selection that is not a tree node is not activatable`` () =

    // Act
    let result = TreeBrowserModel.activatable (box "not a node")

    // Assert
    %result.Should().BeNone()

[<Fact>]
let ``a null selection is not activatable`` () =

    // Act
    let result = TreeBrowserModel.activatable null

    // Assert
    %result.Should().BeNone()
