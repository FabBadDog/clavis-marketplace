module FabioSoft.Nucleus.CommandPalette.Tests.ActivationTests

open System
open System.Collections.Generic
open FabioSoft.Nucleus.Contracts
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.CommandPalette
open Faqt
open Faqt.Operators
open Xunit

let private noPositional : IReadOnlyList<string> = Array.empty<string> :> _
let private positional (values: string list) : IReadOnlyList<string> = List.toArray values :> _
let private noNamed : IReadOnlyDictionary<string, string> = readOnlyDict []
let private named (pairs: (string * string) list) : IReadOnlyDictionary<string, string> = readOnlyDict pairs

[<Fact>]
let ``activate builds an F# record from named arguments`` () =

    // Act
    let outcome =
        MessageActivator.Activate(
            typeof<LogEntry>,
            noPositional,
            named [ "Level", "Debug"; "Source", "s"; "Message", "m"; "Timestamp", "2020-01-01T00:00:00+00:00" ])

    // Assert
    %outcome.IsSuccess.Should().BeTrue()
    let entry = outcome.Value :?> LogEntry
    %entry.Level.Should().Be(LogLevel.Debug)
    %entry.Source.Should().Be("s")
    %entry.Message.Should().Be("m")

[<Fact>]
let ``activate fills record fields positionally in declaration order`` () =

    // Act
    let outcome =
        MessageActivator.Activate(
            typeof<LogEntry>,
            positional [ "Info"; "src"; "msg"; "2020-01-01T00:00:00+00:00" ],
            noNamed)

    // Assert
    let entry = outcome.Value :?> LogEntry
    %entry.Level.Should().Be(LogLevel.Info)
    %entry.Message.Should().Be("msg")

[<Fact>]
let ``activate parses enum case-insensitively`` () =

    // Act
    let outcome =
        MessageActivator.Activate(
            typeof<LogEntry>,
            noPositional,
            named [ "Level", "warn"; "Source", "s"; "Message", "m"; "Timestamp", "2020-01-01T00:00:00Z" ])

    // Assert
    %(outcome.Value :?> LogEntry).Level.Should().Be(LogLevel.Warn)

[<Fact>]
let ``activate reports an unknown record field`` () =

    // Act
    let outcome = MessageActivator.Activate(typeof<LogEntry>, noPositional, named [ "Nope", "x" ])

    // Assert
    %outcome.IsSuccess.Should().BeFalse()
    %outcome.Error.Should().Contain("Nope")

[<Fact>]
let ``activate reports a missing record field`` () =

    // Act
    let outcome = MessageActivator.Activate(typeof<LogEntry>, noPositional, named [ "Level", "Info" ])

    // Assert
    %outcome.IsSuccess.Should().BeFalse()

[<Fact>]
let ``activate reports an unconvertible value`` () =

    // Act
    let outcome =
        MessageActivator.Activate(
            typeof<LogEntry>,
            noPositional,
            named [ "Level", "NotALevel"; "Source", "s"; "Message", "m"; "Timestamp", "2020-01-01T00:00:00Z" ])

    // Assert
    %outcome.IsSuccess.Should().BeFalse()

[<Fact>]
let ``activate builds a class via its constructor with named arguments`` () =

    // Arrange
    let sessionId = Guid.NewGuid()

    // Act
    let outcome =
        MessageActivator.Activate(
            typeof<SendPrompt>,
            noPositional,
            named [ "sessionId", string sessionId; "text", "hello" ])

    // Assert
    let prompt = outcome.Value :?> SendPrompt
    %prompt.SessionId.Should().Be(sessionId)
    %prompt.Text.Should().Be("hello")

[<Fact>]
let ``activate builds a parameterless class`` () =

    // Act
    let outcome = MessageActivator.Activate(typeof<ApplicationShutdown>, noPositional, noNamed)

    // Assert
    %outcome.IsSuccess.Should().BeTrue()
    %(outcome.Value :? ApplicationShutdown).Should().BeTrue()

[<Fact>]
let ``activate reports too many positional arguments`` () =

    // Act
    let outcome = MessageActivator.Activate(typeof<ApplicationShutdown>, positional [ "extra" ], noNamed)

    // Assert
    %outcome.IsSuccess.Should().BeFalse()
