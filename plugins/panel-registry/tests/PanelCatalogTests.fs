module FabioSoft.Nucleus.PanelRegistry.Tests.PanelCatalogTests

open System
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Plugins.PanelRegistry
open Faqt
open Faqt.Operators
open Xunit

let private registration kind title (factory: PanelInstanceContext -> obj) =
    PanelKindRegistration(kind, title, 100.0, 50.0, "", true, Func<PanelInstanceContext, obj>(factory))

let private noCallback = Func<Guid, Action<string>>(fun _ -> Action<string>(fun _ -> ()))

[<Fact>]
let ``resolves a registered kind into a ready instance`` () =

    // Arrange
    let catalog = PanelCatalog()
    let sentinel = obj ()
    catalog.Register(registration "git-log" "git log" (fun _ -> sentinel))
    let instanceId = Guid.NewGuid()

    // Act
    let success, ready = catalog.TryResolve("git-log", instanceId, "", noCallback)

    // Assert
    %success.Should().BeTrue()
    %ready.InstanceId.Should().Be(instanceId)
    %ready.Kind.Should().Be("git-log")
    %ready.Title.Should().Be("git log")
    %ready.MinWidth.Should().Be(100.0)
    %Object.ReferenceEquals(ready.View.Invoke(), sentinel).Should().BeTrue()

[<Fact>]
let ``threads the instance id, saved state, and state callback into the panel context`` () =

    // Arrange
    let catalog = PanelCatalog()
    let mutable captured = Unchecked.defaultof<PanelInstanceContext>
    catalog.Register(registration "markdown" "Notes" (fun context -> captured <- context; obj ()))
    let instanceId = Guid.NewGuid()
    let mutable stateSeen = ""
    let callback = Func<Guid, Action<string>>(fun _ -> Action<string>(fun state -> stateSeen <- state))

    // Act
    let success, ready = catalog.TryResolve("markdown", instanceId, "saved-blob", callback)
    ready.View.Invoke() |> ignore

    // Assert
    %success.Should().BeTrue()
    %captured.InstanceId.Should().Be(instanceId)
    %captured.Kind.Should().Be("markdown")
    %captured.SavedState.Should().Be("saved-blob")
    captured.OnStateChanged.Invoke("new-state")
    %stateSeen.Should().Be("new-state")

[<Fact>]
let ``returns false and no instance for an unregistered kind`` () =

    // Arrange
    let catalog = PanelCatalog()

    // Act
    let success, ready = catalog.TryResolve("missing", Guid.NewGuid(), "", noCallback)

    // Assert
    %success.Should().BeFalse()
    %Object.ReferenceEquals(ready, null).Should().BeTrue()

[<Fact>]
let ``lists the registered kinds`` () =

    // Arrange
    let catalog = PanelCatalog()
    catalog.Register(registration "a" "A" (fun _ -> obj ()))
    catalog.Register(registration "b" "B" (fun _ -> obj ()))

    // Act
    let kinds = catalog.Kinds

    // Assert
    %kinds.Count.Should().Be(2)
