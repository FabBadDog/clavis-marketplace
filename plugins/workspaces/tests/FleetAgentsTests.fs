module FabioSoft.Nucleus.Workspaces.Tests.FleetAgentsTests

open System
open FabioSoft.Contracts.Session
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

let private started = DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero)

/// The bridge only ever publishes instances that are safe to take over, so these are all background agents.
let private instance instanceId name directory isOwned =
    AgentInstance(instanceId, name, directory, "idle", started, false, isOwned)

let private workspace name directory =
    Workspace(
        WorkspaceId = Guid.NewGuid(),
        Name = name,
        AccentKey = "Accent1Brush",
        WorkingDirectory = directory,
        Slot = 1)

[<Fact>]
let ``a workspace finds the agent parked under its own name and directory`` () =

    // Arrange - parking gives the agent a new provider id that Clavis never observes, so the name and directory
    // are the only durable link back to the conversation
    let instances = [| instance "new-id" "Reviews" "C:\\work" true; instance "other" "Notes" "C:\\work" true |]

    // Act
    let found = FleetAgents.ParkedFor(workspace "Reviews" "C:\\work", instances)

    // Assert
    %obj.ReferenceEquals(found, null).Should().BeFalse()
    %found.InstanceId.Should().Be("new-id")

[<Fact>]
let ``a trailing separator does not stop a parked agent being recognised`` () =

    // Arrange - the provider echoes a directory back as it was given
    let instances = [| instance "s1" "Reviews" "C:\\work\\" true |]

    // Act & Assert
    %obj.ReferenceEquals(FleetAgents.ParkedFor(workspace "Reviews" "C:\\work", instances), null).Should().BeFalse()

[<Fact>]
let ``an agent in another directory is not this workspace's`` () =

    // Arrange - two workspaces may share a name; the directory is what tells their agents apart
    let instances = [| instance "s1" "Reviews" "C:\\elsewhere" true |]

    // Act & Assert
    %obj.ReferenceEquals(FleetAgents.ParkedFor(workspace "Reviews" "C:\\work", instances), null).Should().BeTrue()

[<Fact>]
let ``an ambiguous name reclaims nothing rather than guessing`` () =

    // Arrange - there is no way to tell which conversation is the workspace's, and attaching it to the wrong one
    // is worse than attaching it to neither. Both still surface as fleet tabs, so nothing is hidden.
    let instances = [| instance "s1" "Reviews" "C:\\work" true; instance "s2" "Reviews" "C:\\work" true |]

    // Act & Assert
    %obj.ReferenceEquals(FleetAgents.ParkedFor(workspace "Reviews" "C:\\work", instances), null).Should().BeTrue()

[<Fact>]
let ``an agent Clavis never parked is not claimed as a workspace's own`` () =

    // Arrange - a foreign agent may coincidentally share a name and directory. It stays adoptable by an explicit
    // pick; it is just not silently claimed as this workspace's conversation.
    let instances = [| instance "s1" "Reviews" "C:\\work" false |]

    // Act & Assert
    %obj.ReferenceEquals(FleetAgents.ParkedFor(workspace "Reviews" "C:\\work", instances), null).Should().BeTrue()

[<Fact>]
let ``a nameless workspace claims nothing`` () =

    // Arrange - every parked agent carries the bare marker as its name, so an empty name would match many
    let instances = [| instance "s1" "" "C:\\work" true |]

    // Act & Assert
    %obj.ReferenceEquals(FleetAgents.ParkedFor(workspace "" "C:\\work", instances), null).Should().BeTrue()

[<Fact>]
let ``the agents no workspace claims become the fleet tabs`` () =

    // Arrange - one agent is this workspace's own parked one, the other two are somebody else's
    let mine = workspace "Reviews" "C:\\work"
    let instances =
        [| instance "mine" "Reviews" "C:\\work" true
           instance "foreign-a" "API Contract" "C:\\other" false
           instance "foreign-b" "Process Cleanup" "C:\\other" false |]

    // Act
    let unclaimed = FleetAgents.Unclaimed([| mine |], instances)

    // Assert - the workspace's own agent is not offered as a tab; it is already its conversation
    %(unclaimed |> Seq.map _.InstanceId |> Seq.toList).Should().SequenceEqual([ "foreign-a"; "foreign-b" ])

[<Fact>]
let ``an already adopted agent is not offered as a tab`` () =

    // Arrange - it is somebody's live session, not something to hand out again
    let adopted = AgentInstance("taken", "Busy", "C:\\other", "idle", started, true, false)

    // Act & Assert
    %FleetAgents.Unclaimed(Array.empty, [| adopted |]).Should().BeEmpty()

[<Fact>]
let ``a fleet tab's identity is derived from the instance so it survives every discovery pass`` () =

    // Arrange - the tab is rebuilt from scratch on each poll. A fresh id each time would change the active tab's
    // identity under the user mid-click, and there is nowhere to persist one: a fleet tab is not persisted.
    let first = FleetAgents.SyntheticWorkspaceId "instance-1"
    let second = FleetAgents.SyntheticWorkspaceId "instance-1"

    // Act & Assert
    %first.Should().Be(second)
    %first.Should().NotBe(FleetAgents.SyntheticWorkspaceId "instance-2")

[<Fact>]
let ``a fleet tab holds no slot and carries the agent's own name`` () =

    // Act
    let tab = FleetAgents.AsTab(instance "abc" "API Contract" "C:\\other" false)

    // Assert - slotless because it is not yours until you take it over
    %tab.Slot.Should().Be(0)
    %tab.Name.Should().Be("API Contract")
    %tab.IsFleetAgent.Should().BeTrue()
    %tab.AgentSessionId.Should().Be("abc")
    %tab.SessionId.Should().Be(Guid.Empty)

[<Theory>]
[<InlineData("busy", WorkspaceActivity.Working)>]
[<InlineData("BUSY", WorkspaceActivity.Working)>]
[<InlineData("idle", WorkspaceActivity.Idle)>]
[<InlineData("unknown", WorkspaceActivity.Idle)>]
let ``a fleet agent's dot reads like any other tab's`` (status: string) (expected: string) =

    // Arrange - only "working" is asserted positively; anything else reads as idle rather than inventing a state
    let reported = AgentInstance("abc", "n", "C:\\other", status, started, false, false)

    // Act & Assert
    %FleetAgents.ActivityOf(reported).Should().Be(expected)
