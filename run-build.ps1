[CmdletBinding()]
param(
  [switch]$SkipPack,
  [switch]$NoClean,
  [switch]$Publish,
  [switch]$VerboseHashes
)

$ErrorActionPreference = 'Stop'
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

function Get-TcliConfigFlag {
  if (-not (Get-Command tcli -ErrorAction SilentlyContinue)) { return $null }
  $old = $ErrorActionPreference
  $ErrorActionPreference = 'SilentlyContinue'
  try {
    # Capture both streams but suppress turning stderr into a terminating error
    $output = (& tcli --help 2>&1) -join "`n"
  } catch {
    return '--config-path'
  } finally {
    $ErrorActionPreference = $old
  }
  if ($output -match '--config-path') { return '--config-path' }
  if ($output -match '(?m)^\s*--config(\s|$)') { return '--config' }
  return '--config-path'
}

function Read-ModVersion {
  $props = Join-Path $PSScriptRoot 'Version.props'
  $raw = Get-Content $props -Raw
  if ($raw -match '<ModVersion>(.+?)</ModVersion>') { return $Matches[1].Trim() }
  throw "Could not parse <ModVersion> from Version.props"
}

$cfgFlag = Get-TcliConfigFlag
$version = Read-ModVersion

Write-Host "== Time Never Stops Build Runner ==" -ForegroundColor Cyan
Write-Host "Version: $version"
Write-Host "SkipPack: $SkipPack  NoClean: $NoClean  Publish: $Publish" -ForegroundColor DarkGray
if ($cfgFlag) { Write-Host "tcli config flag: $cfgFlag" -ForegroundColor DarkGray }

Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

$psArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File','build-pack.ps1')
if ($SkipPack) { $psArgs += '-SkipPack' }
if ($NoClean)  { $psArgs += '-NoClean' }
powershell @psArgs

$il2cppDll = 'TNS_Project/bin/IL2CPP/net6.0/TimeNeverStops_IL2Cpp.dll'
$monoDll   = 'TNS_Project/bin/MONO/netstandard2.1/TimeNeverStops_Mono.dll'

if ($VerboseHashes) {
  foreach ($p in $il2cppDll,$monoDll) {
    if (Test-Path $p) {
      $h = (Get-FileHash $p -Algorithm SHA256).Hash
      Write-Host "SHA256 $(Split-Path $p -Leaf): $h"
    }
  }
}

if ($Publish -and -not $SkipPack) {
  if (-not $cfgFlag) { throw "tcli not found (cannot publish)." }
  Write-Host "Publishing IL2CPP package..."
  tcli publish $cfgFlag ./packaging/thunderstore_il2cpp.toml
  Write-Host "Publishing MONO package..."
  tcli publish $cfgFlag ./packaging/thunderstore_mono.toml
}

$stopwatch.Stop()
Write-Host "Done in $([int]$stopwatch.Elapsed.TotalSeconds)s."