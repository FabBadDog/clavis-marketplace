module FabioSoft.Nucleus.Conversation.Tests.JsonPrettyTests

open FabioSoft.Nucleus.Plugins.Conversation
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``Format indents compact json onto multiple lines`` () =

    // Act
    let result = JsonPretty.Format("""{"a":1,"b":2}""")

    // Assert
    %result.Should().Contain("\n") |> ignore
    %result.Should().Contain("  \"a\": 1")

[<Fact>]
let ``Format keeps unicode unescaped`` () =

    // Act
    let result = JsonPretty.Format("""{"name":"Grüße"}""")

    // Assert
    %result.Should().Contain("Grüße")

[<Fact>]
let ``Format leaves plain text unchanged`` () =

    %JsonPretty.Format("Launching skill: todos").Should().Be("Launching skill: todos")

[<Fact>]
let ``Format leaves an empty string empty`` () =

    %JsonPretty.Format("").Should().Be("")

[<Fact>]
let ``Format returns malformed json-looking text unchanged`` () =

    // Arrange
    let text = "{not valid json"

    // Act & Assert
    %JsonPretty.Format(text).Should().Be(text)
