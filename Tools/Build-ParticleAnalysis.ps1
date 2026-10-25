param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$HarmonyPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$analysis = Join-Path $repo 'Artifacts/ParticleAnalysis'
& dotnet build (Join-Path $repo 'Source/RimUnity.Spine/RimUnity.Spine.csproj') -c Release "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath" -p:RimUnityParticleAnalysis=true -p:SkipEmbedded=true
if ($LASTEXITCODE -ne 0) { throw 'Particle analysis build failed.' }
& (Join-Path $PSScriptRoot 'Build-Embedded.ps1') -GameRoot $GameRoot -HarmonyPath $HarmonyPath -RuntimeDirectory (Join-Path $analysis 'Assemblies') -OutputDirectory (Join-Path $analysis 'Embedded')
if ($LASTEXITCODE -ne 0) { throw 'Particle analysis payload build failed.' }
Write-Output "Analysis package: $analysis"
