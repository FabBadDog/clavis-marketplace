namespace FabioSoft.Claude

open System
open System.Diagnostics.CodeAnalysis
open System.Globalization
open System.IO
open System.Net.Http
open System.Threading.Tasks
open FabioSoft.Json
open FsToolkit.ErrorHandling

/// One usage window from the Anthropic OAuth usage API. Utilization is a 0..100 percentage; WindowStart
/// is derived from ResetsAt and the window's known length so a consumer can place "now" within the
/// window without knowing the plan.
type UsageWindow =
    { Name: string
      Utilization: float
      WindowStart: DateTimeOffset
      ResetsAt: DateTimeOffset }

/// Reads the account's rolling usage from `GET /api/oauth/usage` (the same source the CLI status line
/// uses). The parse is pure and tested; the fetch reads the OAuth token from the Claude config dir and
/// is a best-effort poll, so it lives behind the same provider seam as the rest of this library.
[<RequireQualifiedAccess>]
module UsageApi =

    [<Literal>]
    let private UsageEndpoint = "https://api.anthropic.com/api/oauth/usage"

    [<Literal>]
    let private OAuthBetaHeader = "oauth-2025-04-20"

    // Provider-specific knowledge: each API section's friendly name and window length. The neutral
    // contract downstream carries only the resolved values, never these keys.
    let private sections =
        [ "five_hour", "5-Hour", TimeSpan.FromHours 5.0
          "seven_day", "Weekly", TimeSpan.FromDays 7.0 ]

    let private parseSection root (key: string, name: string, span: TimeSpan) =

        result {
            let! section = Json.tryGetValue<Json> key root |> Result.mapError JsonError.getMessage
            match section with
            | None -> return None
            | Some section ->
                let! resetsAtText = Json.tryGetValue<string> "resets_at" section |> Result.mapError JsonError.getMessage
                let! utilization = Json.getValueOrDefault<float> "utilization" 0.0 section |> Result.mapError JsonError.getMessage
                match resetsAtText with
                | None -> return None
                | Some text ->
                    match DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind) with
                    | true, resetsAt ->
                        return Some
                            { Name = name
                              Utilization = utilization
                              WindowStart = resetsAt - span
                              ResetsAt = resetsAt }
                    | false, _ -> return None
        }

    /// Parse the usage API body into the windows it reports. Sections that are absent or unparseable are
    /// dropped; a malformed document surfaces as an Error.
    let parseUsage (json: string) : Result<UsageWindow list, string> =

        result {
            let! root = Json.parse json |> Result.mapError JsonError.getMessage
            let! windows = sections |> List.traverseResultM (parseSection root)
            return windows |> List.choose id
        }

    [<ExcludeFromCodeCoverage>]
    let private credentialsPath () =
        let configDir =
            match Environment.GetEnvironmentVariable "CLAUDE_CONFIG_DIR" with
            | null | "" -> Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.UserProfile, ".claude")
            | dir -> dir
        Path.Combine(configDir, ".credentials.json")

    [<ExcludeFromCodeCoverage>]
    let private readAccessToken () =
        match Json.parse (File.ReadAllText(credentialsPath ())) with
        | Error _ -> None
        | Ok root ->
            match Json.tryGetValue<Json> "claudeAiOauth" root with
            | Ok(Some oauth) ->
                match Json.tryGetValue<string> "accessToken" oauth with
                | Ok token -> token
                | Error _ -> None
            | _ -> None

    /// Fetch the current usage windows. Best-effort: any failure (no token, network, non-success status,
    /// malformed body) yields an empty array rather than throwing - the caller polls on an interval and
    /// this library has no logging channel, so a transient miss simply resolves on the next tick.
    [<ExcludeFromCodeCoverage>]
    let fetchUsage () : Task<UsageWindow[]> =

        task {
            try
                match readAccessToken () with
                | None -> return [||]
                | Some token ->
                    use client = new HttpClient(Timeout = TimeSpan.FromSeconds 5.0)
                    use request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint)
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}") |> ignore
                    request.Headers.TryAddWithoutValidation("anthropic-beta", OAuthBetaHeader) |> ignore
                    use! response = client.SendAsync request
                    let! body = response.Content.ReadAsStringAsync()
                    if response.IsSuccessStatusCode then
                        match parseUsage body with
                        | Ok windows -> return List.toArray windows
                        | Error _ -> return [||]
                    else
                        return [||]
            with _ ->
                return [||]
        }
