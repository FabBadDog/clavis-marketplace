module FabioSoft.Editor.Tests.MarkdownSyntaxTests

open FabioSoft.Editor
open Faqt
open Faqt.Operators
open Xunit

[<Theory>]
[<InlineData("```", true)>]
[<InlineData("```fsharp", true)>]
[<InlineData("   ```", true)>]
[<InlineData("not a fence", false)>]
[<InlineData("`inline`", false)>]
let ``isFenceDelimiter detects code fences`` (text: string, expected: bool) =

    // Act / Assert
    %(MarkdownSyntax.isFenceDelimiter text).Should().Be(expected)

[<Fact>]
let ``isInsideFence is false outside a fence and with no preceding lines`` () =

    // Act / Assert
    %(MarkdownSyntax.isInsideFence []).Should().BeFalse()
    %(MarkdownSyntax.isInsideFence [ "text"; "more" ]).Should().BeFalse()

[<Fact>]
let ``isInsideFence is true after an odd number of fence delimiters`` () =

    // Act / Assert
    %(MarkdownSyntax.isInsideFence [ "```fsharp"; "let x = 1" ]).Should().BeTrue()

[<Fact>]
let ``isInsideFence is false after a closed fence`` () =

    // Act / Assert
    %(MarkdownSyntax.isInsideFence [ "```"; "code"; "```" ]).Should().BeFalse()

[<Fact>]
let ``inlineDecorations returns nothing for plain text`` () =

    // Act / Assert
    %(MarkdownSyntax.inlineDecorations 0 "just some prose").Should().BeEmpty()

[<Fact>]
let ``inlineDecorations marks inline code with surrounding markers`` () =

    // Act
    let spans = MarkdownSyntax.inlineDecorations 0 "use `code` here"

    // Assert
    %spans.Should().SequenceEqual(
        [ { Start = 4; Length = 1; Token = Marker }
          { Start = 5; Length = 4; Token = InlineCode }
          { Start = 9; Length = 1; Token = Marker } ])

[<Fact>]
let ``inlineDecorations ignores an unterminated inline code backtick`` () =

    // Act / Assert
    %(MarkdownSyntax.inlineDecorations 0 "a lone ` backtick").Should().BeEmpty()

[<Fact>]
let ``inlineDecorations marks double-star as bold`` () =

    // Act
    let spans = MarkdownSyntax.inlineDecorations 0 "**bold**"

    // Assert
    %spans.Should().SequenceEqual(
        [ { Start = 0; Length = 2; Token = Marker }
          { Start = 2; Length = 4; Token = Bold }
          { Start = 6; Length = 2; Token = Marker } ])

[<Fact>]
let ``inlineDecorations marks double-underscore as bold`` () =

    // Act
    let spans = MarkdownSyntax.inlineDecorations 0 "__b__"

    // Assert
    %(spans |> List.map _.Token).Should().SequenceEqual([ Marker; Bold; Marker ])

[<Fact>]
let ``inlineDecorations marks single-star as italic`` () =

    // Act
    let spans = MarkdownSyntax.inlineDecorations 0 "*em*"

    // Assert
    %(spans |> List.map _.Token).Should().SequenceEqual([ Marker; Italic; Marker ])

[<Fact>]
let ``inlineDecorations marks single-underscore as italic`` () =

    // Act
    let spans = MarkdownSyntax.inlineDecorations 0 "_em_"

    // Assert
    %(spans |> List.map _.Token).Should().SequenceEqual([ Marker; Italic; Marker ])

[<Fact>]
let ``inlineDecorations ignores an unterminated emphasis run`` () =

    // Act / Assert
    %(MarkdownSyntax.inlineDecorations 0 "lone * star").Should().BeEmpty()

[<Fact>]
let ``inlineDecorations marks a link's text and brackets`` () =

    // Act
    let spans = MarkdownSyntax.inlineDecorations 0 "see [docs](url)"

    // Assert
    %spans.Should().SequenceEqual(
        [ { Start = 4; Length = 1; Token = Marker }
          { Start = 5; Length = 4; Token = Link }
          { Start = 9; Length = 6; Token = Marker } ])

[<Theory>]
[<InlineData("[no close")>]
[<InlineData("[text]")>]
[<InlineData("[text]x")>]
[<InlineData("[unclosed](url")>]
let ``inlineDecorations ignores malformed links`` (text: string) =

    // Act / Assert
    %(MarkdownSyntax.inlineDecorations 0 text).Should().BeEmpty()

[<Fact>]
let ``inlineDecorations shifts spans by the given offset`` () =

    // Act
    let spans = MarkdownSyntax.inlineDecorations 10 "`x`"

    // Assert
    %(spans |> List.map _.Start).Should().SequenceEqual([ 10; 11; 12 ])

[<Theory>]
[<InlineData("# Title", 1)>]
[<InlineData("## Section", 2)>]
[<InlineData("### Sub", 3)>]
let ``styleLine treats ATX headings as headings`` (text: string, level: int) =

    // Act
    let style = MarkdownSyntax.styleLine false text

    // Assert
    %style.Base.Should().Be(Heading level)
    %style.Decorations.Head.Should().Be({ Start = 0; Length = level; Token = Marker })

[<Fact>]
let ``styleLine keeps inline decorations inside a heading`` () =

    // Act
    let style = MarkdownSyntax.styleLine false "# A `b`"

    // Assert
    %(style.Decorations |> List.map _.Token).Should().SequenceEqual([ Marker; Marker; InlineCode; Marker ])

[<Theory>]
[<InlineData("- item")>]
[<InlineData("* item")>]
[<InlineData("+ item")>]
[<InlineData("1. item")>]
let ``styleLine marks list bullets`` (text: string) =

    // Act
    let style = MarkdownSyntax.styleLine false text

    // Assert
    %style.Base.Should().Be(Body)
    %style.Decorations.Head.Token.Should().Be(ListBullet)

[<Fact>]
let ``styleLine marks a blockquote prefix`` () =

    // Act
    let style = MarkdownSyntax.styleLine false "> quoted"

    // Assert
    %style.Base.Should().Be(Body)
    %style.Decorations.Head.Should().Be({ Start = 0; Length = 1; Token = Marker })

[<Fact>]
let ``styleLine treats ordinary text as a body paragraph`` () =

    // Act
    let style = MarkdownSyntax.styleLine false "plain paragraph"

    // Assert
    %style.Base.Should().Be(Body)
    %style.Decorations.Should().BeEmpty()

[<Fact>]
let ``styleLine renders a fence delimiter line as a fence`` () =

    // Act / Assert
    %(MarkdownSyntax.styleLine false "```fsharp").Base.Should().Be(CodeFence)

[<Fact>]
let ``styleLine renders content inside a fence as code text`` () =

    // Act
    let style = MarkdownSyntax.styleLine true "let x = 1"

    // Assert
    %style.Base.Should().Be(CodeText)
    %style.Decorations.Should().BeEmpty()

[<Fact>]
let ``styleLine renders the closing fence line as a fence even while inside`` () =

    // Act / Assert
    %(MarkdownSyntax.styleLine true "```").Base.Should().Be(CodeFence)
