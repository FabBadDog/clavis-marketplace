module FabioSoft.Nucleus.TaskTracker.Tests.TaskTrackerModelTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.TaskTracker
open Xunit

let private empty: TaskEntry[] = Array.empty

[<Fact>]
let ``Started appends a running entry`` () =

    // Act
    let result = TaskTrackerModel.Started(empty, "t1", "Do a thing", "local_agent")

    // Assert
    %result.Count.Should().Be(1) |> ignore
    %result[0].TaskId.Should().Be("t1") |> ignore
    %result[0].Description.Should().Be("Do a thing") |> ignore
    %result[0].TaskType.Should().Be("local_agent") |> ignore
    %result[0].IsDone.Should().BeFalse()

[<Fact>]
let ``Started on an existing id resets that slot rather than duplicating`` () =

    // Arrange
    let started = TaskTrackerModel.Started(empty, "t1", "first", "local_bash")
    let completed = TaskTrackerModel.Completed(started, "t1", "completed", "first summary")

    // Act
    let result = TaskTrackerModel.Started(completed, "t1", "second", "local_agent")

    // Assert
    %result.Count.Should().Be(1) |> ignore
    %result[0].Description.Should().Be("second") |> ignore
    %result[0].IsDone.Should().BeFalse()

[<Fact>]
let ``Completed flips the matching entry to done with status and summary`` () =

    // Arrange
    let started = TaskTrackerModel.Started(empty, "t1", "Return word", "local_agent")

    // Act
    let result = TaskTrackerModel.Completed(started, "t1", "completed", "alpha")

    // Assert
    %result.Count.Should().Be(1) |> ignore
    %result[0].IsDone.Should().BeTrue() |> ignore
    %result[0].Status.Should().Be("completed") |> ignore
    %result[0].Summary.Should().Be("alpha") |> ignore
    %result[0].Description.Should().Be("Return word")

[<Fact>]
let ``Completed with no prior start still surfaces a done entry`` () =

    // Act
    let result = TaskTrackerModel.Completed(empty, "t9", "completed", "orphan summary")

    // Assert
    %result.Count.Should().Be(1) |> ignore
    %result[0].IsDone.Should().BeTrue() |> ignore
    %result[0].TaskId.Should().Be("t9") |> ignore
    %result[0].Description.Should().Be("orphan summary")

[<Fact>]
let ``Remove drops the task by id and keeps the rest in order`` () =

    // Arrange
    let a = TaskTrackerModel.Started(empty, "t1", "a", "x")
    let b = TaskTrackerModel.Started(a, "t2", "b", "y")

    // Act
    let result = TaskTrackerModel.Remove(b, "t1")

    // Assert
    %result.Count.Should().Be(1) |> ignore
    %result[0].TaskId.Should().Be("t2")

[<Fact>]
let ``RunningCount counts only the not-done`` () =

    // Arrange
    let a = TaskTrackerModel.Started(empty, "t1", "a", "x")
    let b = TaskTrackerModel.Started(a, "t2", "b", "y")
    let c = TaskTrackerModel.Completed(b, "t1", "completed", "s")

    // Act & Assert
    %(TaskTrackerModel.RunningCount(c)).Should().Be(1)
