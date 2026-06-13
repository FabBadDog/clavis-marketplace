#!/usr/bin/env pwsh
# Regenerates docs/MESSAGE-MAP.md: a cross-reference of every bus message to the components that publish
# and subscribe to it. In a message-bus app the real dependency graph is not the call graph but the
# publish/subscribe wiring, so this is the navigation aid that answers "who reacts to X?" without grepping.
#
# Scope. This lives in the clavis-marketplace repo and scans the marketplace sources (plugins/ + modules/),
# where every plugin and every contract assembly lives. The framework message *publishers* (the kernel
# and the host) live in the separate clavis core repo, so pass -CoreSrc <path-to-clavis>/src to attribute
# them too; without it, framework messages (BootstrapComplete, Plugin*, ...) show their subscribers but no
# publisher.
#
# It is a static scanner (no build, no reflection): it reads the F#/C# sources and matches the literal bus
# call shapes. Limitations, called out in the generated header too:
#   - Resolves only messages named literally at the call site: Send(new X(..)) / Send(X(..)) / Subscribe<X>
#     / Request<X, Y>. A Send(someVariable) forward cannot be resolved and is not counted.
#   - LogEntry publishers are inferred from bus.LogInfo/Warn/Error/Debug/Trace, not a literal Send.

[CmdletBinding()]
param(
    # The clavis-marketplace repo root (parent of this tools/ folder).
    [string] $RepoRoot = (Split-Path $PSScriptRoot -Parent),
    # Optional path to the clavis core 'src' directory, to attribute kernel/shell framework-message publishers.
    [string] $CoreSrc = $null
)

$ErrorActionPreference = 'Stop'

$outputPath = Join-Path $RepoRoot 'docs/MESSAGE-MAP.md'

# Component and contract-assembly names come from each item's PLUGIN.md (folders are kebab-case;
# components are PascalCase pluginIds, contracts are dotted assembly names).
function Read-Frontmatter([string] $pluginMd, [string] $key) {
    foreach ($line in Get-Content -LiteralPath $pluginMd) {
        if ($line -match "^$([regex]::Escape($key))\s*:\s*(.+?)\s*$") { return $matches[1].Trim() }
        if ($line -eq '---' -and $line.ReadCount -gt 1) { break }
    }
    return $null
}

$componentByFolder = @{}   # marketplace folder -> display name (pluginId / assemblyName)
$assemblyByFolder  = @{}   # marketplace module folder -> contract assembly name

foreach ($group in @('plugins', 'modules')) {
    $groupDir = Join-Path $RepoRoot $group
    if (-not (Test-Path $groupDir)) { continue }
    foreach ($dir in Get-ChildItem -Path $groupDir -Directory) {
        $md = Join-Path $dir.FullName 'PLUGIN.md'
        if (-not (Test-Path $md)) { continue }
        $pluginId = Read-Frontmatter $md 'pluginId'
        $assembly = Read-Frontmatter $md 'assemblyName'
        $name     = Read-Frontmatter $md 'name'
        $display  = if ($pluginId) { $pluginId } elseif ($assembly) { $assembly } else { $name }
        if ($display) { $componentByFolder[$dir.Name] = $display }
        if ($assembly -and $assembly -like 'FabioSoft.Contracts.*') {
            $assemblyByFolder[$dir.Name] = $assembly
        }
    }
}

