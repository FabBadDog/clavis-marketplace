module FabioSoft.Nucleus.WpfHost.Tests.LayoutMigrationTests

open System
open System.Collections.Generic
open FabioSoft.Clavis.Rendering
open FabioSoft.Nucleus.Plugins.WpfHost
open Faqt
open Faqt.Operators
open Xunit

let private slot id kind state =
    { PanelId = id; PanelKind = kind; Title = "t"; SavedState = state }

let private leafOf kind = DockingModel.leaf (Guid.NewGuid()) [| slot (Guid.NewGuid()) kind "" |] 0

let private bounds left = PersistedWindowState(left, 0.0, 800.0, 600.0, false)

/// A version-1 layout as written before workspaces existed: every window carried its own tree inline.
let private legacy () =
    let primary = Guid.NewGuid()
    let secondary = Guid.NewGuid()
    let document = LayoutV1(Version = 1)
    document.Windows.Add(
        LayoutV1Window(
            WindowId = primary, IsPrimary = true, Bounds = bounds 0.0, Layout = leafOf "conversation",
            SlideIns = ResizeArray [ PersistedSlideIn(Guid.NewGuid(), "git-log", "git log", "left", "") ]))
    document.Windows.Add(
        LayoutV1Window(
            WindowId = secondary, IsPrimary = false, Bounds = bounds 1280.0, Layout = leafOf "code-editor"))
    document, primary, secondary

[<Fact>]
let ``a version-1 layout migrates every window and its tree`` () =

    // Arrange
    let document, primary, secondary = legacy ()

    // Act
    let migrated = LayoutMigration.FromVersion1 document

    // Assert
    %migrated.Version.Should().Be(LayoutFile.CurrentVersion)
    %migrated.Windows.Count.Should().Be(2)
    %migrated.Layouts.Count.Should().Be(2)
    %(migrated.Windows |> Seq.find (fun w -> w.WindowId = primary)).Role.Should().Be(WindowRole.Primary)
    %(migrated.Windows |> Seq.find (fun w -> w.WindowId = secondary)).Role.Should().Be(WindowRole.Panel)
    %(migrated.For(secondary, Guid.Empty)).Layout.Panels[0].PanelKind.Should().Be("code-editor")

[<Fact>]
let ``migration keeps geometry on the window and slide-ins with the layout`` () =

    // Arrange
    let document, primary, secondary = legacy ()

    // Act
    let migrated = LayoutMigration.FromVersion1 document

    // Assert
    %(migrated.Windows |> Seq.find (fun w -> w.WindowId = secondary)).Bounds.Left.Should().Be(1280.0)
    %(migrated.For(primary, Guid.Empty)).SlideIns.Count.Should().Be(1)
    %(migrated.For(primary, Guid.Empty)).SlideIns[0].Kind.Should().Be("git-log")

[<Fact>]
let ``a migrated layout is unassigned, not orphaned`` () =

    // Arrange
    let document, _, _ = legacy ()

    // Act - the host does not know a workspace while reading the file
    let migrated = LayoutMigration.FromVersion1 document

    // Assert
    %migrated.ActiveWorkspaceId.Should().Be(Guid.Empty)
    %(migrated.Layouts |> Seq.forall (fun l -> l.WorkspaceId = Guid.Empty)).Should().BeTrue()

[<Fact>]
let ``adopt binds an unassigned layout to the active workspace`` () =

    // Arrange
    let document, _, secondary = legacy ()
    let migrated = LayoutMigration.FromVersion1 document
    let workspaceId = Guid.NewGuid()

    // Act
    let adopted = LayoutMigration.Adopt(migrated, workspaceId)

    // Assert
    %adopted.ActiveWorkspaceId.Should().Be(workspaceId)
    %(adopted.Layouts |> Seq.forall (fun l -> l.WorkspaceId = workspaceId)).Should().BeTrue()
    %(adopted.Windows |> Seq.find (fun w -> w.WindowId = secondary)).WorkspaceId.Should().Be(workspaceId)

[<Fact>]
let ``adopt binds the chrome window to the workspace like any other`` () =

    // Arrange
    let document, primary, _ = legacy ()
    let workspaceId = Guid.NewGuid()

    // Act
    let adopted = LayoutMigration.Adopt(LayoutMigration.FromVersion1 document, workspaceId)

    // Assert - a workspace owns its own window now, so the chrome window belongs to one like every other
    %(adopted.Windows |> Seq.find (fun w -> w.WindowId = primary)).WorkspaceId.Should().Be(workspaceId)

