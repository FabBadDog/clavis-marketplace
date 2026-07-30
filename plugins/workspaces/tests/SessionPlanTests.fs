module FabioSoft.Nucleus.Workspaces.Tests.SessionPlanTests

open System
open FabioSoft.Contracts.Session
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

let private started = DateTimeOffset(2026, 7, 30, 9, 0, 0, TimeSpan.Zero)

let private instance instanceId name directory isOwned =
    AgentInstance(instanceId, name, directory, "idle", started, false, isOwned)

let private workspace name directory conversation =
    Workspace(
        WorkspaceId = Guid.NewGuid(),
        Name = name,
        AccentKey = "Accent1Brush",
        WorkingDirectory = directory,
        Slot = 1,
        AgentSessionId = conversation)

[<Fact>]
let ``a workspace with no conversation starts a fresh one`` () =

    // Act
    let plan = SessionPlan.For(workspace "Reviews" "C:\\work" "", Array.empty)

    // Assert
    %plan.Should().BeOfType<StartFresh>().WhoseValue.Name.Should().Be("Reviews")

[<Fact>]
let ``a remembered conversation with nothing running it is reopened from its transcript`` () =

    // Act
    let plan = SessionPlan.For(workspace "Reviews" "C:\\work" "provider-7", Array.empty)

    // Assert
    %plan.Should().BeOfType<ResumeConversation>().WhoseValue.AgentSessionId.Should().Be("provider-7")

[<Fact>]
let ``a running agent is taken over rather than resumed`` () =

    // Arrange - the provider refuses to resume a session its agent still holds, and even if it did not, resuming
    // would fork the conversation and lose whatever the running agent has done since
    let instances = [| instance "live-id" "Reviews" "C:\\work" true |]

    // Act
    let plan = SessionPlan.For(workspace "Reviews" "C:\\work" "provider-7", instances)

    // Assert - the running agent wins over the remembered transcript, every time
    %plan.Should().BeOfType<TakeOver>().WhoseValue.InstanceId.Should().Be("live-id")

[<Fact>]
let ``a fleet tab is always a take-over`` () =

    // Arrange - a fleet tab exists only because an agent is running, so it can never fall through to starting
    // something fresh even though it has no conversation of its own recorded
    let tab = FleetAgents.AsTab(instance "foreign" "API Contract" "C:\\other" false)

    // Act
    let plan = SessionPlan.For(tab, Array.empty)

    // Assert
    %plan.Should().BeOfType<TakeOver>().WhoseValue.InstanceId.Should().Be("foreign")

[<Fact>]
let ``an agent that is not this workspace's does not divert it`` () =

    // Arrange - somebody else's agent in another directory must not capture this workspace's activation
    let instances = [| instance "foreign" "API Contract" "C:\\other" false |]

    // Act
    let plan = SessionPlan.For(workspace "Reviews" "C:\\work" "provider-7", instances)

    // Assert
    %plan.Should().BeOfType<ResumeConversation>()
