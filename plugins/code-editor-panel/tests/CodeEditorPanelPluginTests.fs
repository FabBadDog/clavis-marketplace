module FabioSoft.Nucleus.CodeEditorPanel.Tests.CodeEditorPanelPluginTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.CodeEditorPanel
open Xunit

let private plugin = CodeEditorPanelPlugin()

[<Fact>]
let ``Id is CodeEditorPanel`` () =

    %plugin.Id.Should().Be("CodeEditorPanel")

[<Fact>]
let ``DefaultConfig has empty root and hidden files off`` () =

    // Act
    let config = plugin.DefaultConfig

    // Assert
    %config.RootPath.Should().Be("")
    %config.ShowHiddenFiles.Should().BeFalse()
