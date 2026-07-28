module FabioSoft.Claude.Tests.AgentInstancesTests

open System
open FabioSoft.Claude
open Faqt
open Faqt.Operators
open Xunit

let private row sessionId name =
    $"""{{"pid":1234,"id":"a","sessionId":"{sessionId}","name":"{name}","cwd":"C:/work","kind":"bg","status":"running","startedAt":"2026-07-27T10:00:00Z"}}"""

[<Fact>]
let ``a reported agent maps onto an instance`` () =

    // Act
    let single = row "abc-123" "Reviews"
    let instances = AgentInstances.parse $"[{single}]"

    // Assert
    %instances.Length.Should().Be(1)
    %instances[0].SessionId.Should().Be("abc-123")
    %instances[0].Name.Should().Be("Reviews")
    %instances[0].Status.Should().Be("running")
    %instances[0].StartedAt.Year.Should().Be(2026)

[<Fact>]
let ``a row without a session id is dropped, the rest survive`` () =

    // Arrange - an unusable row cannot be resumed or addressed, so surfacing it would offer a dead entry
    let good = row "good" "Fine"
    let json = $"""[{{"pid":1,"cwd":"C:/x","status":"running"}},{good}]"""

    // Act
    let instances = AgentInstances.parse json

    // Assert
    %instances.Length.Should().Be(1)
    %instances[0].SessionId.Should().Be("good")

[<Fact>]
let ``an absent name falls back to the working directory's last segment`` () =

    // Arrange
    let json = """[{"sessionId":"s1","cwd":"C:/Repos/clavis","status":"running"}]"""

    // Act
    let instances = AgentInstances.parse json

    // Assert - what the user would recognise anyway
    %instances[0].Name.Should().Be("clavis")

[<Fact>]
let ``an agent with no working directory at all still maps`` () =

    // Arrange - a cwd outside every workspace, or none reported
    let json = """[{"sessionId":"s1","status":"running"}]"""

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
let ``an already adopted instance cannot be adopted again`` () =

    // Arrange - two processes on one session id means two windows onto one transcript
    let taken = row "taken" "n"
    let instance = (AgentInstances.parse $"[{taken}]").Head

    // Act & Assert
    %(AgentInstances.canAdopt (Set.ofList [ "taken" ]) instance).Should().BeFalse()
    %(AgentInstances.canAdopt (Set.ofList [ "other" ]) instance).Should().BeTrue()

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
