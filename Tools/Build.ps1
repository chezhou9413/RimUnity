param([string]$GameRoot = '', [string]$HarmonyPath = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-BuildPath.ps1')
$GameRoot = Resolve-BuildGameRoot -Path $(if ($PSBoundParameters.ContainsKey('GameRoot')) { $GameRoot } else { '' }) -Required
$HarmonyPath = Resolve-BuildPath -Path $HarmonyPath -EnvVar 'HARMONY_PATH' -ParamName 'HarmonyPath' -Purpose 'Harmony 程序集' -Kind Leaf
$repo = Split-Path $PSScriptRoot -Parent
& dotnet build (Join-Path $repo 'Source\RimUnity.Spine\RimUnity.Spine.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw 'RimUnity 编译失败。' }
