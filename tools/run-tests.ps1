<#
.SYNOPSIS
  Builds and runs every test project in the marketplace against a built Clavis.

  Tests compile in "runtime form" - the framework and modules resolve as file references
  injected by the runtime Directory.Build.props (the same one the Kernel uses to compile plugins on
  launch), so they need ClavisExeDir (a built Clavis Shell output) and ClavisSharedDir (the installed
  modules) on the environment. Both default sensibly; override -ExeDir for a different build.

.EXAMPLE
  ./tools/run-tests.ps1 -ExeDir C:\path\to\clavis\src\FabioSoft.Clavis.Shell\bin\Debug\net10.0-windows
#>
[CmdletBinding()]
param(
    [string] $ExeDir = $env:ClavisExeDir,
    [string] $SharedDir = (Join-Path $HOME '.clavis/plugins/shared')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Static gate first (no build needed): every in-marketplace dependency must declare the major it actually
# ships, in bare form. Catches the silent drift a major bump leaves in its consumers' manifests.
& (Join-Path $PSScriptRoot 'Validate-Dependencies.ps1')
if ($LASTEXITCODE -ne 0) {
    throw "Dependency validation failed (see above). Run tools/Validate-Dependencies.ps1 -Fix to normalize."
}

if (-not $ExeDir -or -not (Test-Path $ExeDir)) {
    throw "Set -ExeDir to a built Clavis Shell output directory (it supplies the framework reference DLLs)."
}
if (-not (Test-Path $SharedDir)) {
    throw "Shared directory not found: $SharedDir. Launch Clavis once so it compiles the modules."
}

$env:ClavisExeDir = (Resolve-Path $ExeDir).Path
$env:ClavisSharedDir = (Resolve-Path $SharedDir).Path

$projects =
    Get-ChildItem -Path $root -Recurse -Filter '*Tests.fsproj' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Sort-Object FullName

Write-Host "Running $($projects.Count) test projects against $env:ClavisExeDir`n"

$failed = @()
foreach ($p in $projects) {
    Write-Host "==> $($p.Name)"
    & dotnet test $p.FullName --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { $failed += $p.Name }
}

Write-Host "`n================ summary ================"
Write-Host "total:  $($projects.Count)"
Write-Host "passed: $($projects.Count - $failed.Count)"
Write-Host "failed: $($failed.Count)"
$failed | ForEach-Object { Write-Host "  FAIL $_" }
if ($failed.Count -gt 0) { exit 1 }
