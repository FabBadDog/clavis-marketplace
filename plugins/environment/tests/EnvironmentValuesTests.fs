module FabioSoft.Nucleus.Plugins.Environment.Tests.EnvironmentValuesTests

open System
open FabioSoft.Nucleus.Plugins.Environment
open Faqt
open Faqt.Operators
open Xunit

let private git =
    GitRaw("feature/x", " M a\n", "3\t1\ta\n", "0\t2", "clavis", "origin/main", "Fabio", "2.45.1", "", true)

let private now = DateTimeOffset(2026, 6, 9, 12, 48, 31, TimeSpan.Zero)

[<Fact>]
let ``builds cwd, time and version values`` () =

    // Act
    let values =
        EnvironmentValues.Build("C:\\Users\\fhertell\\Repos\\FS\\clavis", "C:\\Users\\fhertell", git, now, "v0.4.1")

    // Assert
    %values["cwd.name"].Should().Be("clavis")
    %values["cwd.short"].Should().Be("~\\Repos\\FS\\clavis")
    %values["clavis.version"].Should().Be("v0.4.1")
    %values["time.date"].Should().Be("2026-06-09")

[<Fact>]
let ``builds git values when inside a repository`` () =

    // Act
    let values = EnvironmentValues.Build("C:\\x", "C:\\home", git, now, "v0")

    // Assert
    %values["git.branch"].Should().Be("feature/x")
    %values["git.dirtyStar"].Should().Be("★")
    %values["git.addedLines"].Should().Be("3")
    %values["git.removedLines"].Should().Be("1")
    %values["git.ahead"].Should().Be("2")
    %values["git.upstream"].Should().Be("origin/main")

[<Fact>]
let ``git values are present but empty when not a repository`` () =

    // Arrange
    let bare = GitRaw("", "", "", "", "", "", "", "", "", false)

    // Act
    let values = EnvironmentValues.Build("C:\\x", "C:\\home", bare, now, "v0")

    // Assert
    %values.ContainsKey("git.branch").Should().BeTrue()
    %values["git.branch"].Should().Be("")
    %values["git.dirtyStar"].Should().Be("")
