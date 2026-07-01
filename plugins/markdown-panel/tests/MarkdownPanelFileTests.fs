module FabioSoft.Nucleus.MarkdownPanel.Tests.MarkdownPanelFileTests

open FabioSoft.Nucleus.Plugins.MarkdownPanel
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``parse of empty yaml is empty`` () =
    %MarkdownPanelFile.Parse(null).Should().HaveLength(0)
    %MarkdownPanelFile.Parse("").Should().HaveLength(0)

[<Fact>]
let ``serialize then parse round-trips definitions with multiline placeholder bodies`` () =

    // Arrange - bodies start with '#', which forces YAML to quote them, so newlines round-trip exactly.
    let definitions =
        [| MarkdownDefinition("a", "Dashboard", "# {cwd.short}\n\nBranch **{git.branch}**")
           MarkdownDefinition("b", "Status", "# Status\nModel: {agent.name:uppercase}") |]

    // Act
    let parsed = MarkdownPanelFile.Parse(MarkdownPanelFile.Serialize definitions)

    // Assert
    %parsed.Count.Should().Be(2)
    %parsed[0].Should().Be(definitions[0])
    %parsed[1].Should().Be(definitions[1])

[<Fact>]
let ``parse skips entries without an id`` () =

    // Arrange
    let yaml = "panels:\n  - id: a\n    title: Alpha\n    body: hello\n  - title: Orphan\n    body: nope\n"

    // Act
    let parsed = MarkdownPanelFile.Parse(yaml)

    // Assert
    %parsed.Count.Should().Be(1)
    %parsed[0].Id.Should().Be("a")

[<Fact>]
let ``parse normalizes a blank title`` () =

    // Arrange
    let yaml = "panels:\n  - id: a\n    title: \"\"\n    body: hello\n"

    // Act
    let parsed = MarkdownPanelFile.Parse(yaml)

    // Assert
    %parsed[0].Title.Should().Be(MarkdownCatalog.DefaultTitle)

[<Fact>]
let ``serialize starter yields one example panel with placeholder tokens`` () =

    // Act
    let parsed = MarkdownPanelFile.Parse(MarkdownPanelFile.SerializeStarter())

    // Assert
    %parsed.Count.Should().Be(1)
    %parsed[0].Body.Should().Contain("{git.branch}")

[<Fact>]
let ``parse throws on malformed yaml`` () =
    let act () = MarkdownPanelFile.Parse("- not\n- a mapping") |> ignore
    %act.Should().Throw<exn, _>()
