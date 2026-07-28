namespace FabioSoft.Claude

open System
open FabioSoft.Json

/// One live agent the CLI reports, reduced to the fields Clavis can use. The CLI reports more (a pid, a bridge
/// session id, a peer protocol version); those are deliberately dropped here rather than carried up, because
/// they are undocumented internals of a self-updating CLI and nothing above this line should depend on them.
type AgentInstanceInfo =
    { SessionId: string
      Name: string
      WorkingDirectory: string
      Status: string
      StartedAt: DateTimeOffset }

/// Reading `claude agents --json`, and deciding what releasing an instance should do. Pure over the JSON text,
/// so the malformed-row handling is testable without invoking the CLI.
[<RequireQualifiedAccess>]
module AgentInstances =

    [<Literal>]
    let ListCommand = "agents"

    let private field name json =
        match json with
        | Json.Object properties -> properties |> List.tryFind (fst >> (=) name) |> Option.map snd
        | _ -> None

    let private stringField name json =
        match field name json with
        | Some (Json.String value) when not (String.IsNullOrWhiteSpace value) -> Some value
        | _ -> None

    let private timeField name json =
        match field name json with
        | Some (Json.String value) ->
            match DateTimeOffset.TryParse value with
            | true, parsed -> parsed
            | _ -> DateTimeOffset.MinValue
        | _ -> DateTimeOffset.MinValue

    /// Map one reported row. A row without a session id is unusable - it cannot be resumed or addressed - so it
    /// yields None and is dropped rather than surfacing an instance nothing can act on. An absent name falls
    /// back to the working directory's last segment, which is what the user would recognise anyway.
    let ofJson (json: Json) =
        match stringField "sessionId" json with
        | None -> None
        | Some sessionId ->
            let workingDirectory = stringField "cwd" json |> Option.defaultValue ""
            let fallbackName =
                if String.IsNullOrWhiteSpace workingDirectory then sessionId
                else IO.Path.GetFileName(workingDirectory.TrimEnd('\\', '/'))

            Some
                { SessionId = sessionId
                  Name = stringField "name" json |> Option.defaultValue fallbackName
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

    /// Whether an instance may be adopted. Adoption is exclusive: one already taken over is refused rather than
    /// handed to a second owner, because two processes on one session id means two windows onto one transcript
    /// and a corrupted conversation.
    let canAdopt (adoptedSessionIds: Set<string>) (instance: AgentInstanceInfo) =
        not (adoptedSessionIds.Contains instance.SessionId)

    /// Whether a release should leave the agent running. Anything that is not an explicit keep-running is a
    /// stop: the safe default when a caller sends something unrecognised is to end the session, not to leave a
    /// detached process behind that nobody is tracking.
    let keepsRunning (mode: string) =
        String.Equals(mode, "keep-running", StringComparison.OrdinalIgnoreCase)
