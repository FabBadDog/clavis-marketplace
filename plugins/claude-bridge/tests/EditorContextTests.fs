module FabioSoft.Nucleus.ClaudeBridge.Tests.EditorContextTests

open FabioSoft.Contracts.Editor
open FabioSoft.Nucleus.Plugins.ClaudeBridge
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``Decorate returns the prompt unchanged when there is no editor state`` () =

    %EditorContext.Decorate("hello", Unchecked.defaultof<EditorStateChanged>).Should().Be("hello")

[<Fact>]
let ``Decorate returns the prompt unchanged when no file is open`` () =

    // Arrange
    let state = EditorStateChanged("", "Text", 1, 1, 1, 1, 1, 1, "")

    // Act / Assert
    %EditorContext.Decorate("hello", state).Should().Be("hello")

[<Fact>]
let ``Decorate prepends the active file and cursor when nothing is selected`` () =

    // Arrange
    let state = EditorStateChanged("C:\\repo\\Program.fs", "F#", 12, 3, 12, 3, 12, 3, "")

    // Act
    let result = EditorContext.Decorate("explain this", state)

    // Assert
    %result.Should().Contain("Active file: C:\\repo\\Program.fs")
    %result.Should().Contain("Cursor: line 12, column 3")
    %result.Should().Contain("explain this")

[<Fact>]
let ``Decorate includes the selection block when text is selected`` () =

    // Arrange
    let state = EditorStateChanged("C:\\repo\\Program.fs", "F#", 5, 1, 3, 1, 5, 10, "let x = 1")

    // Act
    let result = EditorContext.Decorate("refactor", state)

    // Assert
    %result.Should().Contain("Selected lines 3-5")
    %result.Should().Contain("let x = 1")
    %result.Should().Contain("refactor")
