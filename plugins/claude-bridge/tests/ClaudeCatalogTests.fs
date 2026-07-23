module FabioSoft.Nucleus.ClaudeBridge.Tests.ClaudeCatalogTests

open FabioSoft.Nucleus.Plugins.ClaudeBridge
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``catalog offers models including older generations`` () =

    // Act
    let ids = ClaudeCatalog.Models |> Seq.map _.Id |> List.ofSeq

    // Assert
    %ids.Should().Contain("claude-fable-5")
    %ids.Should().Contain("claude-opus-4-6")
    %ids.Should().Contain("claude-opus-4-6[1m]")
    %ids.Should().Contain("claude-haiku-4-4")

[<Fact>]
let ``models carry display data instead of internal names`` () =

    // Act
    let fable = ClaudeCatalog.Models |> Seq.find (fun m -> m.Id = "claude-fable-5[1m]")

    // Assert
    %fable.DisplayName.Should().Be("Fable 5 (1M)")
    %fable.Version.Should().Be("5.0")
    %fable.ContextSize.Should().Be(1_000_000)
    %fable.Description.Should().NotBeEmpty()

[<Fact>]
let ``xhigh effort surfaces as Extra High`` () =

    // Act
    let xhigh = ClaudeCatalog.Efforts |> Seq.find (fun e -> e.Id = "xhigh")

    // Assert
    %xhigh.DisplayName.Should().Be("Extra High")

[<Fact>]
let ``every effort is color coded and described`` () =

    // Assert
    for effort in ClaudeCatalog.Efforts do
        %effort.Color.Should().NotBeEmpty()
        %effort.Description.Should().NotBeEmpty()

[<Fact>]
let ``modes surface in the Shift+Tab cycle order Plan None Auto Edit`` () =

    // Act
    let names = ClaudeCatalog.Modes |> Seq.map _.DisplayName |> List.ofSeq

    // Assert
    %names.Should().Be([ "Plan"; "None"; "Auto"; "Edit" ])

[<Theory>]
[<InlineData("claude-fable-5", "ultracode", true)>]
[<InlineData("claude-opus-4-8", "xhigh", true)>]
[<InlineData("claude-opus-4-8", "ultracode", false)>]
[<InlineData("claude-opus-4-5", "xhigh", false)>]
[<InlineData("claude-haiku-4-5", "low", false)>]
let ``effort support depends on the model`` (model: string) (effort: string) (expected: bool) =

    // Act & Assert
    %ClaudeCatalog.SupportsEffort(model, effort).Should().Be(expected)

[<Theory>]
[<InlineData("claude-opus-4-8", "claude-opus-4-8")>]
[<InlineData("claude-opus-4-8-20260115", "claude-opus-4-8")>]
[<InlineData("claude-fable-5[1m]", "claude-fable-5[1m]")>]
[<InlineData("opus", "claude-opus-4-8")>]
[<InlineData("haiku", "claude-haiku-4-5")>]
let ``reported model strings resolve onto catalog ids`` (reported: string) (expected: string) =

    // Act
    let resolved = ClaudeCatalog.ResolveModel(reported)

    // Assert
    %(box resolved |> isNull).Should().BeFalse()
    %resolved.Id.Should().Be(expected)

[<Fact>]
let ``unknown model resolves to null`` () =

    // Act & Assert
    %(ClaudeCatalog.ResolveModel("gpt-9") |> box |> isNull).Should().BeTrue()

[<Theory>]
[<InlineData("claude-fable-5", "high")>]
[<InlineData("claude-haiku-4-5", "")>]
let ``default effort is high only when the model supports effort`` (model: string) (expected: string) =

    // Act & Assert
    %ClaudeCatalog.DefaultEffortFor(model).Should().Be(expected)

[<Theory>]
[<InlineData("claude-opus-4-8", "ultracode", "high")>]
[<InlineData("claude-opus-4-8", "xhigh", "xhigh")>]
[<InlineData("claude-haiku-4-5", "max", "")>]
[<InlineData("claude-fable-5", "", "high")>]
let ``a model switch coerces an unsupported effort onto the model default``
    (model: string) (currentEffort: string) (expected: string) =

    // Act & Assert
    %ClaudeCatalog.CoerceEffort(model, currentEffort).Should().Be(expected)
