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
[<InlineData("Directory.Build.props", "XML")>]
[<InlineData("theme.xshd", "XML")>]
[<InlineData("data.json", "JSON")>]
[<InlineData("run.ps1", "PowerShell")>]
[<InlineData("readme.md", "Markdown")>]
[<InlineData("config.yaml", "YAML")>]
[<InlineData("compose.yml", "YAML")>]
[<InlineData("notes.txt", "Text")>]
[<InlineData("noextension", "Text")>]
[<InlineData("archive.zip", "Text")>]
let ``languageForExtension maps file paths to language labels`` (path: string, expected: string) =

    // Act / Assert
    %(CodeEditorSyntax.languageForExtension path).Should().Be(expected)

[<Theory>]
[<InlineData("C#", "C#")>]
[<InlineData("XML", "XML")>]
[<InlineData("JSON", "Json")>]
[<InlineData("JavaScript", "JavaScript")>]
[<InlineData("HTML", "HTML")>]
[<InlineData("CSS", "CSS")>]
[<InlineData("PowerShell", "PowerShell")>]
[<InlineData("Python", "Python")>]
[<InlineData("SQL", "TSQL")>]
let ``builtInDefinitionName maps a label to its AvalonEdit definition`` (label: string, expected: string) =

    // Act / Assert
    %(CodeEditorSyntax.builtInDefinitionName label).Should().Be(Some expected)

[<Theory>]
[<InlineData("F#")>]
[<InlineData("YAML")>]
[<InlineData("Markdown")>]
[<InlineData("Text")>]
let ``builtInDefinitionName has no built-in for custom or plain labels`` (label: string) =

    // Act / Assert
    %(CodeEditorSyntax.builtInDefinitionName label).Should().BeNone()
