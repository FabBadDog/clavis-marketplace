namespace FabioSoft.Claude

open System
open FabioSoft.Json

/// One live agent the CLI reports, reduced to the fields Clavis can use. The CLI reports more (a pid, a bridge
/// session id, a peer protocol version); those are deliberately dropped here rather than carried up, because
/// they are undocumented internals of a self-updating CLI and nothing above this line should depend on them.
///
/// IsOwned says the reported name carried Clavis's ownership marker, i.e. Clavis started this agent. It is a
/// label, not a permission: agents started in the CLI's own agent view can be taken over too.
///
/// IsBackground is the actual safety gate. The listing carries interactive sessions as well - the user's own
/// terminals and editors - and those cannot be handed over: taking an agent over stops it first, and stopping
/// somebody's terminal out from under them is not a hand-over but a hijack. Only background agents are offered.
///
/// AgentId is the short handle the CLI addresses an agent by (`claude stop <id>`), and is *not* derivable from
/// the session id: an agent whose session id starts 2b3bba05 can be addressed as a7683d47. Both are carried
/// because adoption needs each - the short handle to stop the running agent, the session id to resume its
/// conversation.
type AgentInstanceInfo =
    { SessionId: string
      AgentId: string
      Name: string
      IsOwned: bool
      IsBackground: bool
      WorkingDirectory: string
      Status: string
      StartedAt: DateTimeOffset }

