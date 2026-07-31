module FabioSoft.Nucleus.Conversation.Tests.ChatViewModelsTests

open System
open FabioSoft.Nucleus.Plugins.Conversation
open FabioSoft.Nucleus.Plugins.Conversation.ViewModels
open Faqt
open Faqt.Operators
open Xunit

let private views () = ChatViewModels(fun _ _ -> ())

let private stateOf (chats: Chat list) =
    ConversationState(Chats = Array.ofList chats)

/// A panel resolves before its workspace has a chat: a workspace's session is obtained asynchronously, so
/// this is the ordinary case on a switch, not a rare one.
let private waitingPanel (viewModels: ChatViewModels) workspaceId =
    viewModels.ForChat(null, Guid.Empty, workspaceId)

[<Fact>]
let ``two workspaces waiting for their chats get separate view models`` () =

    // Arrange
    let viewModels = views ()

    // Act - both panels exist before either chat does
    let first = waitingPanel viewModels (Guid.NewGuid())
    let second = waitingPanel viewModels (Guid.NewGuid())

    // Assert - one shared placeholder would show the same conversation in every workspace
    %Object.ReferenceEquals(first, second).Should().BeFalse()

[<Fact>]
let ``the same workspace asking twice gets the same view model`` () =

    // Arrange
    let viewModels = views ()
    let workspaceId = Guid.NewGuid()

    // Act & Assert
    %Object.ReferenceEquals(waitingPanel viewModels workspaceId, waitingPanel viewModels workspaceId)
        .Should().BeTrue()

[<Fact>]
let ``a chat adopts the panel of its own workspace, not another's`` () =

    // Arrange
    let mine = Guid.NewGuid()
    let theirs = Guid.NewGuid()
    let viewModels = views ()
    let waitingForMine = waitingPanel viewModels mine
    let waitingForTheirs = waitingPanel viewModels theirs

    // Act - only my workspace's chat comes into being
    let chat = Chat.Create(mine, "C:\\mine")
    viewModels.Project(stateOf [], stateOf [ chat ])

    // Assert - my panel is now projecting that chat; theirs is still waiting for its own
    %Object.ReferenceEquals(viewModels.ForChat(chat, chat.ChatId, mine), waitingForMine).Should().BeTrue()
    %Object.ReferenceEquals(waitingPanel viewModels theirs, waitingForTheirs).Should().BeTrue()

[<Fact>]
let ``an adopted panel is projected on later changes`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()
    let viewModels = views ()
    let panel = waitingPanel viewModels workspaceId
    let chat = Chat.Create(workspaceId, "C:\\here")

    // Act
    viewModels.Project(stateOf [], stateOf [ chat ])

    // Assert - a panel that is never adopted stays blank for ever behind a view model nothing projects onto
    %panel.ChatId.Should().Be(Nullable chat.ChatId)

[<Fact>]
let ``a panel that names no workspace is adopted by the visible chat`` () =

    // Arrange - what a panel created before workspaces existed looks like
    let viewModels = views ()
    let panel = waitingPanel viewModels Guid.Empty
    let chat = Chat.Create(Guid.NewGuid(), "C:\\anywhere")
    let after = ConversationState(Chats = [| chat |], VisibleChatId = Nullable chat.ChatId)

    // Act
    viewModels.Project(stateOf [], after)

    // Assert
    %panel.ChatId.Should().Be(Nullable chat.ChatId)
