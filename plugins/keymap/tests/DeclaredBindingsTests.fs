module FabioSoft.Nucleus.KeyMap.Tests.DeclaredBindingsTests

open System.Collections.Generic
open FabioSoft.Contracts.Keymap
open FabioSoft.Nucleus.Plugins.KeyMap
open Faqt
open Faqt.Operators
open Xunit

let private app gesture command = KeyBinding(gesture, command, KeymapScope.Application, "")

let private none : IReadOnlyList<KeyBinding> = List<KeyBinding>() :> _

let private list (bindings: KeyBinding list) : IReadOnlyList<KeyBinding> = List<KeyBinding>(bindings) :> _

let private gestureFor command (merged: IReadOnlyList<KeyBinding>) =
    merged |> Seq.tryFind (fun b -> b.Command = command) |> Option.map (fun b -> b.Gesture)

[<Fact>]
let ``a plugin declaration is merged in alongside the built-in defaults`` () =

    // Act
    let merged = KeymapBindings.Merge(none, list [ app "F5" "ActivateWorkspaceSlot 5" ])

    // Assert - the declaration is present and the built-ins survive
    %(gestureFor "ActivateWorkspaceSlot 5" merged).Should().Be(Some "F5")
    %(gestureFor "ToggleCommandPalette" merged).Should().Be(Some "Ctrl+Shift+P")

[<Fact>]
let ``a user rebinding beats a plugin declaration`` () =

    // Arrange
    let declared = list [ app "F5" "ActivateWorkspaceSlot 5" ]
    let persisted = list [ app "Ctrl+5" "ActivateWorkspaceSlot 5" ]

    // Act
    let merged = KeymapBindings.Merge(persisted, declared)

    // Assert - precedence is user > plugin declaration > built-in
    %(gestureFor "ActivateWorkspaceSlot 5" merged).Should().Be(Some "Ctrl+5")

[<Fact>]
let ``a plugin declaration beats the built-in default for the same command`` () =

    // Arrange - re-declare a command that ships a built-in gesture
    let declared = list [ app "Ctrl+Alt+P" "ToggleCommandPalette" ]

    // Act
    let merged = KeymapBindings.Merge(none, declared)

    // Assert - one entry for the command, and it is the declared gesture
    %(merged |> Seq.filter (fun b -> b.Command = "ToggleCommandPalette") |> Seq.length).Should().Be(1)
    %(gestureFor "ToggleCommandPalette" merged).Should().Be(Some "Ctrl+Alt+P")

[<Fact>]
let ``a gesture claimed by an earlier declaration wins, and the later one is dropped`` () =

    // Arrange - two plugins wanting F5
    let declared = list [ app "F5" "FirstClaim"; app "F5" "SecondClaim" ]

    // Act
    let merged = KeymapBindings.Merge(none, declared)

    // Assert - first declaration wins; the loser is not silently shadowing it
    %(gestureFor "FirstClaim" merged).Should().Be(Some "F5")
    %(gestureFor "SecondClaim" merged).Should().Be(None)

[<Fact>]
let ``declaring nothing leaves the built-in defaults exactly as they were`` () =

    // Act
    let merged = KeymapBindings.Merge(none, none)

    // Assert
    %merged.Count.Should().Be(KeymapBindings.Defaults.Count)

[<Fact>]
let ``the one-argument merge still behaves as before`` () =

    // Act - the overload used by everything that does not declare
    let merged = KeymapBindings.Merge(list [ app "Ctrl+J" "ToggleCommandPalette" ])

    // Assert
    %(gestureFor "ToggleCommandPalette" merged).Should().Be(Some "Ctrl+J")

[<Fact>]
let ``a persisted binding for an unknown command is kept`` () =

    // Act
    let merged = KeymapBindings.Merge(list [ app "Ctrl+Q" "SomeAgentCommand" ], none)

    // Assert
    %(gestureFor "SomeAgentCommand" merged).Should().Be(Some "Ctrl+Q")