[<Fact>]
let ``adopt never rebinds a layout that already names a workspace`` () =

    // Arrange
    let existing = Guid.NewGuid()
    let windowId = Guid.NewGuid()
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion, existing,
            [| PersistedWindow(windowId, WindowRole.Primary, Guid.Empty, bounds 0.0) |],
            [| PersistedWorkspaceLayout(windowId, existing, leafOf "chat") |])

    // Act
    let adopted = LayoutMigration.Adopt(saved, Guid.NewGuid())

    // Assert
    %adopted.ActiveWorkspaceId.Should().Be(existing)
    %adopted.Layouts[0].WorkspaceId.Should().Be(existing)

[<Fact>]
let ``adopt with an empty workspace id is a no-op`` () =

    // Arrange
    let migrated = LayoutMigration.FromVersion1(let d, _, _ = legacy () in d)

    // Act
    let adopted = LayoutMigration.Adopt(migrated, Guid.Empty)

    // Assert
    %Object.ReferenceEquals(adopted, migrated).Should().BeTrue()

[<Fact>]
let ``orphan layouts of closed workspaces are dropped`` () =

    // Arrange - one live workspace, one that no longer exists
    let live = Guid.NewGuid()
    let closed = Guid.NewGuid()
    let primaryId = Guid.NewGuid()
    let orphanWindow = Guid.NewGuid()
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion, live,
            [| PersistedWindow(primaryId, WindowRole.Primary, Guid.Empty, bounds 0.0)
               PersistedWindow(orphanWindow, WindowRole.Panel, closed, bounds 1280.0) |],
            [| PersistedWorkspaceLayout(primaryId, live, leafOf "chat")
               PersistedWorkspaceLayout(orphanWindow, closed, leafOf "code-editor") |])

    // Act
    let pruned = LayoutMigration.DropOrphans(saved, List [ live ])

    // Assert
    %pruned.Windows.Count.Should().Be(1)
    %pruned.Layouts.Count.Should().Be(1)
    %pruned.Layouts[0].WorkspaceId.Should().Be(live)

[<Fact>]
let ``dropping orphans keeps unassigned entries for adoption`` () =

    // Arrange
    let migrated = LayoutMigration.FromVersion1(let d, _, _ = legacy () in d)

    // Act - nothing is live yet, but an unassigned layout must survive to be adopted
    let pruned = LayoutMigration.DropOrphans(migrated, List<Guid>())

    // Assert
    %pruned.Layouts.Count.Should().Be(2)
    %pruned.Windows.Count.Should().Be(2)

let private savedFor windowId workspaceId layout =
    PersistedLayout(
        LayoutFile.CurrentVersion, workspaceId,
        [| PersistedWindow(windowId, WindowRole.Primary, workspaceId, bounds 0.0) |],
        [| PersistedWorkspaceLayout(windowId, workspaceId, layout) |])

[<Fact>]
let ``rebinding points a workspace's saved window at the live one`` () =

    // Arrange - window ids are minted per launch, so the saved one is never the live one
    let workspaceId = Guid.NewGuid()
    let saved = savedFor (Guid.NewGuid()) workspaceId (leafOf "chat")
    let live = Guid.NewGuid()

    // Act
    let rebound = LayoutMigration.RebindWorkspaceWindow(saved, workspaceId, live)

    // Assert - both the window row and its docking tree follow, or the tree is stranded on a dead id
    %rebound.Windows[0].WindowId.Should().Be(live)
    %rebound.Layouts[0].WindowId.Should().Be(live)

[<Fact>]
let ``a rebound layout is restorable, so its workspace is not seeded a second chat`` () =

    // Arrange - the observed defect: a saved chat that no live window claimed, so a fresh one was seeded
    // beside it and the workspace ended up with two
    let workspaceId = Guid.NewGuid()
    let saved = savedFor (Guid.NewGuid()) workspaceId (leafOf "chat")
    let live = Guid.NewGuid()

    // Act
    let rebound = LayoutMigration.RebindWorkspaceWindow(saved, workspaceId, live)

    // Assert
    %LayoutMigration.NeedsDefaultPanels(saved, workspaceId, List [ live ]).Should().BeTrue()
    %LayoutMigration.NeedsDefaultPanels(rebound, workspaceId, List [ live ]).Should().BeFalse()

