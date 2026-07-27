module FabioSoft.Nucleus.Workspaces.Tests.WorkspaceUpdateTests

open System
open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

let private accent = "Accent1Brush"

let private create name (set: WorkspaceSet) =
    let struct (updated, _) = WorkspaceUpdate.Create(set, name, "C:\\work", accent)
    updated

/// A set of n workspaces in slots 1..n, none of which has a session yet.
let private sized count =
    [ 1 .. count ] |> List.fold (fun set index -> create $"w{index}" set) WorkspaceSet.Empty

let private slotOf name (set: WorkspaceSet) =
    (set.Workspaces |> Seq.find (fun workspace -> workspace.Name = name)).Slot

let private idOf name (set: WorkspaceSet) =
    (set.Workspaces |> Seq.find (fun workspace -> workspace.Name = name)).WorkspaceId

let private withSession name sessionId (set: WorkspaceSet) =
    let struct (updated, _) = WorkspaceUpdate.SessionStarted(set, idOf name set, sessionId)
    updated

[<Fact>]
let ``create takes the lowest free slot`` () =

    // Act
    let set = sized 3

    // Assert
    %(slotOf "w1" set).Should().Be(1)
    %(slotOf "w2" set).Should().Be(2)
    %(slotOf "w3" set).Should().Be(3)

[<Fact>]
let ``create activates the new workspace and asks for its session`` () =

    // Act
    let struct (set, effects) = WorkspaceUpdate.Create(WorkspaceSet.Empty, "first", "C:\\work", accent)

    // Assert
    %set.Active.Name.Should().Be("first")
    %(effects |> Seq.exists (fun (e: WorkspaceEffect) -> e :? StartSessionEffect)).Should().BeTrue()
    %(effects |> Seq.exists (fun (e: WorkspaceEffect) -> e :? ActivatedEffect)).Should().BeTrue()

[<Fact>]
let ``close frees the slot and renumbers nothing`` () =

    // Arrange
    let set = sized 4

    // Act
    let struct (closed, _) = WorkspaceUpdate.Close(set, idOf "w2" set)

    // Assert
    %(slotOf "w1" closed).Should().Be(1)
    %(slotOf "w3" closed).Should().Be(3)
    %(slotOf "w4" closed).Should().Be(4)
    %closed.LowestFreeSlot().Should().Be(2)

[<Fact>]
let ``the next create lands in the freed slot`` () =

    // Arrange
    let set = sized 4
    let struct (closed, _) = WorkspaceUpdate.Close(set, idOf "w2" set)

    // Act
    let recreated = create "fresh" closed

    // Assert
    %(slotOf "fresh" recreated).Should().Be(2)

[<Fact>]
let ``closing the active workspace activates a neighbour`` () =

    // Arrange
    let set = sized 3
    let activeId = idOf "w3" set

    // Act
    let struct (closed, _) = WorkspaceUpdate.Close(set, activeId)

    // Assert
    %closed.ActiveWorkspaceId.Should().NotBe(activeId)
    %closed.Active.Name.Should().Be("w1")

[<Fact>]
let ``closing a background workspace leaves the active one alone`` () =

    // Arrange
    let set = sized 3
    let activeId = set.ActiveWorkspaceId

    // Act
    let struct (closed, _) = WorkspaceUpdate.Close(set, idOf "w1" set)

    // Assert
    %closed.ActiveWorkspaceId.Should().Be(activeId)

[<Fact>]
let ``closing the last workspace leaves an empty set, not a refusal`` () =

    // Arrange
    let set = sized 1

    // Act
    let struct (closed, _) = WorkspaceUpdate.Close(set, set.ActiveWorkspaceId)

    // Assert
    %closed.Workspaces.Should().BeEmpty()
    %closed.ActiveWorkspaceId.Should().Be(Guid.Empty)

[<Fact>]
let ``closing disposes the workspace's session`` () =

    // Arrange
    let sessionId = Guid.NewGuid()
    let set = sized 1 |> withSession "w1" sessionId

    // Act
    let struct (_, effects) = WorkspaceUpdate.Close(set, idOf "w1" set)

    // Assert
    %(effects |> Seq.exists (fun (e: WorkspaceEffect) ->
        match e with
        | :? DisposeSessionEffect as d -> d.SessionId = sessionId
        | _ -> false)).Should().BeTrue()

[<Fact>]
let ``closing a workspace that never started a session disposes nothing`` () =

    // Arrange
    let set = sized 1

    // Act
    let struct (_, effects) = WorkspaceUpdate.Close(set, idOf "w1" set)

    // Assert
    %(effects |> Seq.exists (fun (e: WorkspaceEffect) -> e :? DisposeSessionEffect)).Should().BeFalse()

[<Fact>]
let ``activating a free slot creates the workspace there`` () =

    // Act
    let struct (set, _) = WorkspaceUpdate.ActivateSlot(WorkspaceSet.Empty, 5, "C:\\work", accent)

    // Assert
    %set.Active.Slot.Should().Be(5)
    %set.Active.Name.Should().Be("Workspace 5")

