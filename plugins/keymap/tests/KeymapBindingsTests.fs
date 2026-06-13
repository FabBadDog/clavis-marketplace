module FabioSoft.Nucleus.KeyMap.Tests.KeymapBindingsTests

open System.Collections.Generic
open FabioSoft.Contracts.Keymap
open FabioSoft.Nucleus.Plugins.KeyMap
open Faqt
open Faqt.Operators
open Xunit

let private gesture command (bindings: IReadOnlyList<KeyBinding>) =
    bindings |> Seq.tryFind (fun b -> b.Command = command) |> Option.map _.Gesture

[<Fact>]
let ``defaults bind the palette toggle to ctrl shift p`` () =

    // Act / Assert
    %(gesture "ToggleCommandPalette" KeymapBindings.Defaults).Should().Be(Some "Ctrl+Shift+P")

[<Fact>]
let ``set adds a binding to an empty set`` () =

    // Act
    let result = KeymapBindings.Set([], "NewWindow", KeymapScope.Application, "", "ctrl+n")

    // Assert
    %result.Count.Should().Be(1)
    %result[0].Gesture.Should().Be("Ctrl+N")
    %result[0].Command.Should().Be("NewWindow")

[<Fact>]
let ``set moves an existing command to the new gesture`` () =

    // Arrange
    let start: IReadOnlyList<KeyBinding> = [| KeyBinding("Ctrl+E", "ToggleShortcutHelp", KeymapScope.Application, "") |]

    // Act
    let result = KeymapBindings.Set(start, "ToggleShortcutHelp", KeymapScope.Application, "", "ctrl+h")

    // Assert
    %result.Count.Should().Be(1)
    %result[0].Gesture.Should().Be("Ctrl+H")

[<Fact>]
let ``set rebinds a gesture so it maps to a single command`` () =

    // Arrange
    let start: IReadOnlyList<KeyBinding> = [| KeyBinding("Ctrl+E", "ToggleShortcutHelp", KeymapScope.Application, "") |]

    // Act
    let result = KeymapBindings.Set(start, "SomethingElse", KeymapScope.Application, "", "Ctrl+E")

    // Assert
    %result.Count.Should().Be(1)
    %result[0].Command.Should().Be("SomethingElse")

[<Fact>]
let ``set keeps bindings in other scopes when rebinding a gesture`` () =

    // Arrange
    let start: IReadOnlyList<KeyBinding> =
        [| KeyBinding("Ctrl+E", "AppCommand", KeymapScope.Application, "")
           KeyBinding("Ctrl+E", "PanelCommand", KeymapScope.Panel, "events") |]

    // Act
    let result = KeymapBindings.Set(start, "NewApp", KeymapScope.Application, "", "Ctrl+E")

    // Assert - the panel-scope Ctrl+E is untouched
    %result.Count.Should().Be(2)
    %(gesture "PanelCommand" result).Should().Be(Some "Ctrl+E")

[<Fact>]
let ``remove deletes the matching gesture in the scope`` () =

    // Arrange
    let start: IReadOnlyList<KeyBinding> = [| KeyBinding("Ctrl+E", "ToggleShortcutHelp", KeymapScope.Application, "") |]

    // Act
    let result = KeymapBindings.Remove(start, "ctrl+e", KeymapScope.Application, "")

    // Assert
    %result.Count.Should().Be(0)

[<Fact>]
let ``conflicts reports a gesture bound twice in the same scope`` () =

    // Arrange
    let bindings: IReadOnlyList<KeyBinding> =
        [| KeyBinding("Ctrl+E", "CommandA", KeymapScope.Application, "")
           KeyBinding("Ctrl+E", "CommandB", KeymapScope.Application, "") |]

    // Act
    let conflicts = KeymapBindings.Conflicts(bindings)

    // Assert
    %conflicts.Should().HaveLength(1)
    %conflicts[0].Should().Be("Ctrl+E")

[<Fact>]
let ``conflicts ignores the same gesture across different scopes`` () =

    // Arrange
    let bindings: IReadOnlyList<KeyBinding> =
        [| KeyBinding("Ctrl+E", "CommandA", KeymapScope.Application, "")
           KeyBinding("Ctrl+E", "CommandB", KeymapScope.System, "") |]

    // Act / Assert
    %KeymapBindings.Conflicts(bindings).Should().HaveLength(0)

[<Fact>]
let ``merge with no persisted bindings yields the defaults`` () =

    // Act / Assert
    %KeymapBindings.Merge([]).Count.Should().Be(KeymapBindings.Defaults.Count)

[<Fact>]
let ``merge honours a user rebind of a default command`` () =

    // Arrange
    let persisted: IReadOnlyList<KeyBinding> =
        [| KeyBinding("Ctrl+J", "ToggleCommandPalette", KeymapScope.Application, "") |]

    // Act / Assert - the user's gesture replaces the default for that command
    %(gesture "ToggleCommandPalette" (KeymapBindings.Merge(persisted))).Should().Be(Some "Ctrl+J")

[<Fact>]
let ``merge keeps a user binding for a non-default command`` () =

    // Arrange
    let persisted: IReadOnlyList<KeyBinding> =
        [| KeyBinding("Ctrl+Shift+X", "MyCustomCommand", KeymapScope.Application, "") |]

    // Act
    let merged = KeymapBindings.Merge(persisted)

    // Assert - the custom binding survives and the defaults are still present
    %(gesture "MyCustomCommand" merged).Should().Be(Some "Ctrl+Shift+X")
    %(gesture "ToggleCommandPalette" merged).Should().Be(Some "Ctrl+Shift+P")

[<Fact>]
let ``merge keeps a renamed default bound when a stale persisted entry holds its gesture`` () =

    // Arrange - a persisted file from an older build still binds Left to a since-renamed command
    let persisted: IReadOnlyList<KeyBinding> =
        [| KeyBinding("Left", "events.cycle.left", KeymapScope.Panel, "events") |]

    // Act
    let merged = KeymapBindings.Merge(persisted)

    // Assert - the live default still owns Left and leads the stale entry, so first-match resolution wins
    let firstLeftInEvents =
        merged
        |> Seq.tryFind (fun b -> b.Gesture = "Left" && b.PanelKind = "events")
        |> Option.map _.Command

    %firstLeftInEvents.Should().Be(Some "events.severity.left")
