namespace FabioSoft.Clavis.TestKit

open System
open System.Collections.Generic
open System.Threading.Tasks
open FabioSoft.Nucleus.Bus
open FabioSoft.Nucleus.Contracts

/// Observes a live Bus for assertions. It reads the bus activity stream - a replay subject, so a message
/// sent before a query is not missed - and exposes typed snapshots plus an awaitable predicate.
///
/// "Published" means the message was put on the bus, whether or not a subscriber consumed it: a message
/// with no subscriber in a minimal test harness is still a real publish (it appears as a NoSubscriber
/// dead letter). `Messages`/`WaitFor` therefore match by payload type regardless of dead-letter reason;
/// `DeadLetters` is the separate view for asserting genuine delivery failures.
type BusProbe(bus: Bus) =

    let defaultTimeout = TimeSpan.FromSeconds 2.0

    /// A point-in-time snapshot of every published message of the given type, in send order.
    member _.Messages<'message>() : 'message list =
        let results = List<'message>()

        use _ =
            bus.Activity
            |> Observable.subscribe (fun activity ->
                match activity.Payload with
                | :? 'message as message -> results.Add message
                | _ -> ())

        results |> List.ofSeq

    /// A snapshot of every dead-lettered message of the given type (carrying the dead-letter reason).
    member _.DeadLetters<'message>() : ('message * DeadLetterReason) list =
        let results = List<'message * DeadLetterReason>()

        use _ =
            bus.DeadLetters
            |> Observable.subscribe (fun deadLetter ->
                match deadLetter.Payload with
                | :? 'message as message -> results.Add(message, deadLetter.Reason)
                | _ -> ())

        results |> List.ofSeq

    member this.WaitFor<'message>(predicate: 'message -> bool) : Task<'message> =
        this.WaitFor<'message>(predicate, defaultTimeout)

    /// Completes with the first published message of the given type that satisfies the predicate, or faults
    /// with a TimeoutException. Subscribes to the replay stream, so messages already sent are considered.
    member _.WaitFor<'message>(predicate: 'message -> bool, timeout: TimeSpan) : Task<'message> =
        let completion = TaskCompletionSource<'message>()

        let subscription =
            bus.Activity
            |> Observable.subscribe (fun activity ->
                match activity.Payload with
                | :? 'message as message when predicate message -> completion.TrySetResult message |> ignore
                | _ -> ())

        let awaited = completion.Task.WaitAsync(timeout)
        awaited.ContinueWith(fun (_: Task) -> subscription.Dispose()) |> ignore
        awaited
