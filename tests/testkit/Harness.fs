namespace FabioSoft.Clavis.TestKit

open System
open System.Collections.Generic
open System.Threading.Tasks
open FabioSoft.Nucleus.Bus
open FabioSoft.Nucleus.Contracts

/// A headless, running slice of Clavis for integration tests: a real Bus with a set of real plugins
/// activated on it, then the bootstrap buffer flushed (mirroring kernel boot). Assertions are made on the
/// observable bus traffic via the embedded probe; nothing here touches WPF.
type Harness(bus: Bus, handles: IDisposable list) =

    let probe = BusProbe(bus)

    member _.Bus: IBus = bus :> IBus

    member _.Send(message: 'message) = bus.Send(message)

    member _.Messages<'message>() = probe.Messages<'message>()

    member _.WaitFor<'message>(predicate: 'message -> bool) = probe.WaitFor<'message>(predicate)

    member _.WaitFor<'message>(predicate: 'message -> bool, timeout: TimeSpan) =
        probe.WaitFor<'message>(predicate, timeout)

    interface IDisposable with
        member _.Dispose() =
            // Best-effort teardown: a failing plugin dispose must not mask the test result.
            for handle in handles do
                try
                    handle.Dispose()
                with ex ->
                    System.Diagnostics.Trace.WriteLine($"Harness dispose failed: {ex.Message}")

            (bus :> IDisposable).Dispose()

[<RequireQualifiedAccess>]
module Harness =

    /// Boots a real bus, runs each activation in order, flushes the bootstrap buffer, then returns the
    /// running harness. Each activation is a plugin's ActivateAsync partially applied to its config; running
    /// them all before the single flush is what lets a message sent during one plugin's activation reach a
    /// subscriber installed by another.
    let boot (activations: (IBus -> Task<IDisposable>) list) : Task<Harness> =
        task {
            let bus = new Bus(BusConfig.defaultConfig)
            let handles = List<IDisposable>()

            for activate in activations do
                let! handle = activate bus
                handles.Add handle

            bus.FlushBootstrapBuffer()
            return new Harness(bus, List.ofSeq handles)
        }
