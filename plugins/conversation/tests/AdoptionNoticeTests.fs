module FabioSoft.Nucleus.Conversation.Tests.AdoptionNoticeTests

open System
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

let private since = DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero)

let private workspace workspaceId isAdopting =
    WorkspaceInfo(workspaceId, "Reviews", "Accent1Brush", "C:\\work", Guid.Empty, "idle", "", since, 1, false, isAdopting)

[<Fact>]
let ``a panel whose workspace is taking an agent over shows the notice`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()

    // Act
    let notice = AdoptionNotices.For(workspaceId, [| workspace workspaceId true |], workspaceId)

    // Assert
    %notice.IsVisible.Should().BeTrue()
    %notice.WorkspaceId.Should().Be(workspaceId)

[<Fact>]
let ``a panel whose workspace is not taking anything over shows nothing`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()

    // Act & Assert
    %AdoptionNotices.For(workspaceId, [| workspace workspaceId false |], workspaceId).IsVisible.Should().BeFalse()

[<Fact>]
let ``a panel bound to another workspace is unaffected by that one's take-over`` () =

    // Arrange - two chat panels side by side; only the one waiting should be covered
    let mine = Guid.NewGuid()
    let theirs = Guid.NewGuid()
    let workspaces = [| workspace mine false; workspace theirs true |]

    // Act & Assert
    %AdoptionNotices.For(mine, workspaces, theirs).IsVisible.Should().BeFalse()

[<Fact>]
let ``a panel with no workspace of its own follows the active one`` () =

    // Arrange - Guid.Empty in a saved panel blob means "whichever workspace is current", which is also every
    // panel saved before workspaces existed
    let active = Guid.NewGuid()

    // Act
    let notice = AdoptionNotices.For(Guid.Empty, [| workspace active true |], active)

    // Assert
    %notice.IsVisible.Should().BeTrue()
    %notice.WorkspaceId.Should().Be(active)

[<Fact>]
let ``a panel naming a workspace that is gone shows nothing`` () =

    // Act - a stale panel blob must not resolve onto whatever happens to be active
    let notice = AdoptionNotices.For(Guid.NewGuid(), [| workspace (Guid.NewGuid()) true |], Guid.NewGuid())

    // Assert
    %notice.IsVisible.Should().BeFalse()

[<Fact>]
let ``the notice says why it is waiting rather than only that it is`` () =

    // Act & Assert - the alternative on offer destroys a running turn, and nobody can weigh that untold
    %AdoptionNotices.Detail.Should().Contain("discard")
