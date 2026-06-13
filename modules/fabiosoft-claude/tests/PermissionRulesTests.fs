module FabioSoft.Claude.Tests.PermissionRulesTests

open FabioSoft.Claude
open Faqt
open Faqt.Operators
open Xunit

let private noFiles : string -> string option = fun _ -> None

let private noScopes : (string * string) list = []

let private readFrom (files: Map<string, string>) path = Map.tryFind path files

let private askJson rules =
    let quoted = rules |> List.map (fun r -> $"\"{r}\"") |> String.concat ","
    $"{{\"permissions\":{{\"ask\":[{quoted}]}}}}"

[<Fact>]
let ``parseRule splits tool name and arguments`` () =
    // Act
    let rule = PermissionRules.parseRule "Bash(git status:*)"

    // Assert
    %rule.ToolName.Should().Be("Bash")
    %rule.Arguments.Should().BeSome().WhoseValue.Should().Be("git status:*")

[<Fact>]
let ``parseRule without parentheses has no arguments`` () =
    // Act
    let rule = PermissionRules.parseRule "Read"

    // Assert
    %rule.ToolName.Should().Be("Read")
    %rule.Arguments.Should().BeNone()

[<Fact>]
let ``parse extracts the ask rules`` () =
    // Act
    let rules = PermissionRules.parse (askJson [ "Bash(*)"; "Read" ])

    // Assert
    %rules.Length.Should().Be(2)

[<Fact>]
let ``parse returns empty for malformed json`` () =
    // Act / Assert
    %PermissionRules.parse("not json").Length.Should().Be(0)

[<Fact>]
let ``parse returns empty when permissions are absent`` () =
    // Act / Assert
    %PermissionRules.parse("""{"other":1}""").Length.Should().Be(0)

[<Fact>]
let ``findMatch returns the rule whose glob matches the input`` () =
    // Arrange
    let files = Map.ofList [ "user", askJson [ "Bash(git*)" ] ]
    let scopes = [ "user", "user" ]

    // Act
    let result = PermissionRules.findMatch (readFrom files) scopes "Bash" "git status"

    // Assert
    let matched = result.Should().BeSome().WhoseValue
    %matched.Pattern.Should().Be("Bash(git*)")
    %matched.Scope.Should().Be("user")

[<Fact>]
let ``findMatch falls back to a name-only rule when no glob matches`` () =
    // Arrange
    let files = Map.ofList [ "project", askJson [ "Bash(npm*)"; "Bash" ] ]
    let scopes = [ "project", "project" ]

    // Act
    let result = PermissionRules.findMatch (readFrom files) scopes "Bash" "rm -rf"

    // Assert
    %result.Should().BeSome().WhoseValue.Pattern.Should().Be("Bash")

[<Fact>]
let ``findMatch returns none when no rule mentions the tool`` () =
    // Arrange
    let files = Map.ofList [ "user", askJson [ "Read" ] ]

    // Act / Assert
    %(PermissionRules.findMatch (readFrom files) [ "user", "user" ] "Bash" "x").Should().BeNone()

[<Fact>]
let ``findMatch honours scope precedence across files`` () =
    // Arrange - both scopes define a matching rule; the first listed wins.
    let files =
        Map.ofList
            [ "enterprise", askJson [ "Bash(*)" ]
              "user", askJson [ "Bash(*)" ] ]
    let scopes = [ "enterprise", "enterprise"; "user", "user" ]

    // Act
    let result = PermissionRules.findMatch (readFrom files) scopes "Bash" "anything"

    // Assert
    %result.Should().BeSome().WhoseValue.Scope.Should().Be("enterprise")

[<Fact>]
let ``resolveReason returns the matched rule for a rule decision`` () =
    // Arrange
    let files = Map.ofList [ "user", askJson [ "Bash(*)" ] ]

    // Act
    let reason = PermissionRules.resolveReason (readFrom files) [ "user", "user" ] "rule" null "Bash" "ls"

    // Assert
    %reason.MatchedRulePattern.Should().Be("Bash(*)")
    %reason.MatchedRuleScope.Should().Be("user")
    %reason.ReasonText.Should().Be("")

[<Fact>]
let ``resolveReason explains a rule decision with no matching rule`` () =
    // Act
    let reason = PermissionRules.resolveReason noFiles noScopes "rule" null "Bash" "ls"

    // Assert
    %reason.ReasonText.Should().Be("Matching ask rule")
    %reason.MatchedRulePattern.Should().Be("")

[<Fact>]
let ``resolveReason passes safety-check text through`` () =
    // Act
    let reason = PermissionRules.resolveReason noFiles noScopes "safetyCheck" "Dangerous command" "Bash" "x"

    // Assert
    %reason.ReasonText.Should().Be("Dangerous command")

[<Fact>]
let ``resolveReason defaults safety-check text when absent`` () =
    // Act
    let reason = PermissionRules.resolveReason noFiles noScopes "safetyCheck" null "Bash" "x"

    // Assert
    %reason.ReasonText.Should().Be("No matching permission rule")

[<Fact>]
let ``resolveReason describes compound commands`` () =
    // Act
    let reason = PermissionRules.resolveReason noFiles noScopes "subcommandResults" null "Bash" "x"

    // Assert
    %reason.ReasonText.Should().Be("Compound command requires approval")

[<Theory>]
[<InlineData("This command requires approval", "No matching permission rule")>]
[<InlineData("Some other explanation", "Some other explanation")>]
let ``resolveReason maps unknown reason types from the reason text`` (reason: string, expected: string) =
    // Act
    let resolved = PermissionRules.resolveReason noFiles noScopes "" reason "Bash" "x"

    // Assert
    %resolved.ReasonText.Should().Be(expected)

[<Fact>]
let ``resolveReason defaults when reason type and text are both absent`` () =
    // Act
    let resolved = PermissionRules.resolveReason noFiles noScopes null null "Bash" "x"

    // Assert
    %resolved.ReasonText.Should().Be("No matching permission rule")
