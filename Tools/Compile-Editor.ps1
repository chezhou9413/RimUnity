param(
    [string]$UnityEditorDir = '',
    [string]$UnityProject = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-BuildPath.ps1')
$UnityEditorDir = Resolve-BuildUnityEditor -Path $(if ($PSBoundParameters.ContainsKey('UnityEditorDir')) { $UnityEditorDir } else { '' })
$UnityProject = Resolve-BuildUnityProject -Path $(if ($PSBoundParameters.ContainsKey('UnityProject')) { $UnityProject } else { '' })
$repo = Split-Path $PSScriptRoot -Parent
#只编译 UPM 源码，不启动编辑器，不执行导出或场景。
& dotnet build (Join-Path $repo 'Source\RimUnity.UnityCompile\RimUnity.UnityCompile.csproj') -c Release --nologo "-p:UnityEditorDir=$UnityEditorDir" "-p:UnityProject=$UnityProject"
if ($LASTEXITCODE -ne 0) { throw 'UPM 编译失败。' }
