<#
.SYNOPSIS
    Compile a marketplace module and install it into ~/.clavis/modules.

.DESCRIPTION
    Closes a gap in the development loop. Modules are normally compiled by the host on launch, and the
    CompileTest harness builds every item into a throwaway staging dir it then deletes - so neither leaves an
    updated module where dependent items can see it.

    That matters because items are compiled in alphabetical order against whatever is already installed in
    ~/.clavis/modules. Change a contract module and the plugins that consume it still compile against the
    previous copy: `claude-bridge` sorts before `session-contracts`, so a contract added in the same change
    fails with CS0246 until the module is installed. This script installs it without launching the app.

    The project file is synthesized from PLUGIN.md frontmatter, built against the same three reference roots
    the kernel probes at runtime, and removed afterwards - the marketplace stays pure source.

.PARAMETER Name
    Module folder name(s) under modules/, e.g. session-contracts. Accepts several.

.PARAMETER ShellBin
    The host Shell output directory holding the closure DLLs. Defaults to the Debug build in the sibling
    clavis host repo.

.EXAMPLE
    ./tools/Install-Module.ps1 session-contracts, workspace-contracts
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string[]] $Name,

    [string] $ShellBin = "$env:USERPROFILE\Repos\FS\clavis\src\FabioSoft.Clavis.Shell\bin\Debug\net10.0-windows"
)

$ErrorActionPreference = 'Stop'

$clavisHome = Join-Path $env:USERPROFILE '.clavis'
$marketplace = Join-Path $clavisHome 'marketplaces\clavis-marketplace'
$modulesDir = Join-Path $clavisHome 'modules'
$librariesDir = Join-Path $clavisHome 'libraries'

if (-not (Test-Path $ShellBin)) { throw "Shell output not found: $ShellBin (build the host first)" }
if (-not (Test-Path $modulesDir)) { throw "Modules directory not found: $modulesDir (run the app once)" }

# Only the frontmatter fields that shape a module build. Anything else in PLUGIN.md is catalog metadata the
# compiler does not need, so it is deliberately not parsed here.
function Read-Frontmatter {
    param([string] $Path)

    $lines = Get-Content $Path
    if ($lines[0] -ne '---') { throw "No frontmatter in $Path" }

    $spec = @{ Sources = @(); Packages = @(); Language = 'fsharp'; UseWpf = $false }
    $section = ''

    foreach ($line in $lines[1..($lines.Count - 1)]) {
        if ($line -eq '---') { break }

        # A list entry continues whichever key opened the list.
        if ($line -match '^\s+-\s*(.+)$') {
            $entry = $Matches[1].Trim()
            switch ($section) {
                'sources' { $spec.Sources += $entry }
                'packages' {
                    if ($entry -match 'name:\s*([^,}]+).*version:\s*([^,}\s]+)') {
                        $spec.Packages += @{ Name = $Matches[1].Trim(); Version = $Matches[2].Trim() }
                    }
                }
            }
            continue
        }

        if ($line -match '^([A-Za-z]+):\s*(.*)$') {
            $key = $Matches[1]
            $value = $Matches[2].Trim()
            $section = $key.ToLowerInvariant()
            switch ($section) {
                'assemblyname'  { $spec.AssemblyName = $value }
                'version'       { $spec.Version = $value }
                'rootnamespace' { $spec.RootNamespace = $value }
                'language'      { if ($value) { $spec.Language = $value } }
                'usewpf'        { $spec.UseWpf = ($value -eq 'true') }
            }
        }
    }

    if (-not $spec.AssemblyName) { throw "No assemblyName in $Path" }
    if ($spec.Sources.Count -eq 0) { throw "No sources in $Path" }
    $spec
}

function New-ProjectFile {
    param([hashtable] $Spec, [string] $Directory)

    $extension = if ($Spec.Language -eq 'csharp') { 'csproj' } else { 'fsproj' }
    $path = Join-Path $Directory "$($Spec.AssemblyName).$extension"

    $packages = ($Spec.Packages | ForEach-Object {
        "    <PackageReference Include=`"$($_.Name)`" Version=`"$($_.Version)`" />"
    }) -join "`n"

    # Ordering is load-bearing in F#, so the frontmatter order is preserved verbatim.
    $sources = ($Spec.Sources | ForEach-Object { "    <Compile Include=`"$_`" />" }) -join "`n"

    $content = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <AssemblyName>$($Spec.AssemblyName)</AssemblyName>
    <RootNamespace>$($Spec.RootNamespace)</RootNamespace>
    <Version>$($Spec.Version)</Version>
    <UseWPF>$($Spec.UseWpf.ToString().ToLowerInvariant())</UseWPF>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
  <ItemGroup>
$packages
  </ItemGroup>
  <ItemGroup>
$sources
  </ItemGroup>
</Project>
"@

    Set-Content -Path $path -Value $content -Encoding UTF8
    $path
}

$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$env:DOTNET_ROOT = 'C:\Program Files\dotnet'

$failed = @()

foreach ($moduleName in $Name) {
    $moduleDir = Join-Path $marketplace "modules\$moduleName"
    if (-not (Test-Path $moduleDir)) { throw "Module not found: $moduleDir" }

    Write-Host "==> $moduleName" -ForegroundColor Cyan
    $spec = Read-Frontmatter (Join-Path $moduleDir 'PLUGIN.md')
    $projectPath = New-ProjectFile -Spec $spec -Directory $moduleDir
    $output = Join-Path $moduleDir 'obj\install-out'

    try {
        # The three reference roots are passed explicitly: the props' own fallback points at ~/.clavis, which
        # holds no DLLs at its root, so relying on it fails to resolve anything.
        & dotnet build $projectPath -c Release -o $output --nologo -v quiet `
            -p:ClavisExeDir=$ShellBin `
            -p:ClavisLibrariesDir=$librariesDir `
            -p:ClavisModulesDir=$modulesDir
        if ($LASTEXITCODE -ne 0) { $failed += $moduleName; continue }

        $produced = Join-Path $output "$($spec.AssemblyName).dll"
        if (-not (Test-Path $produced)) { throw "Build produced no $($spec.AssemblyName).dll" }

        Copy-Item $produced (Join-Path $modulesDir "$($spec.AssemblyName).dll") -Force
        Write-Host "    installed $($spec.AssemblyName).dll $($spec.Version)" -ForegroundColor Green
    }
    finally {
        # The marketplace is pure source: the synthesized project must not survive the build.
        Remove-Item $projectPath -Force -ErrorAction SilentlyContinue
        Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if ($failed.Count -gt 0) {
    Write-Host "FAILED: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host 'All modules installed.' -ForegroundColor Green
