#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Validates (and optionally normalizes) the dependency versions declared in every marketplace manifest.

.DESCRIPTION
    A marketplace item declares the items it needs in its manifest's `dependencies:` list. The version
    there only ever needs to express a MAJOR: the runtime binds a dependency by major (it loads whatever
    assembly is on disk and matches the major), so a `^1.0.0`-style range carries a minor/patch precision
    nothing honours - and, worse, can silently drift from the major the dependency actually ships (a bump
    of the producer's major never updated its consumers' ranges).

    This tool is the missing feedback loop. It reads every manifest, builds a map of "item -> the major it
    currently ships" (from each item's own `version:`), and for every dependency that names an in-marketplace
    item it checks the declared major against the producer's actual major. Dependencies on things the
    marketplace does not produce (NuGet packages, the core-shipped libraries) are reported as external and
    left untouched - their major is not ours to track.

    Without -Fix it only reports, exiting non-zero when any in-marketplace dependency is stale or not in the
    bare-major form (so it can gate CI). With -Fix it rewrites each in-marketplace dependency's version to a
    bare major matching the producer - the honest, drift-proof form.

.PARAMETER Fix
    Rewrite stale / non-bare in-marketplace dependency versions in place to the producer's bare major.

.EXAMPLE
    ./tools/Validate-Dependencies.ps1
    ./tools/Validate-Dependencies.ps1 -Fix
#>
[CmdletBinding()]
param(
    [switch]$Fix
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$manifests = Get-ChildItem -Path (Join-Path $root 'plugins'), (Join-Path $root 'modules') `
    -Recurse -Include 'PLUGIN.md', 'MODULE.md' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

# A manifest's frontmatter `name:`, `version:` (-> major), and inline-flow dependency entries.
$depPattern = '^\s*-\s*\{\s*name:\s*(?<name>[^,}\s]+)\s*,\s*version:\s*(?<ver>[^}]+?)\s*\}\s*$'

function Get-Major([string]$version) {
    $v = $version.Trim().TrimStart('^', '=')
    $head = ($v -split '\.')[0]
    [int]$out = 0
    if ([int]::TryParse($head, [ref]$out)) { return $out }
    return $null
}

# Pass 1: every item's own current major (the producer side).
$producerMajor = @{}
foreach ($file in $manifests) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    $name = $null; $version = $null
    foreach ($line in $lines) {
        if (-not $name -and $line -match '^name:\s*(?<n>\S+)') { $name = $Matches.n }
        if (-not $version -and $line -match '^version:\s*(?<v>\S+)') { $version = $Matches.v }
        if ($line -match '^---' -and $name) { break }
    }
    if ($name -and $version) { $producerMajor[$name] = (Get-Major $version) }
}

# Pass 2: validate (and optionally fix) each dependency edge.
$stale = @(); $reformatted = @(); $external = @(); $okCount = 0
foreach ($file in $manifests) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName)
    $changed = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch $depPattern) { continue }
        $depName = $Matches.name
        $depVer = $Matches.ver
        $item = $file.Directory.Name

        if (-not $producerMajor.ContainsKey($depName)) {
            $external += "$item -> $depName ($depVer)"
            continue
        }

        $want = $producerMajor[$depName]
        $have = Get-Major $depVer
        $isBare = $depVer.Trim() -match '^\d+$'

        if ($have -ne $want) {
            $stale += "$item -> ${depName}: declares major $have but $depName ships major $want"
            if ($Fix) { $lines[$i] = $lines[$i] -replace [regex]::Escape($depVer), "$want"; $changed = $true }
        }
        elseif (-not $isBare) {
            $reformatted += "$item -> ${depName}: $depVer -> $want"
            if ($Fix) { $lines[$i] = $lines[$i] -replace [regex]::Escape($depVer), "$want"; $changed = $true }
        }
        else { $okCount++ }
    }
    if ($changed) { [System.IO.File]::WriteAllLines($file.FullName, $lines) }
}

Write-Host ""
Write-Host "Scanned $($manifests.Count) manifests."
Write-Host "  in-marketplace deps already correct (bare major): $okCount"
Write-Host "  external deps (NuGet / core-shipped, not validated): $($external.Count)"

if ($stale.Count) {
    Write-Host ""
    Write-Host ($Fix ? "FIXED stale majors:" : "STALE - declared major != shipped major:") -ForegroundColor Yellow
    $stale | ForEach-Object { Write-Host "    $_" }
}
if ($reformatted.Count) {
    Write-Host ""
    Write-Host ($Fix ? "REFORMATTED to bare major:" : "NON-BARE form (consider -Fix):") -ForegroundColor Yellow
    $reformatted | ForEach-Object { Write-Host "    $_" }
}

$problems = $stale.Count + $reformatted.Count
if ($problems -eq 0) {
    Write-Host ""
    Write-Host "All in-marketplace dependency majors are correct and in bare form." -ForegroundColor Green
    exit 0
}
if ($Fix) {
    Write-Host ""
    Write-Host "Applied $problems fix(es)." -ForegroundColor Green
    exit 0
}
exit 1
