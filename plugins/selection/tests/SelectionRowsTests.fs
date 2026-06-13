module FabioSoft.Nucleus.Selection.Tests.SelectionRowsTests

open System.Collections.Generic
open FabioSoft.Contracts.Host
open FabioSoft.Contracts.Session
open FabioSoft.Nucleus.Plugins.Selection
open Faqt
open Faqt.Operators
open Xunit

let private modelInfo id displayName version contextSize description efforts =
    AgentModelInfo(id, displayName, version, contextSize, description, List<string>(efforts: string list))

[<Fact>]
let ``model rows carry display data and the internal id only for the accept message`` () =

    // Arrange
    let models = [ modelInfo "claude-opus-4-8" "Opus 4.8" "4.8" 200_000 "Most capable Opus" [ "high" ] ]

    // Act
    let rows = SelectionRows.BuildModels(models)

    // Assert
    %rows.Count.Should().Be(1)
    %rows[0].Id.Should().Be("claude-opus-4-8")
    %rows[0].Name.Should().Be("Opus 4.8")
    %rows[0].Version.Should().Be("4.8")
    %rows[0].Context.Should().Be("200k")
    %rows[0].Description.Should().Be("Most capable Opus")

[<Fact>]
let ``effort rows offer only the supported levels in catalog order`` () =

    // Arrange
    let efforts =
        [ AgentEffortInfo("low", "Low", "Fast", "dim")
          AgentEffortInfo("xhigh", "Extra High", "Very deep", "accent")
          AgentEffortInfo("ultracode", "Ultracode", "Orchestration", "red") ]

    // Act
    let rows = SelectionRows.BuildEfforts(efforts, [ "xhigh"; "low" ])

    // Assert
    %(rows |> Seq.map _.Id |> List.ofSeq).Should().Be([ "low"; "xhigh" ])
    %rows[1].Name.Should().Be("Extra High")

[<Fact>]
let ``panel rows sort by title and fall back to the kind`` () =

    // Arrange
    let kinds =
        [ KeyValuePair("git-log", "Git Log")
          KeyValuePair("events", "Events")
          KeyValuePair("bare-kind", "") ]

    // Act
    let rows = SelectionRows.BuildPanels(kinds)

    // Assert
    %(rows |> Seq.map _.Title |> List.ofSeq).Should().Be([ "bare-kind"; "Events"; "Git Log" ])

[<Fact>]
let ``option rows carry value label and description`` () =

    // Arrange
    let options = [ SelectionOption("opt-1", "First option", "The safe choice") ]

    // Act
    let rows = SelectionRows.BuildOptions(options)

    // Assert
    %rows[0].Value.Should().Be("opt-1")
    %rows[0].Label.Should().Be("First option")
    %rows[0].Description.Should().Be("The safe choice")

[<Fact>]
let ``empty filter passes all rows in build order`` () =

    // Arrange
    let rows = SelectionRows.BuildModes([ AgentModeInfo("plan", "Plan", "Plan first"); AgentModeInfo("default", "None", "Always ask") ])

    // Act
    let filtered = SelectionRows.Filter(rows, "   ", fun (row: ModeRow) -> SelectionRows.SearchableFields(row))

    // Assert
    %filtered.Count.Should().Be(2)

[<Fact>]
let ``filter matches any searchable field case-insensitively`` () =

    // Arrange
    let rows =
        SelectionRows.BuildModels(
            [ modelInfo "claude-opus-4-8" "Opus 4.8" "4.8" 200_000 "Deep reasoning" [];
              modelInfo "claude-haiku-4-5" "Haiku 4.5" "4.5" 200_000 "Lightweight" [] ])

    // Act
    let byName = SelectionRows.Filter(rows, "OPUS", fun (row: ModelRow) -> SelectionRows.SearchableFields(row))
    let byDescription = SelectionRows.Filter(rows, "lightweight", fun (row: ModelRow) -> SelectionRows.SearchableFields(row))

    // Assert
    %byName.Count.Should().Be(1)
    %(byName[0] :?> ModelRow).Name.Should().Be("Opus 4.8")
    %byDescription.Count.Should().Be(1)
    %(byDescription[0] :?> ModelRow).Name.Should().Be("Haiku 4.5")

[<Theory>]
[<InlineData(200_000, "200k")>]
[<InlineData(1_000_000, "1M")>]
[<InlineData(1_500_000, "1.5M")>]
[<InlineData(800, "800")>]
let ``context sizes format to the short display form`` (tokens: int) (expected: string) =

    // Act & Assert
    %SelectionRows.FormatContext(tokens).Should().Be(expected)

[<Theory>]
[<InlineData("accent", "ClavisBrush")>]
[<InlineData("green", "GreenBrush")>]
[<InlineData("red", "RedBrush")>]
[<InlineData("dim", "TextDimBrush")>]
[<InlineData("unknown", "TextBrush")>]
let ``provider color hints map onto theme brush keys`` (hint: string) (expected: string) =

    // Act & Assert
    %SelectionRows.BrushKeyFor(hint).Should().Be(expected)
