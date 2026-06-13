module FabioSoft.Nucleus.GitLogPanel.Tests.GitLogParseTests

open FabioSoft.Nucleus.Plugins.GitLogPanel
open Faqt
open Faqt.Operators
open Xunit

let private separator = string GitLogParse.FieldSeparator

let private commitLine hash message author time =
    String.concat separator [ hash; message; author; time ]

[<Fact>]
let ``parses a well-formed commit line into its four fields`` () =

    // Arrange
    let raw = commitLine "abc123" "Initial commit" "Fabio" "2 hours ago"

    // Act
    let rows = GitLogParse.Parse(raw)

    // Assert
    %rows.Count.Should().Be(1)
    %rows[0].Hash.Should().Be("abc123")
    %rows[0].Message.Should().Be("Initial commit")
    %rows[0].Author.Should().Be("Fabio")
    %rows[0].RelativeTime.Should().Be("2 hours ago")

[<Fact>]
let ``parses multiple lines and skips lines without four fields`` () =

    // Arrange
    let raw =
        String.concat "\n"
            [ commitLine "a" "m1" "auth" "t1"
              "garbage-without-separators"
              commitLine "b" "m2" "auth" "t2" ]

    // Act
    let rows = GitLogParse.Parse(raw)

    // Assert
    %rows.Count.Should().Be(2)
    %rows[0].Hash.Should().Be("a")
    %rows[1].Hash.Should().Be("b")

[<Theory>]
[<InlineData(null)>]
[<InlineData("")>]
[<InlineData("\n\n")>]
let ``returns no rows for empty output`` (raw: string) =
    %GitLogParse.Parse(raw).Count.Should().Be(0)

[<Fact>]
let ``trims the carriage return from CRLF line endings`` () =

    // Arrange
    let raw = commitLine "a" "m" "auth" "t" + "\r\n" + commitLine "b" "m2" "auth" "t2"

    // Act
    let rows = GitLogParse.Parse(raw)

    // Assert
    %rows.Count.Should().Be(2)
    %rows[0].RelativeTime.Should().Be("t")
