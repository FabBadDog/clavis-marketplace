module FabioSoft.Claude.Tests.AgentInstancesTests

open System
open FabioSoft.Claude
open Faqt
open Faqt.Operators
open Xunit

/// Shaped after the real `claude agents --json` output, not after a guess: startedAt is epoch milliseconds,
/// kind is "background", status is "busy"/"waiting" rather than "running", and `id` is a short handle that is
/// unrelated to the session id (verified against the real listing, where a7683d47 addressed session 2b3bba05).
let private row sessionId name =
    $"""{{"pid":1234,"id":"a7683d47","sessionId":"{sessionId}","name":"{name}","cwd":"C:/work","kind":"background","status":"busy","startedAt":1784996640877,"state":"working"}}"""

let private owned sessionId label = row sessionId $"clavis/{label}"

[<Fact>]
let ``a reported agent maps onto an instance`` () =

    // Act
    let single = owned "abc-123" "Reviews"
    let instances = AgentInstances.parse $"[{single}]"

    // Assert
    %instances.Length.Should().Be(1)
    %instances[0].SessionId.Should().Be("abc-123")
    %instances[0].Name.Should().Be("Reviews")
    %instances[0].IsOwned.Should().BeTrue()
    %instances[0].Status.Should().Be("busy")

[<Fact>]
let ``startedAt is read as epoch milliseconds`` () =

    // Arrange - the CLI reports a number, not a timestamp string; reading it as a string silently dated every
    // instance to DateTimeOffset.MinValue
    let expected = DateTimeOffset.FromUnixTimeMilliseconds(1784996640877L)

    // Act
    let single = owned "s1" "n"
    let instances = AgentInstances.parse $"[{single}]"

    // Assert
    %instances[0].StartedAt.Should().Be(expected)

[<Fact>]
let ``startedAt still falls back to a timestamp string`` () =

    // Arrange - so a provider that switches to ISO does not date everything to the minimum value
    let json = """[{"sessionId":"s1","startedAt":"2026-07-27T10:00:00Z"}]"""

    // Act
    let instances = AgentInstances.parse json

    // Assert
    %instances[0].StartedAt.Year.Should().Be(2026)

[<Fact>]
let ``an unreadable startedAt dates to the minimum rather than throwing`` () =

    // Act
    let instances = AgentInstances.parse """[{"sessionId":"s1","startedAt":"not a date"}]"""

    // Assert
    %instances[0].StartedAt.Should().Be(DateTimeOffset.MinValue)

[<Fact>]
let ``a row without a session id is dropped, the rest survive`` () =

    // Arrange - an unusable row cannot be resumed or addressed, so surfacing it would offer a dead entry
    let good = owned "good" "Fine"
    let json = $"""[{{"pid":1,"cwd":"C:/x","status":"busy"}},{good}]"""

    // Act
    let instances = AgentInstances.parse json

    // Assert
    %instances.Length.Should().Be(1)
    %instances[0].SessionId.Should().Be("good")

[<Fact>]
let ``an absent name falls back to the working directory's last segment and is not owned`` () =

    // Arrange
    let json = """[{"sessionId":"s1","cwd":"C:/Repos/clavis","status":"busy"}]"""

    // Act
    let instances = AgentInstances.parse json

    // Assert - what the user would recognise anyway; ownership is something Clavis writes, so an unnamed
    // session can never be mistaken for one of ours
    %instances[0].Name.Should().Be("clavis")
    %instances[0].IsOwned.Should().BeFalse()

[<Fact>]
let ``a name Clavis did not write is kept as it is and is not owned`` () =

    // Arrange - somebody else's live session, listed alongside ours
    let foreign = row "s1" "API Contract"
    let json = $"[{foreign}]"

    // Act
    let instances = AgentInstances.parse json

    // Assert
    %instances[0].Name.Should().Be("API Contract")
    %instances[0].IsOwned.Should().BeFalse()

[<Fact>]
let ``an owned name with no label behind the marker falls back`` () =

    // Arrange
    let bare = row "s1" "clavis/"
    let json = $"[{bare}]"

    // Act
    let instances = AgentInstances.parse json

    // Assert
    %instances[0].Name.Should().Be("work")
    %instances[0].IsOwned.Should().BeTrue()

[<Fact>]
let ``an agent with no working directory at all still maps`` () =

    // Arrange - a cwd outside every workspace, or none reported
    let json = """[{"sessionId":"s1","status":"busy"}]"""

    // Act
    let instances = AgentInstances.parse json

    // Assert - the session id is the last honest fallback for a name
    %instances[0].Name.Should().Be("s1")
    %instances[0].WorkingDirectory.Should().Be("")

