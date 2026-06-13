module FabioSoft.Clavis.Rendering.Tests.SelectorHistoryTests

open FabioSoft.Clavis.Rendering
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``record appends an entry and leaves history mode`` () =

    // Act
    let state = SelectorHistory.empty |> SelectorHistory.record "first"

    // Assert
    %state.Entries.Should().Be([ "first" ])
    %state.InHistory.Should().BeFalse()
    %state.Index.Should().Be(-1)

[<Fact>]
let ``record collapses consecutive duplicates`` () =

    // Act
    let state =
        SelectorHistory.empty
        |> SelectorHistory.record "same"
        |> SelectorHistory.record "same"

    // Assert
    %state.Entries.Should().Be([ "same" ])

[<Fact>]
let ``record keeps non-consecutive duplicates`` () =

    // Act
    let state =
        SelectorHistory.empty
        |> SelectorHistory.record "a"
        |> SelectorHistory.record "b"
        |> SelectorHistory.record "a"

    // Assert
    %state.Entries.Should().Be([ "a"; "b"; "a" ])

[<Fact>]
let ``enter with no entries is a no-op`` () =

    // Act
    let state, text = SelectorHistory.empty |> SelectorHistory.enter "draft"

    // Assert
    %state.InHistory.Should().BeFalse()
    %text.Should().BeNone()

[<Fact>]
let ``enter recalls the newest entry and stashes the draft`` () =

    // Arrange
    let recorded =
        SelectorHistory.empty
        |> SelectorHistory.record "older"
        |> SelectorHistory.record "newest"

    // Act
    let state, text = recorded |> SelectorHistory.enter "my draft"

    // Assert
    %state.InHistory.Should().BeTrue()
    %state.Draft.Should().Be("my draft")
    %text.Should().BeSome().WhoseValue.Should().Be("newest")

[<Fact>]
let ``up steps towards older entries and stops at the oldest`` () =

    // Arrange
    let entered =
        SelectorHistory.empty
        |> SelectorHistory.record "older"
        |> SelectorHistory.record "newest"
        |> SelectorHistory.enter ""
        |> fst

    // Act
    let afterFirstUp, firstText = SelectorHistory.up entered
    let afterSecondUp, secondText = SelectorHistory.up afterFirstUp

    // Assert
    %firstText.Should().BeSome().WhoseValue.Should().Be("older")
    %secondText.Should().BeNone()
    %afterSecondUp.Index.Should().Be(0)

[<Fact>]
let ``down past the newest entry cancels back to the draft`` () =

    // Arrange
    let entered =
        SelectorHistory.empty
        |> SelectorHistory.record "only"
        |> SelectorHistory.enter "draft"
        |> fst

    // Act
    let state, text = SelectorHistory.down entered

    // Assert
    %state.InHistory.Should().BeFalse()
    %text.Should().BeSome().WhoseValue.Should().Be("draft")

[<Fact>]
let ``down steps towards newer entries while they exist`` () =

    // Arrange
    let atOldest =
        SelectorHistory.empty
        |> SelectorHistory.record "older"
        |> SelectorHistory.record "newest"
        |> SelectorHistory.enter ""
        |> fst
        |> SelectorHistory.up
        |> fst

    // Act
    let state, text = SelectorHistory.down atOldest

    // Assert
    %state.InHistory.Should().BeTrue()
    %text.Should().BeSome().WhoseValue.Should().Be("newest")

[<Fact>]
let ``down outside history mode is a no-op`` () =

    // Act
    let state, text = SelectorHistory.empty |> SelectorHistory.down

    // Assert
    %state.Should().Be(SelectorHistory.empty)
    %text.Should().BeNone()

[<Fact>]
let ``cancel restores the draft and leaves history mode`` () =

    // Arrange
    let entered =
        SelectorHistory.empty
        |> SelectorHistory.record "entry"
        |> SelectorHistory.enter "draft"
        |> fst

    // Act
    let state, draft = SelectorHistory.cancel entered

    // Assert
    %state.InHistory.Should().BeFalse()
    %draft.Should().Be("draft")

[<Fact>]
let ``commit leaves history mode keeping the entries`` () =

    // Arrange
    let entered =
        SelectorHistory.empty
        |> SelectorHistory.record "entry"
        |> SelectorHistory.enter "draft"
        |> fst

    // Act
    let state = SelectorHistory.commit entered

    // Assert
    %state.InHistory.Should().BeFalse()
    %state.Entries.Should().Be([ "entry" ])