# Path under the marketplace repo, relative to its root (so the outer ~/.clavis/plugins/ does not get
# mistaken for the repo's own plugins/). Returns $null for files outside the repo (e.g. -CoreSrc).
function Get-MarketplaceRelativePath([string] $path) {
    $p = $path -replace '/', '\'
    $root = ($RepoRoot -replace '/', '\').TrimEnd('\')
    if ($p.StartsWith("$root\", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $p.Substring($root.Length)
    }
    return $null
}

# Map a source file path to the bus participant that owns it.
function Get-Component([string] $path) {
    $rel = Get-MarketplaceRelativePath $path
    if ($rel -and $rel -match '^\\(?:plugins|modules)\\([^\\]+)\\') {
        $folder = $matches[1]
        if ($componentByFolder.ContainsKey($folder)) { return $componentByFolder[$folder] }
        return $folder
    }
    $p = $path -replace '/', '\'
    switch -regex ($p) {
        '\\nucleus\\FabioSoft\.Nucleus\.([^\\]+)\\'  { return "Nucleus.$($matches[1])" }
        '\\FabioSoft\.Clavis\.Shell\\'               { return 'Shell' }
        '\\libraries\\([^\\]+)\\'                    { return $matches[1] }
        default                                      { return 'other' }
    }
}

# The defining assembly of a message type, by the contract item/directory it is declared in.
function Get-DefiningAssembly([string] $path) {
    $rel = Get-MarketplaceRelativePath $path
    if ($rel -and $rel -match '^\\modules\\([^\\]+)\\') {
        $folder = $matches[1]
        if ($assemblyByFolder.ContainsKey($folder)) { return $assemblyByFolder[$folder] }
    }
    $p = $path -replace '/', '\'
    switch -regex ($p) {
        '\\nucleus\\(FabioSoft\.Nucleus\.Contracts)\\'          { return $matches[1] }
        '\\contracts\\(FabioSoft\.Clavis\.Contracts\.[^\\]+)\\' { return $matches[1] }
        default                                                 { return $null }
    }
}

$published  = @{}  # message -> [ordered set of components]
$subscribed = @{}  # message -> [ordered set of components]
$definedIn  = @{}  # message -> assembly
$requests   = [System.Collections.Generic.List[object]]::new()

function Add-Entry([hashtable] $table, [string] $key, [string] $value) {
    if (-not $table.ContainsKey($key)) {
        $table[$key] = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
    }
    [void] $table[$key].Add($value)
}

# A captured generic argument that is an F# type parameter ('message) or a lowercase C# parameter is
# infrastructure, not a concrete message.
function Test-RealMessage([string] $name) {
    return $name -and $name -notmatch "^'" -and $name -cmatch '^[A-Z]'
}

$scanRoots = @((Join-Path $RepoRoot 'plugins'), (Join-Path $RepoRoot 'modules'))
if ($CoreSrc -and (Test-Path $CoreSrc)) { $scanRoots += $CoreSrc }

$files = $scanRoots |
    Where-Object { Test-Path $_ } |
    ForEach-Object { Get-ChildItem -Path $_ -Recurse -File } |
    Where-Object { $_.Extension -in '.fs', '.cs' -and $_.FullName -notmatch '\\(bin|obj|tests)\\' }

foreach ($file in $files) {
    $component = Get-Component $file.FullName
    $assembly  = Get-DefiningAssembly $file.FullName
    $lines = Get-Content -LiteralPath $file.FullName

    foreach ($line in $lines) {
        # Pure comment lines (// or F# ///) are documentation, not wiring.
        if ($line -match '^\s*//') {
            continue
        }

        # Message type declarations inside a contract assembly: "type Foo(" possibly behind attributes.
        if ($assembly -and $line -cmatch '^\s*type\s+([A-Z]\w*)') {
            if (-not $definedIn.ContainsKey($matches[1])) {
                $definedIn[$matches[1]] = $assembly
            }
        }

        # Publishers. C#: Send(new X(..)). F#: Send(X(..)) / Send(X "..").
        if ($line -cmatch '\.Send\(\s*new\s+([A-Z]\w*)') {
            Add-Entry $published $matches[1] $component
        }
        elseif ($line -cmatch '\.Send\(\s*([A-Z]\w*)') {
            Add-Entry $published $matches[1] $component
        }

        # LogEntry is published through the bus.Log* extension methods, never a literal Send(LogEntry ..).
        if ($line -cmatch '\.Log(Info|Warn|Error|Debug|Trace)\(') {
            Add-Entry $published 'LogEntry' $component
        }

        # Subscribers: Subscribe<X>. Skip the framework's generic Subscribe<'message> helpers.
        if ($line -cmatch '\.Subscribe<\s*([^,>]+?)\s*>') {
            $name = $matches[1].Trim()
            if (Test-RealMessage $name) {
                Add-Entry $subscribed $name $component
            }
        }

        # Request/response: the caller publishes the request and consumes the response.
        if ($line -cmatch '\bRequest<\s*([A-Z]\w*)\s*,\s*([\w\.]+?)\s*>') {
            $requestType  = $matches[1]
            $responseType = ($matches[2] -split '\.')[-1]
            Add-Entry $published $requestType $component
            $requests.Add([pscustomobject]@{ Caller = $component; Request = $requestType; Response = $responseType })
        }
    }
}

# A captured identifier is a real cross-plugin message only if it is declared in a contract assembly or is
# the target of an explicit Subscribe<X>. This drops static-scan noise using the architecture's own
# invariant that every cross-plugin message lives in a contract assembly.
function Test-IsMessage([string] $name) {
    return $definedIn.ContainsKey($name) -or $subscribed.ContainsKey($name)
}

$allMessages = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($key in $published.Keys)  { if (Test-IsMessage $key) { [void] $allMessages.Add($key) } }
foreach ($key in $subscribed.Keys) { if (Test-IsMessage $key) { [void] $allMessages.Add($key) } }

function Format-Set($set) {
    if (-not $set -or $set.Count -eq 0) { return '_none found_' }
    return ($set -join ', ')
}

$builder = [System.Text.StringBuilder]::new()
[void] $builder.AppendLine('# Message map')
[void] $builder.AppendLine()
[void] $builder.AppendLine('_Generated by `tools/Generate-MessageMap.ps1` (in the clavis-marketplace repo). Do not edit by hand - rerun the script._')
[void] $builder.AppendLine()
[void] $builder.AppendLine('Cross-references every cross-plugin bus message to the components that publish and subscribe to')
[void] $builder.AppendLine('it. In the Nucleus bus architecture this publish/subscribe wiring - not the call graph - is the real')
[void] $builder.AppendLine('dependency structure, so this answers "who reacts to X?" and "what does plugin Y emit?" directly.')
[void] $builder.AppendLine()
[void] $builder.AppendLine('**Resolved statically from source.** Only messages named literally at the call site are counted:')
[void] $builder.AppendLine('`Send(new X(..))`, `Send(X(..))`, `Subscribe<X>`, `Request<X, Y>`. A `Send(variable)` forward is not')
[void] $builder.AppendLine('resolved, and `LogEntry` publishers are inferred from `bus.Log*`. Treat this as a strong index, not a')
[void] $builder.AppendLine('proof.')
[void] $builder.AppendLine()
[void] $builder.AppendLine('Scanned: the marketplace `plugins/` + `modules/`. Framework-message publishers (the kernel and host in')
[void] $builder.AppendLine('the clavis core repo) are attributed only when the script is run with `-CoreSrc <clavis>/src`.')
[void] $builder.AppendLine()

# Group messages by their defining contract assembly (undetermined ones last).
$byAssembly = @{}
foreach ($message in $allMessages) {
    $assembly = if ($definedIn.ContainsKey($message)) { $definedIn[$message] } else { '(assembly not resolved)' }
    if (-not $byAssembly.ContainsKey($assembly)) {
        $byAssembly[$assembly] = [System.Collections.Generic.List[string]]::new()
    }
    $byAssembly[$assembly].Add($message)
}

[void] $builder.AppendLine('## Messages by contract assembly')
[void] $builder.AppendLine()

$assemblyOrder = $byAssembly.Keys | Sort-Object { if ($_ -like '(*') { "zzz$_" } else { $_ } }
foreach ($assembly in $assemblyOrder) {
    [void] $builder.AppendLine("### $assembly")
    [void] $builder.AppendLine()
    foreach ($message in ($byAssembly[$assembly] | Sort-Object)) {
        $pub = Format-Set $published[$message]
        $sub = Format-Set $subscribed[$message]
        [void] $builder.AppendLine("- **$message** - pub: $pub - sub: $sub")
    }
    [void] $builder.AppendLine()
}

$realRequests = $requests | Where-Object { Test-IsMessage $_.Request }
if ($realRequests) {
    [void] $builder.AppendLine('## Request / response pairs')
    [void] $builder.AppendLine()
    foreach ($request in ($realRequests | Sort-Object Request -Unique)) {
        [void] $builder.AppendLine("- **$($request.Request)** -> **$($request.Response)** (caller: $($request.Caller))")
    }
    [void] $builder.AppendLine()
}

# Per-component summary: what each participant emits and reacts to.
$components = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($set in $published.Values)  { foreach ($component in $set) { [void] $components.Add($component) } }
foreach ($set in $subscribed.Values) { foreach ($component in $set) { [void] $components.Add($component) } }

[void] $builder.AppendLine('## Per-component summary')
[void] $builder.AppendLine()
foreach ($component in $components) {
    $emits = $allMessages | Where-Object { $published.ContainsKey($_)  -and $published[$_].Contains($component) }  | Sort-Object
    $reads = $allMessages | Where-Object { $subscribed.ContainsKey($_) -and $subscribed[$_].Contains($component) } | Sort-Object
    [void] $builder.AppendLine("### $component")
    [void] $builder.AppendLine("- publishes: $(if ($emits) { $emits -join ', ' } else { '_none_' })")
    [void] $builder.AppendLine("- subscribes: $(if ($reads) { $reads -join ', ' } else { '_none_' })")
    [void] $builder.AppendLine()
}

$outputDirectory = Split-Path $outputPath -Parent
if (-not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

Set-Content -LiteralPath $outputPath -Value $builder.ToString() -Encoding utf8NoBOM

Write-Output "Wrote $outputPath ($($allMessages.Count) messages, $($components.Count) components)"
