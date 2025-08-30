param(
  [string]$Version,  # ignored; version comes from Version.props
  [switch]$NoClean,
  [switch]$SkipPack,
  [switch]$StrictVersionMismatch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-TcliConfigFlag {
  if (-not (Get-Command tcli -ErrorAction SilentlyContinue)) { return $null }
  $old = $ErrorActionPreference
  $ErrorActionPreference = 'SilentlyContinue'
  try {
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
  $propsPath = "Version.props"
  if (-not (Test-Path $propsPath)) { throw "Version.props not found." }
  $raw = Get-Content $propsPath -Raw
  if ($raw -match '<ModVersion>(.+?)</ModVersion>') { return $Matches[1].Trim() }
  throw "Could not parse <ModVersion> from Version.props"
}

function Get-TomlVersion([string]$path) {
  $raw = Get-Content $path -Raw
  if ($raw -match 'versionNumber\s*=\s*"([^"]+)"') { return $Matches[1] }
  throw "versionNumber not found in $path"
}

function Set-TomlVersion([string]$path, [string]$newVersion) {
  (Get-Content $path -Raw) -replace 'versionNumber\s*=\s*"\d+\.\d+\.\d+"', "versionNumber = `"$newVersion`"" |
    Set-Content $path -Encoding UTF8
}

$modVersion = Read-ModVersion
$cfgFlag = Get-TcliConfigFlag

$csproj = "TNS_Project/Time Never Stops.csproj"
$il2cppConfig = "IL2CPP"
$monoConfig   = "MONO"

$packDir = "packaging"
$tsIl2cpp = Join-Path $packDir "thunderstore_il2cpp.toml"
$tsMono   = Join-Path $packDir "thunderstore_mono.toml"

Write-Host "== Time Never Stops Build =="
Write-Host "Version (from Version.props): $modVersion"
if ($cfgFlag) { Write-Host "tcli config flag: $cfgFlag" }
Write-Host ""

foreach ($toml in @($tsIl2cpp,$tsMono)) {
  if (-not (Test-Path $toml)) { throw "Missing $toml" }
  $tv = Get-TomlVersion $toml
  if ($tv -ne $modVersion) {
    if ($StrictVersionMismatch) {
      throw "Version mismatch: Version.props=$modVersion, $toml has $tv"
    }
    Write-Host "Updating versionNumber in $(Split-Path $toml -Leaf): $tv -> $modVersion"
    Set-TomlVersion $toml $modVersion
  }
}

if (-not $NoClean) {
  Write-Host "[Clean] $il2cppConfig"; dotnet clean "$csproj" -c $il2cppConfig | Out-Null
  Write-Host "[Clean] $monoConfig";   dotnet clean "$csproj" -c $monoConfig  | Out-Null
}

Write-Host "[Build] $il2cppConfig"
dotnet build "$csproj" -c $il2cppConfig /p:Version=$modVersion --nologo
Write-Host "[Build] $monoConfig"
dotnet build "$csproj" -c $monoConfig /p:Version=$modVersion --nologo

$il2cppDll = "TNS_Project/bin/IL2CPP/net6.0/TimeNeverStops_IL2Cpp.dll"
$monoDll   = "TNS_Project/bin/MONO/netstandard2.1/TimeNeverStops_Mono.dll"

if (-not (Test-Path $il2cppDll)) { throw "IL2CPP DLL missing at $il2cppDll" }
if (-not (Test-Path $monoDll))   { throw "MONO DLL missing at $monoDll" }

Write-Host "[Verify] Found IL2CPP DLL: $il2cppDll"
Write-Host "[Verify] Found MONO DLL:   $monoDll"

$il2cppHash = (Get-FileHash $il2cppDll -Algorithm SHA256).Hash
$monoHash   = (Get-FileHash $monoDll   -Algorithm SHA256).Hash
Write-Host "SHA256 IL2CPP: $il2cppHash"
Write-Host "SHA256 MONO  : $monoHash"

if ($SkipPack) {
  Write-Host "SkipPack set; skipping Thunderstore packaging."
  exit 0
}

if (-not $cfgFlag) {
  Write-Warning "tcli not found; skipping package build."
  exit 0
}

Write-Host "[Pack] IL2CPP"
tcli build $cfgFlag $tsIl2cpp
Write-Host "[Pack] MONO"
tcli build $cfgFlag $tsMono

Write-Host ""
Write-Host "== Completed =="
Write-Host "IL2CPP DLL : $il2cppDll"
Write-Host "MONO DLL   : $monoDll"
Write-Host "Hashes above can be added to release notes."