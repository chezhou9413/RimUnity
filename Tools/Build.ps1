param([string]$GameRoot = 'E:\steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $repo 'Source\RimUnity.Spine\RimUnity.Spine.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot"
if ($LASTEXITCODE -ne 0) { throw 'RimUnity 编译失败。' }
