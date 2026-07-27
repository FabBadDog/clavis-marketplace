module FabioSoft.Nucleus.Workspaces.Tests.WorkspaceBindingsTests

open FabioSoft.Contracts.Keymap
open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``one binding per keyboard-switchable slot, plus the overview`` () =

    // Act & Assert - F1..F11 and F12
    %WorkspaceBindings.Defaults.Count.Should().Be(WorkspaceSet.SlotCount + 1)

[<Fact>]
let ``each slot binding activates its own slot`` () =

    // Act
    let bySlot =
        [ 1 .. WorkspaceSet.SlotCount ]
        |> List.map (fun slot ->
            WorkspaceBindings.Defaults |> Seq.tryFind (fun b -> b.Gesture = $"F{slot}"))

    // Assert
    %(bySlot |> List.forall Option.isSome).Should().BeTrue()
    %(bySlot
      |> List.mapi (fun index binding -> binding.Value.Command = $"ActivateWorkspaceSlot {index + 1}")
      |> List.forall id).Should().BeTrue()

[<Fact>]
let ``F12 toggles the overview panel`` () =

    // Act
    let f12 = WorkspaceBindings.Defaults |> Seq.find (fun b -> b.Gesture = "F12")

    // Assert
    %f12.Command.Should().Be("TogglePanel workspace-overview")

[<Fact>]
let ``every binding is application scope, never system`` () =

    // Act & Assert - a system binding registers an OS global hotkey, which would take F1-F12 away from every
    // application on the machine
    %(WorkspaceBindings.Defaults
      |> Seq.forall (fun b -> b.Scope = KeymapScope.Application)).Should().BeTrue()

[<Fact>]
let ``no binding is panel-scoped`` () =

    // Act & Assert - switching a workspace must work whatever panel holds focus
    %(WorkspaceBindings.Defaults |> Seq.forall (fun b -> b.PanelKind = "")).Should().BeTrue()

[<Fact>]
let ``no two bindings claim the same gesture`` () =

    // Act
    let distinct = WorkspaceBindings.Defaults |> Seq.map (fun b -> b.Gesture) |> Seq.distinct |> Seq.length

    // Assert
    %distinct.Should().Be(WorkspaceBindings.Defaults.Count)
