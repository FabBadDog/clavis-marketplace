module FabioSoft.Nucleus.Plugins.Environment.Tests.GitFactsTests

open FabioSoft.Nucleus.Plugins.Environment
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``counts changed files`` () =
    %GitFacts.ChangedFileCount(" M a.fs\n?? b.fs\n").Should().Be(2)

[<Fact>]
let ``dirty star is set when changes exist`` () =
    %GitFacts.DirtyStar(" M a.fs").Should().Be("★")

[<Fact>]
let ``dirty star is empty when clean`` () =
    %GitFacts.DirtyStar("").Should().Be("")

[<Fact>]
let ``sums numstat added and removed`` () =

    // Act
    let struct (added, removed) = GitFacts.DiffLines("3\t2\ta.fs\n10\t0\tb.fs\n")

    // Assert
    %added.Should().Be(13)
    %removed.Should().Be(2)

[<Fact>]
let ``ignores binary numstat rows`` () =

    // Act
    let struct (added, removed) = GitFacts.DiffLines("-\t-\tbinary.png\n4\t1\ta.fs\n")

    // Assert
    %added.Should().Be(4)
    %removed.Should().Be(1)

[<Fact>]
let ``parses ahead and behind from left-right count`` () =

    // Act
    let struct (ahead, behind) = GitFacts.AheadBehind("0\t2")

    // Assert
    %ahead.Should().Be(2)
    %behind.Should().Be(0)

[<Fact>]
let ``counts stashes`` () =
    %GitFacts.StashCount("stash@{0}: WIP\nstash@{1}: WIP\n").Should().Be(2)
