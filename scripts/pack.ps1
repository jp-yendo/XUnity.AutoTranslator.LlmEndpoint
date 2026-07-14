<#
.SYNOPSIS
  Builds both target frameworks and packs per-loader release ZIPs.

.DESCRIPTION
  Produces drop-in archives whose directory layout overlays directly onto the
  target game/app directory, the same way the XUnity.AutoTranslator releases do.
  The Translators folder differs per mod loader, so one ZIP is produced per loader
  and runtime:

    net35  (Mono)   -> BepInEx\plugins\XUnity.AutoTranslator\Translators\
    net6.0 (IL2CPP)    UserLibs\Translators\  (MelonLoader)

.PARAMETER Version
  Version string used in the ZIP file names. Defaults to the AssemblyVersion in
  Properties\AssemblyInfo.cs.

.PARAMETER Configuration
  Build configuration. Defaults to Release.

.EXAMPLE
  pwsh scripts\pack.ps1
  pwsh scripts\pack.ps1 -Version 1.2.0
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "XUnity.AutoTranslator.LlmEndpoint.csproj"
$assembly = "XUnity.AutoTranslator.LlmEndpoint.dll"
$distDir = Join-Path $repoRoot "dist"
$stagingDir = Join-Path $distDir "staging"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $assemblyInfo = Get-Content (Join-Path $repoRoot "Properties\AssemblyInfo.cs") -Raw
    $match = [regex]::Match($assemblyInfo, 'AssemblyVersion\("(\d+)\.(\d+)\.(\d+)')
    if (-not $match.Success) { throw "Could not determine version. Pass -Version." }
    $Version = "{0}.{1}.{2}" -f $match.Groups[1].Value, $match.Groups[2].Value, $match.Groups[3].Value
}

Write-Host "Building $Configuration (net6.0;net35)..." -ForegroundColor Cyan
& dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# name suffix, target framework, in-archive relative directory for the DLL
$packages = @(
    @{ Name = "BepInEx";        Tfm = "net35";  Path = "BepInEx\plugins\XUnity.AutoTranslator\Translators" },
    @{ Name = "BepInEx-IL2CPP"; Tfm = "net6.0"; Path = "BepInEx\plugins\XUnity.AutoTranslator\Translators" },
    @{ Name = "MelonMod";        Tfm = "net35";  Path = "UserLibs\Translators" },
    @{ Name = "MelonMod-IL2CPP"; Tfm = "net6.0"; Path = "UserLibs\Translators" }
)

if (Test-Path $stagingDir) { Remove-Item $stagingDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

foreach ($pkg in $packages) {
    $dll = Join-Path $repoRoot ("bin\{0}\{1}\{2}" -f $Configuration, $pkg.Tfm, $assembly)
    if (-not (Test-Path $dll)) { throw "Missing build output: $dll" }

    $zipName = "XUnity.AutoTranslator.LlmEndpoint-{0}-{1}.zip" -f $pkg.Name, $Version
    $zipPath = Join-Path $distDir $zipName
    $stage = Join-Path $stagingDir $pkg.Name
    $target = Join-Path $stage $pkg.Path
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item $dll (Join-Path $target $assembly) -Force

    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -Force
    Write-Host ("Packed {0}  ->  {1}\{2}" -f $zipName, $pkg.Path, $assembly) -ForegroundColor Green
}

Remove-Item $stagingDir -Recurse -Force
Write-Host "`nArtifacts in $distDir" -ForegroundColor Cyan
Get-ChildItem $distDir -Filter *.zip | Select-Object Name, Length | Format-Table -AutoSize
