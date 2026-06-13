module FabioSoft.Clavis.Rendering.Tests.SegmentedSelectorModelTests

open System.Collections.Generic
open FabioSoft.Clavis.Rendering
open Faqt
open Faqt.Operators
open Xunit

let private build () =
    let options = [| SegmentItem("A", ""); SegmentItem("B", ""); SegmentItem("C", "") |]
    SegmentedSelectorModel(options :> IReadOnlyList<SegmentItem>), options

[<Fact>]
let ``starts with nothing selected`` () =

    // Arrange
    let model, _ = build ()

    // Act & Assert
    %model.SelectedIndex.Should().Be(-1)

[<Fact>]
let ``selecting an index highlights only that item`` () =

    // Arrange
    let model, options = build ()

    // Act
    model.SelectedIndex <- 1

    // Assert
    %options[0].IsSelected.Should().BeFalse()
    %options[1].IsSelected.Should().BeTrue()
    %options[2].IsSelected.Should().BeFalse()

[<Fact>]
let ``SelectedIndex clamps above the range`` () =

    // Arrange
    let model, _ = build ()

    // Act
    model.SelectedIndex <- 9

    // Assert
    %model.SelectedIndex.Should().Be(2)

[<Fact>]
let ``MoveSelection clamps at both bounds`` () =

    // Arrange
    let model, _ = build ()

    // Act
    model.SelectedIndex <- 0
    model.MoveSelection(-1)
    let low = model.SelectedIndex
    model.SelectedIndex <- 2
    model.MoveSelection(1)
    let high = model.SelectedIndex

    // Assert
    %low.Should().Be(0)
    %high.Should().Be(2)

[<Fact>]
let ``SelectionChanged fires only when the index moves`` () =

    // Arrange
    let model, _ = build ()
    let mutable changes = 0
    model.SelectionChanged.Add(fun _ -> changes <- changes + 1)

    // Act
    model.SelectedIndex <- 1
    model.SelectedIndex <- 1

    // Assert
    %changes.Should().Be(1)

[<Fact>]
let ``Choose commits even when the index is unchanged`` () =

    // Arrange
    let model, _ = build ()
    model.SelectedIndex <- 1
    let mutable committed = -1
    model.Committed.Add(fun index -> committed <- index)

    // Act
    model.Choose(1)

    // Assert
    %committed.Should().Be(1)