[<Fact>]
let ``an unreported status reads as unknown rather than empty`` () =

    // Act
    let instances = AgentInstances.parse """[{"sessionId":"s1"}]"""

    // Assert
    %instances[0].Status.Should().Be("unknown")

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData("not json")>]
[<InlineData("{\"sessionId\":\"s1\"}")>]
[<InlineData("[")>]
let ``output that is not an array of agents yields no instances`` (output: string) =

    // Act & Assert - a provider that changed its format degrades to "none known", it does not take us down
    %(AgentInstances.parse output).Should().BeEmpty()

[<Fact>]
let ``only Clavis's own agents are offered for reclaiming`` () =

    // Arrange - the listing carries every live session on the machine, including editors and terminals nobody
    // asked us to touch
    let foreign = row "foreign" "API Contract"
    let mine = owned "mine" "Reviews"
    let json = $"""[{foreign},{mine},{{"sessionId":"unnamed"}}]"""

    // Act
    let reclaimable = AgentInstances.parse json |> AgentInstances.reclaimable

    // Assert
    %reclaimable.Length.Should().Be(1)
    %reclaimable[0].SessionId.Should().Be("mine")

[<Fact>]
let ``an already adopted instance cannot be adopted again`` () =

    // Arrange - two processes on one session id means two windows onto one transcript
    let taken = owned "taken" "n"
    let instance = (AgentInstances.parse $"[{taken}]").Head

    // Act & Assert
    %(AgentInstances.canAdopt (Set.ofList [ "taken" ]) instance).Should().BeFalse()
    %(AgentInstances.canAdopt (Set.ofList [ "other" ]) instance).Should().BeTrue()

[<Fact>]
let ``an instance Clavis does not own cannot be adopted at all`` () =

    // Arrange - resuming somebody else's live session would hijack it
    let foreign = row "foreign" "API Contract"
    let instance = (AgentInstances.parse $"[{foreign}]").Head

    // Act & Assert
    %(AgentInstances.canAdopt Set.empty instance).Should().BeFalse()

[<Theory>]
[<InlineData("keep-running", true)>]
[<InlineData("Keep-Running", true)>]
[<InlineData("stop", false)>]
[<InlineData("", false)>]
[<InlineData("something-else", false)>]
let ``only an explicit keep-running leaves the agent alive`` (mode: string, expected: bool) =

    // Act & Assert - the safe default for an unrecognised mode is to stop, not to leave a detached process
    // behind that nobody is tracking
    %(AgentInstances.keepsRunning mode).Should().Be(expected)

[<Fact>]
let ``a session Clavis starts is named so it can be recognised again`` () =

    // Act & Assert
    %(AgentInstances.nameFor "Reviews").Should().Be("clavis/Reviews")
    %(AgentInstances.nameFor "  Reviews  ").Should().Be("clavis/Reviews")

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
let ``a nameless session still carries the ownership marker`` (label: string) =

    // Act & Assert - without the marker the agent would be unreclaimable, so the marker is not optional
    %(AgentInstances.nameFor label).Should().Be("clavis")

[<Fact>]
let ``an agent carries the short handle it is addressed by, distinct from its session id`` () =

    // Arrange - `claude stop` takes the short handle, and deriving it from the session id would be wrong: the
    // real listing addressed session 2b3bba05-... as a7683d47
    let single = owned "2b3bba05-b035-4d29-acf5-efa12f0dd652" "Clavis"

    // Act
    let instances = AgentInstances.parse $"[{single}]"

    // Assert
    %instances[0].AgentId.Should().Be("a7683d47")
    %instances[0].SessionId.Should().Be("2b3bba05-b035-4d29-acf5-efa12f0dd652")

[<Fact>]
let ``an agent with no reported handle cannot be addressed`` () =

    // Act - an empty handle is honest: the caller must not invent one to stop with
    let instances = AgentInstances.parse """[{"sessionId":"s1","name":"clavis/x"}]"""

    // Assert
    %instances[0].AgentId.Should().Be("")

[<Fact>]
let ``stopping an agent addresses it by its short handle`` () =

    // Act & Assert - the CLI refuses to resume a session while its agent runs, so adoption stops it first
    %(AgentInstances.stopArguments "a7683d47").Should().SequenceEqual([ "stop"; "a7683d47" ])

[<Fact>]
let ``handing a session back resumes it as a background agent`` () =

    // Act
    let arguments = AgentInstances.handOffArguments "abc-123" "clavis/Reviews"

    // Assert
    %arguments.Should().SequenceEqual([ "--bg"; "--resume"; "abc-123"; "-n"; "clavis/Reviews" ])

[<Fact>]
let ``handing back without a name omits the name flag`` () =

    // Act & Assert - an empty -n would be a provider argument error, not a nameless agent
    %(AgentInstances.handOffArguments "abc-123" "").Should().SequenceEqual([ "--bg"; "--resume"; "abc-123" ])
