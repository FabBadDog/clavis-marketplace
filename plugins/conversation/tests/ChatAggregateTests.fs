module FabioSoft.Nucleus.Conversation.Tests.ChatAggregateTests

open System
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

// A two-chat state, both ready with an empty turn list, so a test can watch one chat while asserting the
// other is left strictly alone. Chats are constructed directly: creating them is Workspaces' job (WP5), and
// the aggregate deliberately exposes no add-a-chat operation yet.
let private ready (chat: Chat) =
    chat.WithLiveSession(fun session ->
        session.WithInitState(null).WithStatus(SessionStatus.Ready).WithTurns([||]))

let private twoChats () =
    let first = ready (Chat.Create(Guid.Empty, "C:\\first"))
    let second = ready (Chat.Create(Guid.Empty, "C:\\second"))
    let state = ConversationState(Chats = [| first; second |], VisibleChatId = Nullable first.ChatId)
    state, first, second

let private chatById chatId (state: ConversationState) =
    state.Chats |> Seq.find (fun chat -> chat.ChatId = chatId)

let private liveSessionId (chat: Chat) = chat.LiveSession.Id

let private runningTurn () = Turn(Prompt = "working", Status = Running())

[<Fact>]
let ``a stream event routes to the chat that owns the session`` () =

    // Arrange
    let state, _, second = twoChats ()

    // Act
    let struct (updated, _) =
        ConversationUpdate.HandleStreamEvent(state, AgentThinking(liveSessionId second, "pondering"))

    // Assert
    %(chatById second.ChatId updated).LiveSession.Status.Should().Be(SessionStatus.Thinking)

[<Fact>]
let ``a stream event leaves every other chat reference-equal`` () =

    // Arrange
    let state, first, second = twoChats ()

    // Act
    let struct (updated, _) =
        ConversationUpdate.HandleStreamEvent(state, AgentThinking(liveSessionId second, "pondering"))

    // Assert
    %Object.ReferenceEquals(chatById first.ChatId updated, first).Should().BeTrue()
    %Object.ReferenceEquals(chatById second.ChatId updated, second).Should().BeFalse()

[<Fact>]
let ``a stream event for an unknown session changes nothing`` () =

    // Arrange
    let state, _, _ = twoChats ()

    // Act
    let struct (updated, effects) = ConversationUpdate.HandleStreamEvent(state, AgentThinking(Guid.NewGuid(), "x"))

    // Assert
    %Object.ReferenceEquals(updated, state).Should().BeTrue()
    %effects.Should().BeEmpty()

[<Fact>]
let ``a restart ends and disposes only the target chat's live session`` () =

    // Arrange
    let state, first, second = twoChats ()
    let endedSessionId = liveSessionId first

    // Act
    let struct (updated, effects) = ConversationUpdate.HandleFullRestart state
    let restarted = chatById first.ChatId updated

    // Assert
    %restarted.Sessions.Count.Should().Be(2)
    %(restarted.Sessions |> Seq.find (fun s -> s.Id = endedSessionId)).Status.Should().Be(SessionStatus.Ended)
    %restarted.LiveSessionId.Should().NotBe(endedSessionId)
    %Object.ReferenceEquals(chatById second.ChatId updated, second).Should().BeTrue()
    %(effects |> Seq.exists (fun (e: ConversationEffect) ->
        match e with
        | :? DisposeSessionEffect as d -> d.SessionId = endedSessionId
        | _ -> false)).Should().BeTrue()

[<Fact>]
let ``a restart triggered by a background session restarts that chat, not the visible one`` () =

    // Arrange
    let state, first, second = twoChats ()

    // Act
    let struct (updated, _) = ConversationUpdate.HandleFullRestart(state, liveSessionId second)

    // Assert
    %(chatById second.ChatId updated).Sessions.Count.Should().Be(2)
    %Object.ReferenceEquals(chatById first.ChatId updated, first).Should().BeTrue()

[<Fact>]
let ``switching the visible chat mutates no chat`` () =

    // Arrange
    let state, first, second = twoChats ()

    // Act
    let switched = state.WithVisibleChatId(Nullable second.ChatId)

    // Assert
    %switched.VisibleChat.ChatId.Should().Be(second.ChatId)
    %Object.ReferenceEquals(chatById first.ChatId switched, first).Should().BeTrue()
    %Object.ReferenceEquals(chatById second.ChatId switched, second).Should().BeTrue()

[<Fact>]
let ``the tick refreshes a background chat with a running turn`` () =

    // Arrange
    let started = DateTime.UtcNow.AddSeconds(-5.0)
    let withTurn (chat: Chat) =
        chat.WithLiveSession(fun s -> s.WithTurns([| Turn(Prompt = "working", Status = Running(), StartedAt = started) |]))
    let first = ready (Chat.Create(Guid.Empty, ""))
    let second = withTurn (ready (Chat.Create(Guid.Empty, "")))
    let state = ConversationState(Chats = [| first; second |], VisibleChatId = Nullable first.ChatId)

    // Act
    let struct (ticked, _) = ConversationUpdate.HandleTick(state, DateTime.UtcNow)

    // Assert
    %(chatById second.ChatId ticked).LiveSession.Turns[0].Duration.Should().BeGreaterThan(TimeSpan.FromSeconds 4.0)

[<Fact>]
let ``the tick leaves a chat with nothing running reference-equal`` () =

    // Arrange
    let idle = ready (Chat.Create(Guid.Empty, ""))
    let busy = (ready (Chat.Create(Guid.Empty, ""))).WithLiveSession(fun s -> s.WithTurns([| runningTurn () |]))
    let state = ConversationState(Chats = [| idle; busy |], VisibleChatId = Nullable idle.ChatId)

    // Act
    let struct (ticked, _) = ConversationUpdate.HandleTick(state, DateTime.UtcNow)

    // Assert
    %Object.ReferenceEquals(chatById idle.ChatId ticked, idle).Should().BeTrue()
    %Object.ReferenceEquals(chatById busy.ChatId ticked, busy).Should().BeFalse()

[<Fact>]
let ``a chat's session history keeps every session with one live tail`` () =

    // Arrange
    let chat = ready (Chat.Create(Guid.Empty, ""))
    let replacement = SessionState.Create()

    // Act
    let restarted = chat.Restarted replacement

    // Assert
    %restarted.Sessions.Count.Should().Be(2)
    %restarted.LiveSessionId.Should().Be(replacement.Id)
    %restarted.LiveSession.Id.Should().Be(replacement.Id)

[<Fact>]
let ``every session of every chat is reachable for the activity stream`` () =

    // Arrange
    let state, first, second = twoChats ()
    let struct (restarted, _) = ConversationUpdate.HandleFullRestart state

    // Act
    let all = restarted.AllSessions |> Seq.toList

    // Assert
    %all.Length.Should().Be(3)
    %(all |> List.exists (fun s -> s.Id = liveSessionId second)).Should().BeTrue()
    %(all |> List.exists (fun s -> s.Id = liveSessionId first)).Should().BeTrue()

[<Fact>]
let ``a single-chat state still projects its active session`` () =

    // Arrange
    let state = ConversationState.Init()

    // Act & Assert - the regression guard: everything reading ActiveSession keeps working unchanged
    %state.ActiveSession.Should().NotBeNull()
    %state.ActiveSessionId.Should().Be(Nullable state.ActiveSession.Id)
    %state.VisibleChat.WorkingDirectory.Should().Be("")
