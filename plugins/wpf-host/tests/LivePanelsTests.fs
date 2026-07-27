module FabioSoft.Nucleus.WpfHost.Tests.LivePanelsTests

open System
open FabioSoft.Contracts.Layout
open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

let private panel workspaceId = LivePanel(Guid.NewGuid(), Guid.NewGuid(), "tab", workspaceId)

[<Fact>]
let ``many never reuses a live panel`` () =

    // Arrange
    let candidates = [| panel Guid.Empty |]

    // Act
    let found = LivePanels.Find(candidates, PanelCardinality.Many, Guid.Empty, Guid.Empty)

    // Assert
    %found.HasValue.Should().BeFalse()

[<Fact>]
let ``one per application reuses an instance from another workspace`` () =

    // Arrange
    let elsewhere = panel (Guid.NewGuid())

    // Act
    let found = LivePanels.Find([| elsewhere |], PanelCardinality.OnePerApplication, Guid.NewGuid(), Guid.Empty)

    // Assert
    %found.HasValue.Should().BeTrue()
    %found.Value.PanelId.Should().Be(elsewhere.PanelId)

[<Fact>]
let ``one per workspace does not reuse an instance from another workspace`` () =

    // Arrange
    let elsewhere = panel (Guid.NewGuid())

    // Act
    let found = LivePanels.Find([| elsewhere |], PanelCardinality.OnePerWorkspace, Guid.NewGuid(), Guid.Empty)

    // Assert
    %found.HasValue.Should().BeFalse()

[<Fact>]
let ``one per workspace reuses the instance in the same workspace`` () =

    // Arrange
    let workspaceId = Guid.NewGuid()
    let here = panel workspaceId

    // Act
    let found = LivePanels.Find([| panel (Guid.NewGuid()); here |], PanelCardinality.OnePerWorkspace, workspaceId, Guid.Empty)

    // Assert
    %found.HasValue.Should().BeTrue()
    %found.Value.PanelId.Should().Be(here.PanelId)

[<Fact>]
let ``the excluded instance never matches itself`` () =

    // Arrange
    let minted = panel Guid.Empty

    // Act
    let found = LivePanels.Find([| minted |], PanelCardinality.OnePerWorkspace, Guid.Empty, minted.PanelId)

    // Assert
    %found.HasValue.Should().BeFalse()

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData(null)>]
let ``an unset cardinality reads as one per workspace`` (cardinality: string) =

    // Act
    let normalized = LivePanels.Normalize cardinality

    // Assert
    %normalized.Should().Be(PanelCardinality.OnePerWorkspace)

[<Fact>]
let ``an unset cardinality scopes the search to the workspace`` () =

    // Arrange
    let elsewhere = panel (Guid.NewGuid())

    // Act
    let found = LivePanels.Find([| elsewhere |], "", Guid.NewGuid(), Guid.Empty)

    // Assert
    %found.HasValue.Should().BeFalse()

[<Fact>]
let ``an empty candidate list finds nothing`` () =

    // Act
    let found = LivePanels.Find([||], PanelCardinality.OnePerApplication, Guid.Empty, Guid.Empty)

    // Assert
    %found.HasValue.Should().BeFalse()
