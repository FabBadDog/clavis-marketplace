module FabioSoft.Nucleus.Workspaces.Tests.WorkspaceFileTests

open System
open FabioSoft.Nucleus.Plugins.Workspaces
open Faqt
open Faqt.Operators
open Xunit

let private fallbackDirectory = "C:\\fallback"

let private create name directory slot (set: WorkspaceSet) =
    let struct (updated, _) =
        WorkspaceUpdate.Create(set, name, directory, AccentPalette.Assign(Seq.empty), slot)
    updated

[<Fact>]
let ``a round trip preserves names, directories, accents and slot gaps`` () =

    // Arrange - slots 1, 3 and 7, so the gaps have to survive
    let original =
        WorkspaceSet.Empty
        |> create "Reviews" "C:\\reviews" 1
        |> create "Spikes" "C:\\spikes" 3
        |> create "Docs" "C:\\docs" 7

    // Act
    let restored = WorkspaceFile.Parse(WorkspaceFile.Serialize original, fallbackDirectory)

    // Assert
    %(restored.InSlotOrder() |> Seq.map (fun w -> w.Slot) |> Seq.toList).Should().SequenceEqual([ 1; 3; 7 ])
    %(restored.InSlotOrder() |> Seq.map (fun w -> w.Name) |> Seq.toList)
        .Should().SequenceEqual([ "Reviews"; "Spikes"; "Docs" ])
    %(restored.BySlot 3).WorkingDirectory.Should().Be("C:\\spikes")
    %(restored.BySlot 3).AccentKey.Should().Be((original.BySlot 3).AccentKey)

[<Fact>]
let ``a round trip preserves which workspace was active`` () =

    // Arrange
    let original = WorkspaceSet.Empty |> create "one" "C:\\a" 1 |> create "two" "C:\\b" 2
    let struct (activated, _) = WorkspaceUpdate.Activate(original, (original.BySlot 1).WorkspaceId)

    // Act
    let restored = WorkspaceFile.Parse(WorkspaceFile.Serialize activated, fallbackDirectory)

    // Assert
    %restored.Active.Name.Should().Be("one")

[<Fact>]
let ``nothing live is persisted`` () =

    // Arrange
    let original = WorkspaceSet.Empty |> create "one" "C:\\a" 1
    let struct (started, _) =
        WorkspaceUpdate.SessionStarted(original, original.ActiveWorkspaceId, Guid.NewGuid())
    let struct (working, _) =
        WorkspaceUpdate.ApplyActivity(started, (started.BySlot 1).SessionId, WorkspaceActivity.Working, "Bash")

    // Act
    let restored = WorkspaceFile.Parse(WorkspaceFile.Serialize working, fallbackDirectory)

    // Assert
    %(restored.BySlot 1).SessionId.Should().Be(Guid.Empty)
    %(restored.BySlot 1).Activity.Should().Be(WorkspaceActivity.Idle)

[<Theory>]
[<InlineData("")>]
[<InlineData("   ")>]
[<InlineData(null)>]
[<InlineData("workspaces: []")>]
[<InlineData("active: null")>]
let ``a section with nothing usable yields an empty set`` (yaml: string) =

    // Act
    let parsed = WorkspaceFile.Parse(yaml, fallbackDirectory)

    // Assert
    %parsed.Workspaces.Should().BeEmpty()

[<Fact>]
let ``an entry without a usable id is dropped, the rest survive`` () =

    // Arrange
    let good = Guid.NewGuid()
    let yaml =
        String.concat "\n"
            [ "workspaces:"
              "  - id: not-a-guid"
              "    name: Broken"
              "    slot: 1"
              $"  - id: {good}"
              "    name: Fine"
              "    slot: 2" ]

    // Act
    let parsed = WorkspaceFile.Parse(yaml, fallbackDirectory)

    // Assert
    %parsed.Workspaces.Should().HaveLength(1)
    %parsed.Active.Name.Should().Be("Fine")

[<Fact>]
let ``a duplicated slot demotes the later entry rather than shadowing the first`` () =

    // Arrange
    let first = Guid.NewGuid()
    let second = Guid.NewGuid()
    let yaml =
        String.concat "\n"
            [ "workspaces:"
              $"  - id: {first}"
              "    name: First"
              "    slot: 2"
              $"  - id: {second}"
              "    name: Second"
              "    slot: 2" ]

    // Act
    let parsed = WorkspaceFile.Parse(yaml, fallbackDirectory)

    // Assert
    %(parsed.BySlot 2).Name.Should().Be("First")
    %(parsed.Workspaces |> Seq.find (fun w -> w.Name = "Second")).Slot.Should().Be(0)

[<Fact>]
let ``an entry with no directory falls back to the launch directory`` () =

    // Arrange
    let yaml = String.concat "\n" [ "workspaces:"; $"  - id: {Guid.NewGuid()}"; "    name: Bare"; "    slot: 1" ]

    // Act
    let parsed = WorkspaceFile.Parse(yaml, fallbackDirectory)

    // Assert
    %(parsed.BySlot 1).WorkingDirectory.Should().Be(fallbackDirectory)

[<Fact>]
let ``a persisted active id that no longer exists falls back to the first in slot order`` () =

    // Arrange
    let live = Guid.NewGuid()
    let yaml =
        String.concat "\n"
            [ $"active: {Guid.NewGuid()}"
              "workspaces:"
              $"  - id: {live}"
              "    name: Only"
              "    slot: 4" ]

    // Act
    let parsed = WorkspaceFile.Parse(yaml, fallbackDirectory)

    // Assert
    %parsed.ActiveWorkspaceId.Should().Be(live)

[<Fact>]
let ``the default set is one workspace in slot one in the launch directory`` () =

    // Act
    let defaulted = WorkspaceFile.Default fallbackDirectory

    // Assert
    %defaulted.Workspaces.Should().HaveLength(1)
    %defaulted.Active.Slot.Should().Be(1)
    %defaulted.Active.WorkingDirectory.Should().Be(fallbackDirectory)
    %defaulted.Active.SessionId.Should().Be(Guid.Empty)
