module FabioSoft.Nucleus.Conversation.Tests.CollectionSyncTests

open System
open System.Collections.ObjectModel
open System.ComponentModel
open FabioSoft.Nucleus.Plugins.Conversation
open FabioSoft.Nucleus.Plugins.Conversation.ViewModels
open Faqt
open Faqt.Operators
open Xunit

[<Fact>]
let ``two error items sharing a message text get distinct keys`` () =

    // Arrange: two distinct errors that happen to carry the same message
    let errorOne = ErrorItem("timeout")
    let errorTwo = ErrorItem("timeout")

    // Act
    let keyOne = CollectionSync.GetItemKey(errorOne)
    let keyTwo = CollectionSync.GetItemKey(errorTwo)

    // Assert
    %keyOne.Should().NotBe(keyTwo)

[<Fact>]
let ``reconcile keeps both error items that share a message`` () =

    // Arrange
    let targets = ObservableCollection<INotifyPropertyChanged>()
    let sources: TurnItem[] = [| ErrorItem("timeout") :> TurnItem; ErrorItem("timeout") :> TurnItem |]
    let publishPermission = Action<string, string>(fun _ _ -> ())

    // Act
    CollectionSync.Reconcile<TurnItem, INotifyPropertyChanged>(
        targets,
        sources,
        Func<TurnItem, string>(fun item -> CollectionSync.GetItemKey(item)),
        Func<INotifyPropertyChanged, string>(fun viewModel -> CollectionSync.GetItemViewModelKey(viewModel)),
        Func<TurnItem, INotifyPropertyChanged>(fun item -> CollectionSync.CreateItemViewModel(item, publishPermission)),
        Action<INotifyPropertyChanged, TurnItem>(fun viewModel item -> CollectionSync.UpdateItemViewModel(viewModel, item)))

    // Assert
    %targets.Count.Should().Be(2)
