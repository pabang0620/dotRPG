[CmdletBinding()]
param(
    [string]$UnityEditorData,
    [string]$RepositoryRoot,
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'dot-rpg-level-rewards-client')
)

$ErrorActionPreference = 'Stop'
if (-not $RepositoryRoot) { $RepositoryRoot = Join-Path $PSScriptRoot '../../..' }
$repository = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if (-not $UnityEditorData) {
    $editors = Join-Path $env:ProgramFiles 'Unity/Hub/Editor'
    $UnityEditorData = Get-ChildItem -LiteralPath $editors -Directory |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'Editor/Data' } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'NetCoreRuntime/dotnet.exe') } |
        Select-Object -First 1
}
if (-not $UnityEditorData) { throw 'Supply -UnityEditorData for a Unity editor with bundled .NET 8 SDK and runtime.' }
$dotnet = Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe'
$sdk = Get-ChildItem -LiteralPath (Join-Path $UnityEditorData 'DotNetSdk/sdk') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$referencePack = Get-ChildItem -LiteralPath (Join-Path $UnityEditorData 'DotNetSdk/packs/Microsoft.NETCore.App.Ref') -Directory |
    Where-Object Name -Like '8.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$runtime = Get-ChildItem -LiteralPath (Join-Path $UnityEditorData 'NetCoreRuntime/shared/Microsoft.NETCore.App') -Directory |
    Where-Object Name -Like '8.*' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk -or -not $referencePack -or -not $runtime) { throw 'Unity must include .NET 8 SDK, reference assemblies and runtime.' }
$compiler = Join-Path $sdk.FullName 'Roslyn/bincore/csc.dll'
$references = Join-Path $referencePack.FullName 'ref/net8.0'
[void](New-Item -ItemType Directory -Path $OutputDirectory -Force)
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$assembly = Join-Path $OutputDirectory 'LevelRewardClientRegression.dll'
$responseFile = Join-Path $OutputDirectory 'compile.rsp'
$log = Join-Path $OutputDirectory 'results.txt'
$sources = @(
    (Join-Path $repository 'Assets/Scripts/Runtime/Net/LevelRewardClient.cs'),
    (Join-Path $repository 'Assets/Scripts/Runtime/Net/MiniJson.cs'),
    (Join-Path $PSScriptRoot 'Stubs.cs'),
    (Join-Path $PSScriptRoot 'Program.cs')
)
$arguments = @('/nologo', '/nostdlib+', '/target:exe', '/langversion:9.0', '/warnaserror+', ('/out:"' + $assembly + '"'))
$arguments += Get-ChildItem -LiteralPath $references -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' }
$arguments += $sources | ForEach-Object { '"' + $_ + '"' }
$arguments | Set-Content -LiteralPath $responseFile -Encoding UTF8
@{
    runtimeOptions = @{
        tfm = 'net8.0'
        framework = @{ name = 'Microsoft.NETCore.App'; version = $runtime.Name }
    }
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'LevelRewardClientRegression.runtimeconfig.json') -Encoding UTF8
"Compiling actual LevelRewardClient.cs and MiniJson.cs with isolated network/Unity stubs." | Tee-Object -FilePath $log
Get-FileHash -LiteralPath $sources[0] -Algorithm SHA256 | ForEach-Object { "LevelRewardClient.cs SHA256: $($_.Hash)" } | Tee-Object -FilePath $log -Append
& $dotnet $compiler /noconfig ('@' + $responseFile) 2>&1 | Tee-Object -FilePath $log -Append
if ($LASTEXITCODE -ne 0) { throw "Client regression harness compilation failed. See $log" }
& $dotnet $assembly 2>&1 | Tee-Object -FilePath $log -Append
if ($LASTEXITCODE -ne 0) { throw "Client regression tests failed. See $log" }
"Results saved to $log"
