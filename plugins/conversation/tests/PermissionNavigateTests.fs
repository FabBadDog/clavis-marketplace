module FabioSoft.Nucleus.Conversation.Tests.PermissionNavigateTests

open System
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

let private session (state: ConversationState) =
    state.ActiveSession |> Option.ofObj |> Option.get

let private selectedIndex state =
    (session state).Turns
    |> Seq.collect (fun turn -> turn.Items)
    |> Seq.pick (function
        | :? PermissionItem as item -> Some item.Permission.SelectedIndex
        | _ -> None)

let private permissionState selectedIndex isResolved =
    let permission = Permission(RequestId = "req-1", SelectedIndex = selectedIndex, IsResolved = isResolved)
    let turn = Turn(Prompt = "p", Items = [| PermissionItem(permission) :> TurnItem |])
    ConversationState.Init().WithActiveSession(fun s -> s.WithTurns([| turn |]))

[<Theory>]
[<InlineData(0, 1, 1)>] // Right: ALLOW -> DENY
[<InlineData(1, 1, 2)>] // Right: DENY -> ALWAYS ALLOW
[<InlineData(2, 1, 2)>] // Right clamps at ALWAYS ALLOW
[<InlineData(2, -1, 1)>] // Left: ALWAYS ALLOW -> DENY
[<InlineData(0, -1, 0)>] // Left clamps at ALLOW
let ``HandlePermissionNavigate moves the selection within bounds`` (start, delta, expected) =

    // Arrange
    let state = permissionState start false

    // Act
    let struct (newState, _) = ConversationUpdate.HandlePermissionNavigate(state, delta)

    // Assert
    %(selectedIndex newState).Should().Be(expected)

[<Fact>]
let ``HandlePermissionNavigate is a no-op when no permission is pending`` () =

    // Arrange
    let state = ConversationState.Init()

    // Act
    let struct (newState, effects) = ConversationUpdate.HandlePermissionNavigate(state, 1)

    // Assert
    %Object.ReferenceEquals(newState, state).Should().BeTrue()
    %effects.Length.Should().Be(0)

[<Fact>]
let ``HandlePermissionNavigate ignores a resolved permission`` () =

    // Arrange
    let state = permissionState 0 true

    // Act
    let struct (newState, _) = ConversationUpdate.HandlePermissionNavigate(state, 1)

    // Assert
    %(selectedIndex newState).Should().Be(0)

[<Theory>]
[<InlineData(0, "allow")>]
[<InlineData(1, "deny")>]
[<InlineData(2, "allow_always")>]
let ``PermissionDecisionAt maps the choice index to a decision`` (index, expected) =
    %ConversationUpdate.PermissionDecisionAt(index).Should().Be(expected)

[<Theory>]
[<InlineData(0, true)>] // ALLOW -> allow
[<InlineData(1, false)>] // DENY -> deny
[<InlineData(2, true)>] // ALWAYS ALLOW -> allow
let ``HandlePermissionConfirm resolves at the highlighted choice`` (start, expectedAllow) =

    // Arrange
    let state = permissionState start false

    // Act
    let struct (_, effects) = ConversationUpdate.HandlePermissionConfirm(state)

    // Assert
    %effects.Length.Should().Be(1)
    %(effects[0] :?> SendPermissionResponseEffect).Allow.Should().Be(expectedAllow)

[<Fact>]
let ``HasPendingPermission reflects an unresolved permission`` () =

    // Act / Assert
    %ConversationUpdate.HasPendingPermission(permissionState 0 false).Should().BeTrue()
    %ConversationUpdate.HasPendingPermission(permissionState 0 true).Should().BeFalse()
    %ConversationUpdate.HasPendingPermission(ConversationState.Init()).Should().BeFalse()
