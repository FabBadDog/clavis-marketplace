module FabioSoft.Editor.Tests.CodeEditorSyntaxTests

open FabioSoft.Editor
open Faqt
open Faqt.Operators
open Xunit

[<Theory>]
[<InlineData("Program.fs", "F#")>]
[<InlineData("script.fsx", "F#")>]
[<InlineData("Types.fsi", "F#")>]
[<InlineData("Widget.cs", "C#")>]
[<InlineData("App.xaml", "XML")>]
[<InlineData("build.fsproj", "XML")>]
[<InlineData("data.json", "JSON")>]
[<InlineData("run.ps1", "PowerShell")>]
[<InlineData("readme.md", "Markdown")>]
[<InlineData("notes.txt", "Text")>]
[<InlineData("noextension", "Text")>]
[<InlineData("archive.zip", "Text")>]
let ``languageForExtension maps file paths to language labels`` (path: string, expected: string) =

    // Act / Assert
    %(CodeEditorSyntax.languageForExtension path).Should().Be(expected)
