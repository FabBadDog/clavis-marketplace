module FabioSoft.Nucleus.Conversation.Tests.ChatPanelStateTests

open System
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``round-trips the workspace and chat it is bound to`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()
    let chatId = Guid.NewGuid()

    // Act
    let restored = ChatPanelState(workspaceId, chatId).Serialize() |> ChatPanelState.Parse

    // Assert
    %restored.WorkspaceId.Should().Be(workspaceId)
    %restored.ChatId.Should().Be(chatId)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData(null)>]
[<InlineData("{\"workspaceId\":")>]
[<InlineData("not json at all")>]
[<InlineData("[1,2,3]")>]
[<InlineData("{\"workspaceId\":\"nope\",\"chatId\":42}")>]
let ``an unreadable blob yields a fresh chat rather than throwing`` (raw: string) =

    // Act
    let parsed = ChatPanelState.Parse raw

    // Assert
    %parsed.Should().Be(ChatPanelState.Empty)

[<Fact>]
let ``a blob missing one field keeps the other`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()

    // Act
    let parsed = ChatPanelState.Parse $"{{\"workspaceId\":\"{workspaceId}\"}}"

    // Assert
    %parsed.WorkspaceId.Should().Be(workspaceId)
    %parsed.ChatId.Should().Be(Guid.Empty)
