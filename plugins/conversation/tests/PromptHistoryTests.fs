module FabioSoft.Nucleus.Conversation.Tests.PromptHistoryTests

open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

let private withEntries entries =
    entries |> List.fold (fun (history: PromptHistory) entry -> history.Added entry) PromptHistory.Empty

[<Fact>]
let ``up on an empty history recalls nothing`` () =

    // Act
    let struct (history, text) = PromptHistory.Empty.Up "draft"

    // Assert
    %text.Should().BeNull()
    %history.Index.Should().Be(-1)

[<Fact>]
let ``up lands on the newest entry and stashes the draft`` () =

    // Arrange
    let history = withEntries [ "first"; "second" ]

    // Act
    let struct (stepped, text) = history.Up "in progress"

    // Assert
    %text.Should().Be("second")
    %stepped.Draft.Should().Be("in progress")

[<Fact>]
let ``repeated up walks towards the oldest and stops there`` () =

    // Arrange
    let struct (history, _) = (withEntries [ "first"; "second" ]).Up ""

    // Act
    let struct (atOldest, first) = history.Up ""
    let struct (_, stillOldest) = atOldest.Up ""

    // Assert
    %first.Should().Be("first")
    %stillOldest.Should().Be("first")

[<Fact>]
let ``down past the newest restores the stashed draft and leaves recall`` () =

    // Arrange
    let struct (recalling, _) = (withEntries [ "only" ]).Up "in progress"

    // Act
    let struct (left, text) = recalling.Down()

    // Assert
    %text.Should().Be("in progress")
    %left.Index.Should().Be(-1)

[<Fact>]
let ``down while not recalling does nothing`` () =

    // Act
    let struct (history, text) = (withEntries [ "only" ]).Down()

    // Assert
    %text.Should().BeNull()
    %history.Index.Should().Be(-1)

[<Fact>]
let ``a submitted prompt is appended and leaves recall`` () =

    // Arrange
    let struct (recalling, _) = (withEntries [ "first" ]).Up "in progress"

    // Act
    let history = recalling.Added "second"
    let struct (_, recalled) = history.Up ""

    // Assert
    %history.Entries.Count.Should().Be(2)
    %history.Index.Should().Be(-1)
    %history.Draft.Should().Be("")
    %recalled.Should().Be("second")
