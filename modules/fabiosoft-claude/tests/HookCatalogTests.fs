module FabioSoft.Claude.Tests.HookCatalogTests

open FabioSoft.Claude
open Faqt
open Faqt.Operators
open Xunit

[<Theory>]
[<InlineData("pwsh ./scripts/validate-command.ps1 --strict", "validate-command")>]
[<InlineData("python hook.py", "hook")>]
[<InlineData("my-hook", "my-hook")>]
[<InlineData("", "")>]
let ``deriveBasename extracts the script name`` (command: string, expected: string) =
    // Act / Assert
    %HookCatalog.deriveBasename(command).Should().Be(expected)

[<Fact>]
let ``parse maps each hook event to its script basenames`` () =
    // Arrange
    let json =
        """{"hooks":{"SessionStart":[{"hooks":[{"command":"pwsh ./session-start.ps1"}]}]}}"""

    // Act
    let catalog = HookCatalog.parse json

    // Assert
    %catalog.Should().ContainKey("SessionStart")
    %catalog["SessionStart"].Should().SequenceEqual([ "session-start" ])

[<Fact>]
let ``parse deduplicates repeated script names across matchers`` () =
    // Arrange
    let json =
        """{"hooks":{"PreToolUse":[{"hooks":[{"command":"a.ps1"},{"command":"a.ps1"},{"command":"b.ps1"}]}]}}"""

    // Act
    let catalog = HookCatalog.parse json

    // Assert
    %catalog["PreToolUse"].Should().SequenceEqual([ "a"; "b" ])

[<Fact>]
let ``parse returns empty for malformed json`` () =
    // Act / Assert
    %HookCatalog.parse("not json").Count.Should().Be(0)

[<Fact>]
let ``parse ignores events with no resolvable commands`` () =
    // Act
    let catalog = HookCatalog.parse """{"hooks":{"PreToolUse":[{"hooks":[{"other":"x"}]}]}}"""

    // Assert
    %catalog.Count.Should().Be(0)

[<Fact>]
let ``resolveDisplayName returns the script name at the firing index`` () =
    // Arrange
    let catalog = Map.ofList [ "SessionStart", [ "first"; "second" ] ]

    // Act / Assert
    %(HookCatalog.resolveDisplayName catalog "SessionStart" 1).Should().Be("second")

[<Fact>]
let ``resolveDisplayName falls back to the event name when the event is unknown`` () =
    // Act / Assert
    %(HookCatalog.resolveDisplayName Map.empty "PreToolUse" 0).Should().Be("PreToolUse")

[<Fact>]
let ``resolveDisplayName falls back to the event name when the index overflows`` () =
    // Arrange
    let catalog = Map.ofList [ "SessionStart", [ "only" ] ]

    // Act / Assert
    %(HookCatalog.resolveDisplayName catalog "SessionStart" 3).Should().Be("SessionStart")
