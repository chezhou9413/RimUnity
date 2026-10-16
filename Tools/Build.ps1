param([string]$GameRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-BuildPath.ps1')
$GameRoot = Resolve-BuildGameRoot -Path $(if ($PSBoundParameters.ContainsKey('GameRoot')) { $GameRoot } else { '' }) -Required
$repo = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $repo 'Source\RimUnity.Spine\RimUnity.Spine.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot"
if ($LASTEXITCODE -ne 0) { throw 'RimUnity 编译失败。' }
