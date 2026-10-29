param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$HarmonyPath,
    [Parameter(Mandatory)][string]$RuntimeDirectory,
    [string]$OutputFile = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$OutputFile) { $OutputFile = Join-Path $repo ('Artifacts/verify/trail-phases-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '.json') }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($OutputFile))) | Out-Null
$previousTiering = $env:DOTNET_TieredCompilation
try {
    # Stable call shapes for the offline comparison; never changes the game process.
    $env:DOTNET_TieredCompilation = '0'
    & dotnet run --project (Join-Path $PSScriptRoot 'ParticleDrawSpaceTests') -c Release -- $RuntimeDirectory $GameRoot (Split-Path $HarmonyPath -Parent) --trail-phases $OutputFile
    if ($LASTEXITCODE -ne 0) { throw 'Trail phase benchmark failed.' }
} finally {
    $env:DOTNET_TieredCompilation = $previousTiering
}
