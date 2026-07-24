module FabioSoft.Nucleus.Conversation.Tests.SessionPhaseTests

open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

[<Theory>]
[<InlineData(SessionStatus.Thinking, "thinking")>]
[<InlineData(SessionStatus.Retrying, "retrying")>]
[<InlineData(SessionStatus.Compacting, "compacting")>]
let ``Whisper maps a working phase to its lowercase word`` (status: SessionStatus) (expected: string) =

    %SessionPhase.Whisper(status).Should().Be(expected)

[<Theory>]
[<InlineData(SessionStatus.Idle)>]
[<InlineData(SessionStatus.Ready)>]
[<InlineData(SessionStatus.Aborting)>]
[<InlineData(SessionStatus.Ended)>]
[<InlineData(SessionStatus.Aborted)>]
let ``Whisper is empty for a non-working phase`` (status: SessionStatus) =

    %SessionPhase.Whisper(status).Should().Be("")
