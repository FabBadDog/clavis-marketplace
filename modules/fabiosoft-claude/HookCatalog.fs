namespace FabioSoft.Claude

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Text.RegularExpressions
open FabioSoft.Json

[<RequireQualifiedAccess>]
module HookCatalog =

    [<Literal>]
    let private scriptExtensionPattern = @"[A-Za-z0-9_\-\.]+\.(ps1|cmd|exe|sh|py|bat)"

    let deriveBasename (command: string) =
        if String.IsNullOrWhiteSpace command then
            ""
        else
            let matched = Regex.Match(command, scriptExtensionPattern, RegexOptions.IgnoreCase)
            let raw =
                if matched.Success then
                    matched.Value
                else
                    command.Split([| ' '; '\t' |]) |> Array.tryHead |> Option.defaultValue command

            try
                Path.GetFileNameWithoutExtension raw
            with ex ->
                Trace.TraceWarning($"Extracting filename from '{raw}' failed: {ex.Message}")
                raw

    let private collectNames (matchers: Json[]) =
        matchers
        |> Array.collect (fun matcher ->
            match Json.tryGetValue<Json> "hooks" matcher with
            | Ok(Some(Json.Array hooks)) ->
                hooks
                |> Array.choose (fun hook ->
                    match Json.tryGetValue<string> "command" hook with
                    | Ok(Some command) ->
                        let name = deriveBasename command
                        if name.Length > 0 then Some name else None
                    | _ -> None)
            | _ -> [||])
        |> Array.toList
        |> List.distinct

    let parse (json: string) =
        match Json.parse json with
        | Error error ->
            Trace.TraceWarning($"Parsing hook catalog failed: {JsonError.getMessage error}")
            Map.empty
        | Ok root ->
            match Json.tryGetValue<Json> "hooks" root with
            | Ok(Some(Json.Object hookPairs)) ->
                hookPairs
                |> List.choose (fun (hookEvent, value) ->
                    match value with
                    | Json.Array matchers ->
                        match collectNames matchers with
                        | [] -> None
                        | names -> Some(hookEvent, names)
                    | _ -> None)
                |> Map.ofList
            | _ -> Map.empty

    let resolveDisplayName (catalog: Map<string, string list>) (hookEvent: string) (index: int) =
        match Map.tryFind hookEvent catalog with
        | None -> hookEvent
        | Some scriptNames -> if index < List.length scriptNames then scriptNames[index] else hookEvent

    [<ExcludeFromCodeCoverage>]
    let load () =
        let settingsPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                "settings.json")

        try
            if File.Exists settingsPath then parse (File.ReadAllText settingsPath) else Map.empty
        with ex ->
            Trace.TraceWarning($"Loading hook catalog from '{settingsPath}' failed: {ex.Message}")
            Map.empty

/// Loads Claude's hook catalogue once and resolves a hook event + firing index to a friendly script
/// display name. The bridge holds one of these and feeds the resolved name into AgentHookStart.
[<ExcludeFromCodeCoverage>]
type ClaudeHookCatalog() =
    let catalog = HookCatalog.load ()

    member _.ResolveDisplayName(hookEvent: string, index: int) =
        HookCatalog.resolveDisplayName catalog hookEvent index