[<Fact>]
let ``rebinding leaves another workspace's window alone`` () =

    // Arrange
    let mine = Guid.NewGuid()
    let other = Guid.NewGuid()
    let otherWindow = Guid.NewGuid()
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion, mine,
            [| PersistedWindow(Guid.NewGuid(), WindowRole.Primary, mine, bounds 0.0)
               PersistedWindow(otherWindow, WindowRole.Primary, other, bounds 1280.0) |],
            [| PersistedWorkspaceLayout(otherWindow, other, leafOf "chat") |])

    // Act
    let rebound = LayoutMigration.RebindWorkspaceWindow(saved, mine, Guid.NewGuid())

    // Assert
    %(rebound.Windows |> Seq.find (fun w -> w.WorkspaceId = other)).WindowId.Should().Be(otherWindow)
    %rebound.Layouts[0].WindowId.Should().Be(otherWindow)

[<Fact>]
let ``rebinding a workspace with nothing saved is a no-op`` () =

    // Arrange
    let saved = savedFor (Guid.NewGuid()) (Guid.NewGuid()) (leafOf "chat")

    // Act
    let rebound = LayoutMigration.RebindWorkspaceWindow(saved, Guid.NewGuid(), Guid.NewGuid())

    // Assert
    %Object.ReferenceEquals(rebound, saved).Should().BeTrue()

[<Fact>]
let ``a workspace with a restorable layout is not seeded with defaults`` () =

    // Arrange
    let windowId = Guid.NewGuid()
    let workspaceId = Guid.NewGuid()
    let saved = savedFor windowId workspaceId (leafOf "chat")

    // Act & Assert
    %LayoutMigration.NeedsDefaultPanels(saved, workspaceId, List [ windowId ]).Should().BeFalse()

[<Fact>]
let ``an empty saved layout is taken at its word`` () =

    // Arrange - the chat was closed, so restoring must leave it closed
    let windowId = Guid.NewGuid()
    let workspaceId = Guid.NewGuid()
    let saved = savedFor windowId workspaceId (DockingModel.leaf (Guid.NewGuid()) [||] 0)

    // Act & Assert
    %LayoutMigration.NeedsDefaultPanels(saved, workspaceId, List [ windowId ]).Should().BeFalse()

[<Fact>]
let ``a layout naming a window that is gone restores nothing, so defaults are seeded`` () =

    // Arrange - the observed failure: the only entry with panels named a window not in the file
    let workspaceId = Guid.NewGuid()
    let saved = savedFor (Guid.NewGuid()) workspaceId (leafOf "chat")

    // Act & Assert
    %LayoutMigration.NeedsDefaultPanels(saved, workspaceId, List [ Guid.NewGuid() ]).Should().BeTrue()

[<Fact>]
let ``a workspace with nothing saved is seeded with defaults`` () =

    // Arrange - the second workspace you open has no layout of its own, and needs a chat just as much
    let windowId = Guid.NewGuid()
    let saved = savedFor windowId (Guid.NewGuid()) (leafOf "chat")

    // Act & Assert
    %LayoutMigration.NeedsDefaultPanels(saved, Guid.NewGuid(), List [ windowId ]).Should().BeTrue()

[<Fact>]
let ``a launch with no saved layout at all is seeded with defaults`` () =

    // Act & Assert
    %LayoutMigration.NeedsDefaultPanels(null, Guid.NewGuid(), List<Guid>()).Should().BeTrue()

[<Fact>]
let ``a workspace keeps its own window bounds`` () =

    // Arrange - two workspaces in one window, each having moved it somewhere different
    let windowId = Guid.NewGuid()
    let first = Guid.NewGuid()
    let second = Guid.NewGuid()
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion, first,
            [| PersistedWindow(windowId, WindowRole.Primary, Guid.Empty, bounds 0.0) |],
            [| PersistedWorkspaceLayout(windowId, first, leafOf "chat", Bounds = bounds 100.0)
               PersistedWorkspaceLayout(windowId, second, leafOf "chat", Bounds = bounds 900.0) |])

    // Act & Assert
    %(saved.For(windowId, first)).Bounds.Left.Should().Be(100.0)
    %(saved.For(windowId, second)).Bounds.Left.Should().Be(900.0)

[<Fact>]
let ``a workspace that has never been on screen carries no bounds of its own`` () =

    // Arrange - geometry written for every workspace up front is just copies waiting to drift
    let windowId = Guid.NewGuid()
    let workspaceId = Guid.NewGuid()
    let saved =
        PersistedLayout(
            LayoutFile.CurrentVersion, workspaceId,
            [| PersistedWindow(windowId, WindowRole.Primary, Guid.Empty, bounds 42.0) |],
            [| PersistedWorkspaceLayout(windowId, workspaceId, leafOf "chat") |])

    // Act & Assert - the window's standing position stands in
    %(obj.ReferenceEquals((saved.For(windowId, workspaceId)).Bounds, null)).Should().BeTrue()
    %saved.Windows[0].Bounds.Left.Should().Be(42.0)
