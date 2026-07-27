module FabioSoft.Nucleus.PanelRegistry.Tests.PanelCatalogTests

open System
open FabioSoft.Contracts.Layout
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
    catalog.Register(registration "git-log" "git log" (fun _ -> sentinel)) |> ignore
    let instanceId = Guid.NewGuid()

    // Act
    let success, ready = catalog.TryResolve("git-log", instanceId, "", Guid.Empty, noCallback)

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
    catalog.Register(registration "markdown" "Notes" (fun context -> captured <- context; obj ())) |> ignore
    let instanceId = Guid.NewGuid()
    let mutable stateSeen = ""
    let callback = Func<Guid, Action<string>>(fun _ -> Action<string>(fun state -> stateSeen <- state))

    // Act
    let success, ready = catalog.TryResolve("markdown", instanceId, "saved-blob", Guid.Empty, callback)
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
    let success, ready = catalog.TryResolve("missing", Guid.NewGuid(), "", Guid.Empty, noCallback)

    // Assert
    %success.Should().BeFalse()
    %Object.ReferenceEquals(ready, null).Should().BeTrue()

[<Fact>]
let ``lists the registered kinds`` () =

    // Arrange
    let catalog = PanelCatalog()
    catalog.Register(registration "a" "A" (fun _ -> obj ())) |> ignore
    catalog.Register(registration "b" "B" (fun _ -> obj ())) |> ignore

    // Act
    let kinds = catalog.Kinds

    // Assert
    %kinds.Count.Should().Be(2)

[<Fact>]
let ``buffers an open for an unregistered kind and replays it on registration`` () =

    // Arrange
    let catalog = PanelCatalog()
    let instanceId = Guid.NewGuid()
    catalog.Buffer("git-log", instanceId, "saved-blob", Guid.Empty)

    // Act
    let pending = catalog.Register(registration "git-log" "git log" (fun _ -> obj ()))

    // Assert
    %pending.Count.Should().Be(1)
    %pending[0].InstanceId.Should().Be(instanceId)
    %pending[0].SavedState.Should().Be("saved-blob")

[<Fact>]
let ``carries the kind's declared cardinality and the requested workspace onto the ready instance`` () =

    // Arrange
    let catalog = PanelCatalog()
    let chat = registration "chat" "Chat" (fun _ -> obj ())
    chat.Cardinality <- PanelCardinality.OnePerApplication
    catalog.Register chat |> ignore
    let workspaceId = Guid.NewGuid()

    // Act
    let _, ready = catalog.TryResolve("chat", Guid.NewGuid(), "", workspaceId, noCallback)

    // Assert
    %ready.Cardinality.Should().Be(PanelCardinality.OnePerApplication)
    %ready.WorkspaceId.Should().Be(workspaceId)

[<Fact>]
let ``a kind that declares no cardinality resolves to one per workspace`` () =

    // Arrange
    let catalog = PanelCatalog()
    catalog.Register(registration "git-log" "git log" (fun _ -> obj ())) |> ignore

    // Act
    let _, ready = catalog.TryResolve("git-log", Guid.NewGuid(), "", Guid.Empty, noCallback)

    // Assert
    %ready.Cardinality.Should().Be(PanelCardinality.OnePerWorkspace)

[<Fact>]
let ``a buffered open keeps its workspace through the replay`` () =

    // Arrange
    let catalog = PanelCatalog()
    let workspaceId = Guid.NewGuid()
    catalog.Buffer("chat", Guid.NewGuid(), "", workspaceId)

    // Act
    let pending = catalog.Register(registration "chat" "Chat" (fun _ -> obj ()))

    // Assert
    %pending[0].WorkspaceId.Should().Be(workspaceId)

[<Fact>]
let ``registering a kind with nothing buffered returns no pending opens`` () =

    // Arrange
    let catalog = PanelCatalog()

    // Act
    let pending = catalog.Register(registration "git-log" "git log" (fun _ -> obj ()))

    // Assert
    %pending.Count.Should().Be(0)
