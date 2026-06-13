module FabioSoft.Nucleus.Configuration.Tests.SectionedYamlTests

open FabioSoft.Nucleus.Plugins.Configuration
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``UpsertSection into empty text produces a file with just that section`` () =
    // Act
    let merged = SectionedYaml.UpsertSection("", "alpha", "value: 1")

    // Assert
    %merged.Should().Contain("alpha:")
    %(SectionedYaml.ReadSection(merged, "alpha")).Should().Contain("value: 1")

[<Fact>]
let ``ReadSection returns null for an absent section`` () =
    // Arrange
    let merged = SectionedYaml.UpsertSection("", "alpha", "value: 1")

    // Act / Assert
    %(SectionedYaml.ReadSection(merged, "missing")).Should().BeNull()

[<Fact>]
let ``ReadSection returns null for empty text`` () =
    %(SectionedYaml.ReadSection("", "alpha")).Should().BeNull()

[<Fact>]
let ``UpsertSection appends a new section after the existing ones`` () =
    // Arrange
    let withAlpha = SectionedYaml.UpsertSection("", "alpha", "a: 1")

    // Act
    let withBoth = SectionedYaml.UpsertSection(withAlpha, "beta", "b: 2")

    // Assert
    %(SectionedYaml.ReadSection(withBoth, "alpha")).Should().Contain("a: 1")
    %(SectionedYaml.ReadSection(withBoth, "beta")).Should().Contain("b: 2")
    %(withBoth.IndexOf("alpha:") < withBoth.IndexOf("beta:")).Should().BeTrue()

[<Fact>]
let ``UpsertSection replaces a present section in place, preserving the others and its position`` () =
    // Arrange
    let withBoth =
        SectionedYaml.UpsertSection(SectionedYaml.UpsertSection("", "alpha", "a: 1"), "beta", "b: 2")

    // Act
    let updated = SectionedYaml.UpsertSection(withBoth, "alpha", "a: 99")

    // Assert
    %(SectionedYaml.ReadSection(updated, "alpha")).Should().Contain("a: 99")
    %(SectionedYaml.ReadSection(updated, "beta")).Should().Contain("b: 2")
    %(updated.IndexOf("alpha:") < updated.IndexOf("beta:")).Should().BeTrue()

[<Fact>]
let ``a nested section survives a round-trip`` () =
    // Arrange
    let body = "outer:\n  inner: deep\nflag: true"

    // Act
    let read = SectionedYaml.ReadSection(SectionedYaml.UpsertSection("", "alpha", body), "alpha")

    // Assert
    %read.Should().Contain("inner: deep")
    %read.Should().Contain("flag: true")
