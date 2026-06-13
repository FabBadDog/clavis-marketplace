namespace FabioSoft.Claude

open System
open System.Diagnostics
open System.Diagnostics.CodeAnalysis
open System.IO
open System.Text.RegularExpressions
open FabioSoft.Json

type RuleMatch = { Pattern: string; Scope: string }

type PermissionRule = { ToolName: string; Arguments: string option }

/// The neutral, already-classified outcome of a Claude permission decision: either a matched ask-rule
/// (Pattern/Scope set, ReasonText empty) or a human-readable explanation (ReasonText set, the others
/// empty). Empty string always means "absent" - never null - so C# consumers need no null checks.
type ResolvedPermissionReason =
    { MatchedRulePattern: string
      MatchedRuleScope: string
      ReasonText: string }

[<RequireQualifiedAccess>]
module PermissionRules =

    [<Literal>]
    let private enterpriseSettingsPath = @"C:\ProgramData\ClaudeCode\managed-settings.json"

    [<Literal>]
    let private noMatchingRule = "No matching permission rule"

    let private toRegex (pattern: string) =
        let escaped = Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".")
        Regex("^" + escaped + "$", RegexOptions.IgnoreCase)

    let private isMatch (toolName: string) (input: string) (rule: PermissionRule) =
        if rule.ToolName <> toolName then
            false
        else
            match rule.Arguments with
            | None -> true
            | Some arguments ->
                try
                    (toRegex arguments).IsMatch(input)
                with ex ->
                    Trace.TraceWarning($"Regex match for pattern '{arguments}' failed: {ex.Message}")
                    false

    let private toMatch (rule: PermissionRule) scope =
        let pattern =
            match rule.Arguments with
            | None -> rule.ToolName
            | Some arguments -> $"{rule.ToolName}({arguments})"

        { Pattern = pattern; Scope = scope }

    let parseRule (rule: string) =
        let openIndex = rule.IndexOf('(')
        if openIndex >= 0 && rule.EndsWith(')') then
            { ToolName = rule[.. openIndex - 1]
              Arguments = Some rule[openIndex + 1 .. rule.Length - 2] }
        else
            { ToolName = rule; Arguments = None }

    let parse (json: string) =
        match Json.parse json with
        | Error error ->
            Trace.TraceWarning($"Parsing permission rules failed: {JsonError.getMessage error}")
            []
        | Ok root ->
            match Json.tryGetValue<Json> "permissions" root with
            | Ok(Some permissions) ->
                match Json.tryGetValue<string[]> "ask" permissions with
                | Ok(Some rules) -> rules |> Array.map parseRule |> Array.toList
                | _ -> []
            | _ -> []

    let findMatch
        (readFile: string -> string option)
        (scopePaths: (string * string) list)
        (toolName: string)
        (input: string)
        =
        let allRules =
            scopePaths
            |> List.collect (fun (scope, path) ->
                match readFile path with
                | Some content -> parse content |> List.map (fun rule -> rule, scope)
                | None -> [])

        match allRules |> List.tryFind (fun (rule, _) -> isMatch toolName input rule) with
        | Some(rule, scope) -> Some(toMatch rule scope)
        | None ->
            allRules
            |> List.tryFind (fun (rule, _) -> rule.ToolName = toolName)
            |> Option.map (fun (rule, scope) -> toMatch rule scope)

    let private explanation text =
        { MatchedRulePattern = ""; MatchedRuleScope = ""; ReasonText = text }

    let private matched (ruleMatch: RuleMatch) =
        { MatchedRulePattern = ruleMatch.Pattern
          MatchedRuleScope = ruleMatch.Scope
          ReasonText = "" }

    /// Folds Claude's decision-reason classification into the neutral form. The provider-specific
    /// reason-type literals are interpreted here and never cross the facade.
    let resolveReason
        (readFile: string -> string option)
        (scopePaths: (string * string) list)
        (decisionReasonType: string)
        (decisionReason: string)
        (toolName: string)
        (input: string)
        =
        match decisionReasonType with
        | "rule" ->
            match findMatch readFile scopePaths toolName input with
            | Some ruleMatch -> matched ruleMatch
            | None -> explanation "Matching ask rule"
        | "safetyCheck" -> explanation (if isNull decisionReason then noMatchingRule else decisionReason)
        | "subcommandResults" -> explanation "Compound command requires approval"
        | _ ->
            if decisionReason = "This command requires approval" then
                explanation noMatchingRule
            elif isNull decisionReason then
                explanation noMatchingRule
            else
                explanation decisionReason

    [<ExcludeFromCodeCoverage>]
    let scopePaths (workingDirectory: string) =
        let userSettingsPath =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                "settings.json")

        [ "enterprise", enterpriseSettingsPath
          "local", Path.Combine(workingDirectory, ".claude", "settings.local.json")
          "project", Path.Combine(workingDirectory, ".claude", "settings.json")
          "user", userSettingsPath ]

    [<ExcludeFromCodeCoverage>]
    let readSettingsFile (path: string) =
        try
            if File.Exists path then Some(File.ReadAllText path) else None
        with ex ->
            Trace.TraceWarning($"Reading settings file '{path}' failed: {ex.Message}")
            None

/// Reads Claude's settings files (built once per working directory) and classifies a permission
/// decision into the neutral ResolvedPermissionReason. The bridge holds one of these per session.
[<ExcludeFromCodeCoverage>]
type ClaudePermissionResolver(workingDirectory: string) =
    let paths = PermissionRules.scopePaths workingDirectory

    member _.Resolve(decisionReasonType: string, decisionReason: string, toolName: string, input: string) =
        PermissionRules.resolveReason
            PermissionRules.readSettingsFile
            paths
            decisionReasonType
            decisionReason
            toolName
            input
