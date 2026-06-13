module FabioSoft.Nucleus.PluginManager.Tests.PluginManagerViewModelTests

open Faqt
open Faqt.Operators
open FabioSoft.Nucleus.Plugins.PluginManager
open Xunit

let private noop _ = ()

[<Fact>]
let ``AddOrUpdate adds new plugin`` () =

    // Arrange
    let viewModel = PluginManagerViewModel()

    // Act
    viewModel.AddOrUpdate("TestPlugin", "Active", noop)

    // Assert
    %viewModel.Plugins.Count.Should().Be(1)
    %viewModel.ActiveCount.Should().Be(1)

[<Fact>]
let ``AddOrUpdate updates existing plugin state`` () =

    // Arrange
    let viewModel = PluginManagerViewModel()
    viewModel.AddOrUpdate("TestPlugin", "Active", noop)

    // Act
    viewModel.AddOrUpdate("TestPlugin", "Error", noop)

    // Assert
    %viewModel.Plugins.Count.Should().Be(1)
    %viewModel.Plugins[0].State.Should().Be("Error")
    %viewModel.ActiveCount.Should().Be(0)

[<Fact>]
let ``Remove removes plugin`` () =

    // Arrange
    let viewModel = PluginManagerViewModel()
    viewModel.AddOrUpdate("TestPlugin", "Active", noop)

    // Act
    viewModel.Remove("TestPlugin")

    // Assert
    %viewModel.Plugins.Count.Should().Be(0)
    %viewModel.TotalCount.Should().Be(0)

[<Fact>]
let ``Remove does nothing for unknown plugin`` () =

    // Arrange
    let viewModel = PluginManagerViewModel()
    viewModel.AddOrUpdate("TestPlugin", "Active", noop)

    // Act
    viewModel.Remove("Unknown")

    // Assert
    %viewModel.Plugins.Count.Should().Be(1)

[<Fact>]
let ``multiple plugins tracked independently`` () =

    // Arrange
    let viewModel = PluginManagerViewModel()

    // Act
    viewModel.AddOrUpdate("A", "Active", noop)
    viewModel.AddOrUpdate("B", "Active", noop)
    viewModel.AddOrUpdate("C", "Error", noop)

    // Assert
    %viewModel.TotalCount.Should().Be(3)
    %viewModel.ActiveCount.Should().Be(2)
