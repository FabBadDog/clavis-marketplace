module FabioSoft.Nucleus.CodeEditorPanel.Tests.FileTreeTests

open System.IO
open FabioSoft.Nucleus.Plugins.CodeEditorPanel
open Faqt
open Faqt.Operators
open Xunit

let private node name kind = FileNode(name, name, kind)

[<Fact>]
let ``Order puts directories before files`` () =

    // Arrange
    let input = [ node "zeta.txt" FileNodeKind.File; node "alpha" FileNodeKind.Directory ]

    // Act
    let result = FileTree.Order(input)

    // Assert
    %result[0].Kind.Should().Be(FileNodeKind.Directory)
    %result[1].Kind.Should().Be(FileNodeKind.File)

[<Fact>]
let ``Order sorts directories then files case-insensitively`` () =

    // Arrange
    let input =
        [ node "banana.fs" FileNodeKind.File
          node "Apple.fs" FileNodeKind.File
          node "Zeb" FileNodeKind.Directory
          node "alpha" FileNodeKind.Directory ]

    // Act
    let names = FileTree.Order(input) |> Seq.map _.Name |> Seq.toList

    // Assert
    %names.Should().Be([ "alpha"; "Zeb"; "Apple.fs"; "banana.fs" ])

[<Theory>]
[<InlineData(FileAttributes.Hidden, true)>]
[<InlineData(FileAttributes.System, true)>]
[<InlineData(FileAttributes.Normal, false)>]
[<InlineData(FileAttributes.Directory, false)>]
let ``IsHidden flags hidden and system entries`` (attributes: FileAttributes, expected: bool) =

    // Act / Assert
    %FileTree.IsHidden(attributes).Should().Be(expected)