/// Reading `claude agents --json`, naming sessions so they can be recognised later, and deciding what releasing
/// an instance should do. Pure over the JSON text and over the argument lists, so the malformed-row handling and
/// the spawn shape are testable without invoking the CLI.
[<RequireQualifiedAccess>]
module AgentInstances =

    /// Marks a session as Clavis-owned in the provider's session name. The name is the only field that survives
    /// into `claude agents --json` and is under our control, so it carries ownership; without a marker Clavis
    /// could not tell its own parked agents from the user's live sessions.
    [<Literal>]
    let OwnershipPrefix = "clavis/"

    /// `claude agents --json` - list active sessions without needing a TTY.
    let listArguments = [ "agents"; "--json" ]

    /// `claude stop <agent-id>` - the CLI's own way to end a background agent, addressed by its short handle
    /// rather than its session id. This is the release half of a hand-over: while an agent is running, the CLI
    /// refuses to resume its session at all ("currently running as a background agent"), so adoption has to ask
    /// the agent to let go before it can pick the conversation up.
    let stopArguments (agentId: string) = [ "stop"; agentId ]

    /// Hand a session back to a durable background agent, which carries on after Clavis exits. `--resume` starts
    /// a *new* process over the persisted transcript rather than joining the old one, which is why the owned
    /// stream must be finished before this runs.
    let handOffArguments (sessionId: string) (name: string) =
        let resume = [ "--bg"; "--resume"; sessionId ]
        if String.IsNullOrWhiteSpace name then resume
        else resume @ [ "-n"; name ]

    /// The provider-visible name for a session Clavis starts. The label is what the user recognises (a workspace
    /// name, or the working directory's last segment); the prefix is what makes it reclaimable.
    let nameFor (label: string) =
        if String.IsNullOrWhiteSpace label then OwnershipPrefix.TrimEnd('/')
        else $"{OwnershipPrefix}{label.Trim()}"

    let private field name json =
        match json with
        | Json.Object properties -> properties |> List.tryFind (fst >> (=) name) |> Option.map snd
        | _ -> None

    let private stringField name json =
        match field name json with
        | Some (Json.String value) when not (String.IsNullOrWhiteSpace value) -> Some value
        | _ -> None

    /// The CLI reports startedAt as Unix epoch milliseconds, not as a timestamp string (verified against the
    /// real output, which is why the string branch is only a fallback for a format change).
    let private timeField name json =
        match field name json with
        | Some (Json.Integer milliseconds) -> DateTimeOffset.FromUnixTimeMilliseconds milliseconds
        | Some (Json.Float milliseconds) -> DateTimeOffset.FromUnixTimeMilliseconds(int64 milliseconds)
        | Some (Json.String value) ->
            match DateTimeOffset.TryParse value with
            | true, parsed -> parsed
            | _ -> DateTimeOffset.MinValue
        | _ -> DateTimeOffset.MinValue

    /// Map one reported row. A row without a session id is unusable - it cannot be resumed or addressed - so it
    /// yields None and is dropped rather than surfacing an instance nothing can act on. An absent name falls
    /// back to the working directory's last segment, which is what the user would recognise anyway; an absent
    /// name is never owned, because ownership is something Clavis writes.
    let ofJson (json: Json) =
        match stringField "sessionId" json with
        | None -> None
        | Some sessionId ->
            let workingDirectory = stringField "cwd" json |> Option.defaultValue ""
            let fallbackName =
                if String.IsNullOrWhiteSpace workingDirectory then sessionId
                else IO.Path.GetFileName(workingDirectory.TrimEnd('\\', '/'))

            let reported = stringField "name" json
            let isOwned =
                reported
                |> Option.exists _.StartsWith(OwnershipPrefix, StringComparison.OrdinalIgnoreCase)

            let name =
                match reported with
                | Some value when isOwned ->
                    match value.Substring(OwnershipPrefix.Length).Trim() with
                    | "" -> fallbackName
                    | label -> label
                | Some value -> value
                | None -> fallbackName

            Some
                { SessionId = sessionId
                  AgentId = stringField "id" json |> Option.defaultValue ""
                  Name = name
                  IsOwned = isOwned
                  IsBackground =
                    stringField "kind" json
                    |> Option.exists (fun kind -> String.Equals(kind, "background", StringComparison.OrdinalIgnoreCase))
                  WorkingDirectory = workingDirectory
                  Status = stringField "status" json |> Option.defaultValue "unknown"
                  StartedAt = timeField "startedAt" json }

    /// Parse the whole `claude agents --json` payload. Unparseable output, or output that is not an array,
    /// yields an empty list: a provider that changed its format must degrade to "no instances known" rather than
    /// taking the caller down.
    let parse (output: string) =
        if String.IsNullOrWhiteSpace output then
            []
        else
            match Json.parse output with
            | Ok (Json.Array rows) -> rows |> List.ofArray |> List.choose ofJson
            | _ -> []

    /// The instances Clavis may offer to take over: every background agent, including ones started in the CLI's
    /// own agent view. Interactive sessions are excluded - taking an agent over stops it first, which is a
    /// hand-over for a background agent but would pull a terminal out from under whoever is typing in it.
    let reclaimable (instances: AgentInstanceInfo list) =
        instances |> List.filter _.IsBackground

    /// Whether an instance may be adopted. Adoption is exclusive: one already taken over is refused rather than
    /// handed to a second owner, because two Clavis streams on one session id means two windows onto one
    /// transcript.
    let canAdopt (adoptedSessionIds: Set<string>) (instance: AgentInstanceInfo) =
        instance.IsBackground && not (adoptedSessionIds.Contains instance.SessionId)

    /// The status the CLI reports for an agent that is mid-turn. Verified against the real listing, which reports
    /// `busy` while working, `idle` once done, and omits the field entirely for an agent that is blocked.
    [<Literal>]
    let BusyStatus = "busy"

    /// Whether the instance is working on something right now. Taking an agent over stops it first, so an
    /// unfinished turn is lost; a caller waits for this to go false rather than interrupting.
    ///
    /// Only a positively reported busy counts. An absent or unrecognised status reads as *not* working, because
    /// the alternative - waiting on a status the provider never reports - would wait forever and never hand over
    /// at all. Losing a turn is recoverable; a take-over that can never happen is not.
    let isWorking (instance: AgentInstanceInfo) =
        String.Equals(instance.Status, BusyStatus, StringComparison.OrdinalIgnoreCase)

    /// Whether a release should leave the agent running. Anything that is not an explicit keep-running is a
    /// stop: the safe default when a caller sends something unrecognised is to end the session, not to leave a
    /// detached process behind that nobody is tracking.
    let keepsRunning (mode: string) =
        String.Equals(mode, "keep-running", StringComparison.OrdinalIgnoreCase)

    let private sameDirectory left right =
        String.Equals(
            (left: string).TrimEnd('\\', '/'),
            (right: string).TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase)

    /// Find the agent Clavis parked for a given label and directory, so a workspace picks its own conversation
    /// back up on the next launch.
    ///
    /// Matching is by name and directory rather than by a remembered id, because handing a session back gives the
    /// parked agent a *new* session id that Clavis never sees - it spawns the agent and exits. The name is the one
    /// field Clavis writes and the provider preserves, which makes it the only durable link back.
    ///
    /// An ambiguous match yields None. Two agents answering to one label means Clavis cannot tell which
    /// conversation belongs to the workspace, and silently picking either would attach it to somebody else's work;
    /// the caller surfaces both as reclaimable instead and lets the user choose.
    let parkedFor (label: string) (workingDirectory: string) (instances: AgentInstanceInfo list) =
        if String.IsNullOrWhiteSpace label then
            None
        else
            let matches =
                instances
                |> List.filter (fun instance ->
                    instance.IsOwned
                    && instance.IsBackground
                    && String.Equals(instance.Name, label.Trim(), StringComparison.OrdinalIgnoreCase)
                    && sameDirectory instance.WorkingDirectory workingDirectory)

            match matches with
            | [ single ] -> Some single
            | _ -> None
