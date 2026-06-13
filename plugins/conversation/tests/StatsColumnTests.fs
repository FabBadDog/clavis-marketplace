module FabioSoft.Nucleus.Conversation.Tests.StatsColumnTests

open System
open FabioSoft.Nucleus.Plugins.Conversation
open FabioSoft.Nucleus.Plugins.Conversation.ViewModels
open Faqt
open Faqt.Operators
open Xunit

let private noPermission = Action<string, string>(fun _ _ -> ())

[<Fact>]
let ``stats column shows runtime and tokens for an interaction turn`` () =

    // Arrange
    TurnViewModel.StatsTemplate <- StatusLineTemplates.DefaultStatsColumn
    let turn = Turn(Kind = TurnKind.Interaction, Duration = TimeSpan.FromSeconds 5.0, TotalTokens = 1200)

    // Act
    let viewModel = TurnViewModel(turn, noPermission)

    // Assert
    %viewModel.Stats.Count.Should().Be(2)
    %viewModel.Stats[0].Icon.Should().Be("clock")
    %viewModel.Stats[1].Icon.Should().Be("tokens")

[<Fact>]
let ``stats column omits the token stat on the init turn`` () =

    // Arrange
    TurnViewModel.StatsTemplate <- StatusLineTemplates.DefaultStatsColumn
    let turn = Turn(Kind = TurnKind.InitTurn, Duration = TimeSpan.FromSeconds 2.0)

    // Act
    let viewModel = TurnViewModel(turn, noPermission)

    // Assert
    %viewModel.Stats.Count.Should().Be(1)
    %viewModel.Stats[0].Icon.Should().Be("clock")

[<Fact>]
let ``stats column honors a custom template`` () =

    // Arrange
    TurnViewModel.StatsTemplate <- "{microstat(cost):turn.tokens}"
    let turn = Turn(Kind = TurnKind.Interaction, TotalTokens = 500)

    // Act
    let viewModel = TurnViewModel(turn, noPermission)

    // Assert
    TurnViewModel.StatsTemplate <- StatusLineTemplates.DefaultStatsColumn
    %viewModel.Stats.Count.Should().Be(1)
    %viewModel.Stats[0].Icon.Should().Be("cost")
