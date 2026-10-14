<#
.SYNOPSIS
  Uploads the release player (Builds\WindowsRelease) to Steam with SteamPipe:
  steamcmd +login <user> +run_app_build <abs app_build.vdf> +quit
.DESCRIPTION
  Fills app_build.vdf.template and depot_build_windows.vdf.template (next to this script) into tools\steam\output\
  (__APPID__, __DEPOTID__, __CONTENTROOT__ = <repo>\Builds\WindowsRelease, __BUILDDESC__, __SETLIVE__) and runs steamcmd.
  Refuses to run when Builds\WindowsRelease\dotRPG.exe is missing or when a steam_appid.txt is anywhere inside
  Builds\WindowsRelease (BuildScript writes it only for development / Spacewar builds; a release upload never ships it).
  No password is accepted or stored here: steamcmd asks for it (and for Steam Guard) itself.
  Output lines: "UPLOAD REFUSED <reason>", "DRY RUN ...", "UPLOAD OK ...", "UPLOAD FAIL ...". Exit code 0 / 1 (or steamcmd's).
.PARAMETER AppId
  Steam App ID assigned to dotRPG in Steamworks.
.PARAMETER DepotId
  Windows depot ID (Steamworks > SteamPipe > Depots).
.PARAMETER SteamUser
  Steam account with build upload rights for the app.
.PARAMETER SteamCmd
  Path of steamcmd.exe.
.PARAMETER Description
  Build description shown in Steamworks. Default: "dotRPG <exe write time>".
.PARAMETER SetLive
  Optional beta branch to set the build live on. Empty = not set live. SteamPipe cannot set the default branch live;
  do that on the Steamworks site (SteamPipe > Builds).
.PARAMETER DryRun
  Write the VDF files and print the steamcmd command without running it.
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\steam\upload.ps1 -AppId 1234560 -DepotId 1234561 -SteamUser builder -DryRun
#>
[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)][uint32]$AppId,
  [Parameter(Mandatory = $true)][uint32]$DepotId,
  [Parameter(Mandatory = $true)][string]$SteamUser,
  [string]$SteamCmd = 'C:\steamcmd\steamcmd.exe',
  [string]$Description = '',
  [string]$SetLive = '',
  [switch]$DryRun
)
$ErrorActionPreference = 'Stop'

function Stop-Upload([string]$message) {
  Write-Host "UPLOAD REFUSED $message" -ForegroundColor Red
  exit 1
}

# VDF values are written inside "...": quotes and line breaks are replaced (steamcmd does not unescape).
function ConvertTo-VdfValue([string]$value) {
  return (($value -replace '[\r\n\t]+', ' ') -replace '"', "'").Trim()
}

$toolDir = $PSScriptRoot
$root = [System.IO.Path]::GetFullPath((Join-Path $toolDir '..\..'))
$contentRoot = Join-Path $root 'Builds\WindowsRelease'
$exe = Join-Path $contentRoot 'dotRPG.exe'
$outDir = Join-Path $toolDir 'output'
$appTemplate = Join-Path $toolDir 'app_build.vdf.template'
$depotTemplate = Join-Path $toolDir 'depot_build_windows.vdf.template'
$user = $SteamUser.Trim()
$branch = $SetLive.Trim()

# ---- refusals
if ($AppId -eq 0) { Stop-Upload '-AppId is 0 (App ID not issued yet).' }
if ($DepotId -eq 0) { Stop-Upload '-DepotId is 0.' }
if ($AppId -eq 480) { Stop-Upload 'App ID 480 is only for development.' }
if ($user.Length -eq 0) { Stop-Upload '-SteamUser is empty.' }
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
  Stop-Upload "$exe not found. Build the release player first (DotRPG.EditorTools.BuildScript.BuildWindowsRelease)."
}
$appIdFiles = @(Get-ChildItem -LiteralPath $contentRoot -Recurse -File -Filter 'steam_appid.txt' -ErrorAction SilentlyContinue)
if ($appIdFiles.Count -gt 0) {
  Stop-Upload ('steam_appid.txt must not be uploaded: ' + (($appIdFiles | ForEach-Object { $_.FullName }) -join ', ') +
    '. BuildScript writes it only for development / Spacewar (480) builds; rebuild the release player.')
}
$manifestPath = Join-Path $contentRoot 'release-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { Stop-Upload 'Missing release-manifest.json; rebuild with BuildWindowsRelease.' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.release -ne $true -or $manifest.server -notmatch '^https://[^/]+') { Stop-Upload 'Invalid release manifest.' }
if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ine $manifest.executableSha256) { Stop-Upload 'Executable differs from the verified release build.' }
if ($branch -ieq 'default') {
  Stop-Upload '-SetLive default: SteamPipe cannot set the default branch live; do it on the Steamworks site.'
}
foreach ($template in @($appTemplate, $depotTemplate)) {
  if (-not (Test-Path -LiteralPath $template -PathType Leaf)) { Stop-Upload "template missing: $template" }
}
if (-not $DryRun -and -not (Test-Path -LiteralPath $SteamCmd -PathType Leaf)) {
  Stop-Upload "steamcmd not found at $SteamCmd (install it, run it once, or pass -SteamCmd)."
}

# ---- fill the templates
if ($Description.Trim().Length -eq 0) {
  $Description = 'dotRPG ' + (Get-Item -LiteralPath $exe).LastWriteTime.ToString('yyyy-MM-dd HH:mm')
}
$values = [ordered]@{
  '__APPID__'       = [string]$AppId
  '__DEPOTID__'     = [string]$DepotId
  '__CONTENTROOT__' = $contentRoot
  '__BUILDDESC__'   = ConvertTo-VdfValue $Description
  '__SETLIVE__'     = ConvertTo-VdfValue $branch
}

function Expand-Template([string]$path) {
  $text = [System.IO.File]::ReadAllText($path)
  foreach ($key in $values.Keys) { $text = $text.Replace($key, $values[$key]) }
  $left = [regex]::Match($text, '__[A-Z]+__')
  if ($left.Success) { Stop-Upload "unfilled placeholder $($left.Value) in $path" }
  return $text
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$appVdf = Join-Path $outDir 'app_build.vdf'
$depotVdf = Join-Path $outDir 'depot_build_windows.vdf'
[System.IO.File]::WriteAllText($appVdf, (Expand-Template $appTemplate), $utf8)
[System.IO.File]::WriteAllText($depotVdf, (Expand-Template $depotTemplate), $utf8)
"wrote $appVdf"
"wrote $depotVdf"
"content root $contentRoot, description '$($values['__BUILDDESC__'])', set live '$($values['__SETLIVE__'])'"

# ---- steamcmd
$steamArgs = @('+login', $user, '+run_app_build', $appVdf, '+quit')
$shown = ($steamArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' '
$commandLine = '"' + $SteamCmd + '" ' + $shown
if ($DryRun) {
  "DRY RUN (steamcmd not started): $commandLine"
  exit 0
}

"running: $commandLine"
& $SteamCmd @steamArgs
$code = $LASTEXITCODE
if ($code -ne 0) {
  Write-Host "UPLOAD FAIL steamcmd exit code $code (logs in $outDir\buildoutput)" -ForegroundColor Red
  exit $code
}
if ($branch.Length -gt 0) {
  "UPLOAD OK app $AppId depot $DepotId, set live on branch '$branch'"
} else {
  "UPLOAD OK app $AppId depot $DepotId, not set live (Steamworks > SteamPipe > Builds)"
}


