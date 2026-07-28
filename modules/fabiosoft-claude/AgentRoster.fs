namespace FabioSoft.Claude

open System
open FabioSoft.Json

/// What the supervisor daemon knows about one background agent that the session listing does not: the process
/// behind it and the pipes it is reachable on. Needed to hand an agent over cleanly rather than leaving a second
/// process on the same transcript.
type AgentWorker =
    { SessionId: string
      ProcessId: int
      /// The daemon's short form of the session id, which names its pipes and its pty-pids file.
      Short: string
      PtySocket: string
      RendezvousSocket: string
      /// The prompt the agent was dispatched with, when it was dispatched with one. Often a better label than a
      /// name, because it says what the agent is actually doing.
      Intent: string }

/// Reading the supervisor daemon's roster (`<claude home>/daemon/roster.json`).
///
/// This is undocumented internals of a CLI that updates itself, so every read is gated on the roster's own
/// `proto` field: an unrecognised version yields nothing and the caller falls back to `claude agents --json`,
/// which is documented and stable. Nothing here is required for Clavis to work - it only adds the process
/// identity that a clean hand-over needs.
[<RequireQualifiedAccess>]
module AgentRoster =

    /// The only roster layout this understands. A newer daemon must degrade, not be guessed at.
    [<Literal>]
    let SupportedProto = 1

    /// `<claude home>/daemon/roster.json`. The daemon is per Claude home, so a machine running two homes has two
    /// independent supervisors and two rosters.
    let pathIn (claudeHome: string) =
        IO.Path.Combine(claudeHome, "daemon", "roster.json")

    let private field name json =
        match json with
        | Json.Object properties -> properties |> List.tryFind (fst >> (=) name) |> Option.map snd
        | _ -> None

    let private stringField name json =
        match field name json with
        | Some (Json.String value) when not (String.IsNullOrWhiteSpace value) -> Some value
        | _ -> None

    let private intField name json =
        match field name json with
        | Some (Json.Integer value) -> Some(int value)
        | Some (Json.Float value) -> Some(int value)
        | _ -> None

    let private workerOf (short: string) json =
        match stringField "sessionId" json, intField "pid" json with
        | Some sessionId, Some processId ->
            let intent =
                field "dispatch" json
                |> Option.bind (field "seed")
                |> Option.bind (stringField "intent")
                |> Option.defaultValue ""

            Some
                { SessionId = sessionId
                  ProcessId = processId
                  Short = short
                  PtySocket = stringField "ptySock" json |> Option.defaultValue ""
                  RendezvousSocket = stringField "rendezvousSock" json |> Option.defaultValue ""
                  Intent = intent }
        | _ -> None

    /// Parse a roster document. Yields nothing for an unreadable file, a missing or unrecognised `proto`, or a
    /// missing `workers` map - all of which mean "the daemon is not something we understand", never an error.
    let parse (document: string) =
        if String.IsNullOrWhiteSpace document then
            []
        else
            match Json.parse document with
            | Error _ -> []
            | Ok root ->
                match intField "proto" root with
                | Some proto when proto = SupportedProto ->
                    match field "workers" root with
                    | Some (Json.Object workers) -> workers |> List.choose (fun (short, worker) -> workerOf short worker)
                    | _ -> []
                | _ -> []

    /// The worker behind a session, if the daemon supervises one.
    let forSession (sessionId: string) (workers: AgentWorker list) =
        workers |> List.tryFind (fun worker -> String.Equals(worker.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
