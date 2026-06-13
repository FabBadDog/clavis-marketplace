module FabioSoft.Clavis.IntegrationTests.PanelRegistryTests

open System
open Faqt
open Faqt.Operators
open FabioSoft.Clavis.TestKit
open FabioSoft.Contracts.Workspace
open FabioSoft.Nucleus.Contracts
open FabioSoft.Nucleus.Plugins.PanelRegistry
open Xunit

let private bootRegistry () =
    let plugin = PanelRegistryPlugin()
    Harness.boot [ (fun bus -> plugin.ActivateAsync(bus, PanelRegistryConfig())) ]

let private registration kind =
    PanelKindRegistration(kind, $"{kind} title", 100.0, 80.0, "", true, Func<PanelInstanceContext, obj>(fun _ -> obj ()))

[<Fact>]
let ``registers a panel kind and opens a ready instance`` () =
    task {
        // Arrange
        use! harness = bootRegistry ()
        harness.Send(registration "notes")
        // The registration confirmation orders the open after the kind is catalogued (separate channels).
        let! _ = harness.WaitFor<LogEntry>(fun entry -> entry.Message.Contains("Registered panel kind 'notes'"))

        // Act
        harness.Send(OpenPanel "notes")
        let! ready = harness.WaitFor<PanelInstanceReady>(fun message -> message.Kind = "notes")

        // Assert
        %ready.Kind.Should().Be("notes")
        %ready.Title.Should().Be("notes title")
    }

[<Fact>]
let ``restores a panel under its saved instance id`` () =
    task {
        // Arrange
        use! harness = bootRegistry ()
        harness.Send(registration "notes")
        let! _ = harness.WaitFor<LogEntry>(fun entry -> entry.Message.Contains("Registered panel kind 'notes'"))
        let instanceId = Guid.NewGuid()

        // Act
        harness.Send(RestorePanel(instanceId, "notes", "saved-state"))
        let! ready = harness.WaitFor<PanelInstanceReady>(fun message -> message.Kind = "notes")

        // Assert
        %ready.InstanceId.Should().Be(instanceId)
    }

[<Fact>]
let ``warns and opens nothing for an unregistered kind`` () =
    task {
        // Arrange
        use! harness = bootRegistry ()

        // Act
        harness.Send(OpenPanel "ghost")
        let! warning =
            harness.WaitFor<LogEntry>(fun entry -> entry.Level = LogLevel.Warn && entry.Message.Contains("ghost"))

        // Assert
        %warning.Message.Should().Contain("ghost")
        %(harness.Messages<PanelInstanceReady>() |> List.isEmpty).Should().BeTrue()
    }
