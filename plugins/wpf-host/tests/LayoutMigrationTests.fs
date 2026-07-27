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
let ``adopt leaves the primary window belonging to no workspace`` () =

    // Arrange
    let document, primary, _ = legacy ()
    let adopted = LayoutMigration.Adopt(LayoutMigration.FromVersion1 document, Guid.NewGuid())

    // Act & Assert - the primary carries the chrome for every workspace, so it belongs to none
    %(adopted.Windows |> Seq.find (fun w -> w.WindowId = primary)).WorkspaceId.Should().Be(Guid.Empty)

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
