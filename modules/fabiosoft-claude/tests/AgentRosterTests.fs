module FabioSoft.Claude.Tests.AgentRosterTests

open FabioSoft.Claude
open Faqt
open Faqt.Operators
open Xunit

/// Shaped after the real daemon roster, including the fields Clavis ignores, so a parser that is accidentally
/// strict about unknown keys fails here rather than in front of a user.
let private roster = """
{
  "proto": 1,
  "supervisorPid": 87664,
  "updatedAt": 1785231905598,
  "workers": {
    "28eb5d66": {
      "pid": 117036,
      "sessionId": "28eb5d66-df76-418a-b5fa-d7e968638af5",
      "rendezvousSock": "\\\\.\\pipe\\cc-daemon-abc-rv-28eb5d66",
      "ptySock": "\\\\.\\pipe\\cc-daemon-abc-pty-28eb5d66",
      "cliVersion": "2.1.220",
      "attempt": 2,
      "cwd": "C:\\Users\\someone",
      "decModes": [1004, 1000],
      "dispatch": {
        "proto": 1,
        "short": "28eb5d66",
        "source": "fleet",
        "seed": { "intent": "tackle BL-Item 17554" }
      }
    }
  }
}
"""

[<Fact>]
let ``a worker maps onto its process and pipes`` () =

    // Act
    let workers = AgentRoster.parse roster

    // Assert
    %workers.Length.Should().Be(1)
    %workers[0].SessionId.Should().Be("28eb5d66-df76-418a-b5fa-d7e968638af5")
    %workers[0].ProcessId.Should().Be(117036)
    %workers[0].Short.Should().Be("28eb5d66")
    %workers[0].Intent.Should().Be("tackle BL-Item 17554")
    %workers[0].PtySocket.Should().Contain("pty-28eb5d66")

[<Fact>]
let ``a newer daemon layout is declined rather than guessed at`` () =

    // Arrange - the whole point of the proto gate: an unrecognised roster must degrade to the documented
    // listing, not be parsed on the assumption that the fields still mean what they used to
    let future = roster.Replace("\"proto\": 1,", "\"proto\": 2,")

    // Act & Assert
    %(AgentRoster.parse future).Should().BeEmpty()

[<Fact>]
let ``a roster with no proto at all is declined`` () =

    // Act & Assert
    %(AgentRoster.parse """{"workers":{"a":{"pid":1,"sessionId":"s"}}}""").Should().BeEmpty()

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("not json")>]
[<InlineData("{}")>]
[<InlineData("{\"proto\":1}")>]
let ``an unreadable or empty roster yields no workers`` (document: string) =

    // Act & Assert - a missing daemon is normal, not a failure
    %(AgentRoster.parse document).Should().BeEmpty()

[<Fact>]
let ``a worker without a pid or session id is dropped`` () =

    // Arrange - it cannot be identified, so it cannot be handed over
    let document = """{"proto":1,"workers":{"a":{"pid":1},"b":{"sessionId":"s"},"c":{"pid":2,"sessionId":"good"}}}"""

    // Act
    let workers = AgentRoster.parse document

    // Assert
    %workers.Length.Should().Be(1)
    %workers[0].SessionId.Should().Be("good")

[<Fact>]
let ``a worker with no dispatched prompt has no intent`` () =

    // Act
    let workers = AgentRoster.parse """{"proto":1,"workers":{"a":{"pid":1,"sessionId":"s"}}}"""

    // Assert
    %workers[0].Intent.Should().Be("")

[<Fact>]
let ``a session is matched to its worker regardless of id casing`` () =

    // Arrange
    let workers = AgentRoster.parse roster

    // Act & Assert
    %(AgentRoster.forSession "28EB5D66-DF76-418A-B5FA-D7E968638AF5" workers).Should().BeSome()
    %(AgentRoster.forSession "unknown" workers).Should().BeNone()

[<Fact>]
let ``the roster sits under the daemon folder of a Claude home`` () =

    // Act & Assert - the daemon is per home, so the path is derived from one rather than assumed global
    %(AgentRoster.pathIn "C:/Users/someone/.claude").Should().EndWith("daemon\\roster.json")
