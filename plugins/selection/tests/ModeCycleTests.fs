module FabioSoft.Nucleus.Selection.Tests.ModeCycleTests

open System.Collections.Generic
open FabioSoft.Nucleus.Plugins.Selection
open Faqt
open Faqt.Operators
open Xunit

let private modes: IReadOnlyList<string> = [| "plan"; "default"; "auto"; "acceptEdits" |]

[<Theory>]
[<InlineData("plan", "default")>]
[<InlineData("default", "auto")>]
[<InlineData("auto", "acceptEdits")>]
let ``next advances to the following mode`` (current: string) (expected: string) =
    %(ModeCycle.Next(modes, current)).Should().Be(expected)

[<Fact>]
let ``next wraps from the last mode back to the first`` () =
    %(ModeCycle.Next(modes, "acceptEdits")).Should().Be("plan")

[<Fact>]
let ``an unknown current mode advances to the first`` () =
    %(ModeCycle.Next(modes, "bypassPermissions")).Should().Be("plan")

[<Fact>]
let ``matching is case-insensitive`` () =
    %(ModeCycle.Next(modes, "PLAN")).Should().Be("default")

[<Fact>]
let ``an empty catalog has no next mode`` () =
    %(ModeCycle.Next([||], "plan")).Should().BeNull()