[<Fact>]
let ``activating an occupied slot activates its workspace`` () =

    // Arrange
    let set = sized 3

    // Act
    let struct (activated, _) = WorkspaceUpdate.ActivateSlot(set, 1, "C:\\work", accent)

    // Assert
    %activated.Active.Name.Should().Be("w1")
    %activated.Workspaces.Count.Should().Be(3)

[<Theory>]
[<InlineData(0)>]
[<InlineData(-1)>]
[<InlineData(12)>]
let ``activating a slot outside the keyboard range is ignored`` (slot: int) =

    // Arrange
    let set = sized 1

    // Act
    let struct (result, effects) = WorkspaceUpdate.ActivateSlot(set, slot, "C:\\work", accent)

    // Assert
    %Object.ReferenceEquals(result, set).Should().BeTrue()
    %effects.Should().BeEmpty()

[<Fact>]
let ``a workspace created when every slot is taken is slotless`` () =

    // Arrange
    let full = sized WorkspaceSet.SlotCount

    // Act
    let overflowing = create "twelfth" full

    // Assert
    %(slotOf "twelfth" overflowing).Should().Be(0)
    %overflowing.InSlotOrder().Should().HaveLength(12)
    %(overflowing.InSlotOrder() |> Seq.last).Name.Should().Be("twelfth")

[<Fact>]
let ``a session starts exactly once per workspace`` () =

    // Arrange
    let set = sized 2
    let target = idOf "w1" set

    // Act - first activation starts it, the second must not
    let struct (first, firstEffects) = WorkspaceUpdate.Activate(set, target)
    let started = first |> withSession "w1" (Guid.NewGuid())
    let struct (_, secondEffects) = WorkspaceUpdate.Activate(started, target)

    // Assert
    %(firstEffects |> Seq.exists (fun (e: WorkspaceEffect) -> e :? StartSessionEffect)).Should().BeTrue()
    %(secondEffects |> Seq.exists (fun (e: WorkspaceEffect) -> e :? StartSessionEffect)).Should().BeFalse()

[<Fact>]
let ``re-activating the already active workspace changes nothing`` () =

    // Arrange
    let set = sized 1 |> withSession "w1" (Guid.NewGuid())

    // Act
    let struct (result, effects) = WorkspaceUpdate.Activate(set, set.ActiveWorkspaceId)

    // Assert
    %Object.ReferenceEquals(result, set).Should().BeTrue()
    %effects.Should().BeEmpty()

[<Fact>]
let ``activity for an unknown session is ignored`` () =

    // Arrange
    let set = sized 1 |> withSession "w1" (Guid.NewGuid())

    // Act
    let struct (result, _) =
        WorkspaceUpdate.ApplyActivity(set, Guid.NewGuid(), WorkspaceActivity.Working, "Bash")

    // Assert
    %Object.ReferenceEquals(result, set).Should().BeTrue()

[<Fact>]
let ``activity lands on the workspace that owns the session`` () =

    // Arrange
    let sessionId = Guid.NewGuid()
    let set = sized 2 |> withSession "w2" sessionId

    // Act
    let struct (updated, _) =
        WorkspaceUpdate.ApplyActivity(set, sessionId, WorkspaceActivity.Waiting, "permission: Write")

    // Assert
    let w2 = updated.Workspaces |> Seq.find (fun w -> w.Name = "w2")
    let w1 = updated.Workspaces |> Seq.find (fun w -> w.Name = "w1")
    %w2.Activity.Should().Be(WorkspaceActivity.Waiting)
    %w2.ActivityDetail.Should().Be("permission: Write")
    %w1.Activity.Should().Be(WorkspaceActivity.Idle)

[<Fact>]
let ``an unchanged activity report changes nothing`` () =

    // Arrange
    let sessionId = Guid.NewGuid()
    let set = sized 1 |> withSession "w1" sessionId
    let struct (working, _) = WorkspaceUpdate.ApplyActivity(set, sessionId, WorkspaceActivity.Working, "Bash")

    // Act
    let struct (again, _) = WorkspaceUpdate.ApplyActivity(working, sessionId, WorkspaceActivity.Working, "Bash")

    // Assert
    %Object.ReferenceEquals(again, working).Should().BeTrue()

[<Fact>]
let ``rename trims the name and ignores an empty one`` () =

    // Arrange
    let set = sized 1
    let target = idOf "w1" set

    // Act
    let struct (renamed, _) = WorkspaceUpdate.Rename(set, target, "  Reviews  ")
    let struct (untouched, _) = WorkspaceUpdate.Rename(renamed, target, "   ")

    // Assert
    %renamed.Active.Name.Should().Be("Reviews")
    %Object.ReferenceEquals(untouched, renamed).Should().BeTrue()
