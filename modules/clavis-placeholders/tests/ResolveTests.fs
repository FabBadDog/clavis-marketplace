module FabioSoft.Clavis.Placeholders.Tests.ResolveTests

open FabioSoft.Clavis.Placeholders
open Faqt
open Faqt.Operators
open Xunit

let private engine = PlaceholderEngine()

let private values =
    readOnlyDict
        [ "git.branch", "main"
          "agent.name", "claude"
          "agent.contextPercent", "64" ]

[<Fact>]
let ``resolve substitutes a known value`` () =
    %engine.ResolveToText("on {git.branch}", values).Should().Be("on main")

[<Fact>]
let ``resolve leaves an unknown token verbatim`` () =
    %engine.ResolveToText("{git.unknown}", values).Should().Be("{git.unknown}")

[<Fact>]
let ``resolve applies a named transform`` () =
    %engine.ResolveToText("{agent.name:uppercase}", values).Should().Be("CLAUDE")

[<Fact>]
let ``resolve a resolvedComponent exposes value text and parsed number`` () =

    // Act
    let segments = engine.Resolve("{bar:agent.contextPercent}", values)

    // Assert
    %segments.Count.Should().Be(1)
    let resolvedComponent = segments[0] :?> ResolvedComponent
    %resolvedComponent.Component.Should().Be("bar")
    %resolvedComponent.Value.Should().Be("64")
    %resolvedComponent.Number.HasValue.Should().BeTrue()
    %resolvedComponent.Number.Value.Should().Be(64.0)

[<Fact>]
let ``resolve a resolvedComponent with a missing value yields empty text and no number`` () =

    // Act
    let segments = engine.Resolve("{bar:agent.missing}", values)

    // Assert
    let resolvedComponent = segments[0] :?> ResolvedComponent
    %resolvedComponent.Value.Should().Be("")
    %resolvedComponent.Number.HasValue.Should().BeFalse()

[<Fact>]
let ``resolve marks an unknown value token unresolved`` () =
    %(engine.Resolve("{git.unknown}", values)[0] :?> ResolvedText).Unresolved.Should().BeTrue()

[<Fact>]
let ``resolve marks literals and known values resolved`` () =

    // Act
    let segments = engine.Resolve("on {git.branch}", values)

    // Assert
    %(segments[0] :?> ResolvedText).Unresolved.Should().BeFalse()
    %(segments[1] :?> ResolvedText).Unresolved.Should().BeFalse()

[<Fact>]
let ``resolve marks a component with a missing value key unresolved`` () =
    %(engine.Resolve("{bar:agent.missing}", values)[0] :?> ResolvedComponent).Unresolved.Should().BeTrue()

[<Fact>]
let ``resolve marks a component with a known value key resolved`` () =
    %(engine.Resolve("{bar:agent.contextPercent}", values)[0] :?> ResolvedComponent).Unresolved.Should().BeFalse()

[<Fact>]
let ``resolve marks a keyless component resolved`` () =
    %(engine.Resolve("{limitPlane}", values)[0] :?> ResolvedComponent).Unresolved.Should().BeFalse()
