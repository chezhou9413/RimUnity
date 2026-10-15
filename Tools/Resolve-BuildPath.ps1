#供 Tools 下的构建、验证、部署脚本共用：把缺失的绝对路径变成带指引的报错。
#规则：显式参数优先，其次环境变量，最后内置回退值；最终必须存在。
function Resolve-BuildPath {
    param(
        # 不能用 Mandatory：调用方在未传参时会传空字符串占位，
        # Mandatory 会先抛 "Cannot bind argument ... empty string"，把下面带指引的报错整个吃掉。
        [string]$Path,
        [string]$EnvVar = '',
        [string]$Fallback = '',
        [Parameter(Mandatory = $true)][string]$ParamName,
        [string]$Purpose = '所需目录',
        [ValidateSet('Container', 'Leaf')][string]$Kind = 'Container'
    )
    if (-not $Path) { $Path = [Environment]::GetEnvironmentVariable($EnvVar) }
    if (-not $Path) { $Path = $Fallback }
    if (-not $Path) {
        $hint = if ($EnvVar) { "，或设置环境变量 $EnvVar" } else { '' }
        throw ("未提供{0}：{1}。请用 -{1} 指定{2}；也可在 Source/Directory.Build.props 中覆盖同名属性。" -f $Purpose, $ParamName, $hint)
    }
    if (-not (Test-Path -LiteralPath $Path -PathType $Kind)) {
        $hint = if ($EnvVar) { "，或修正环境变量 $EnvVar" } else { '' }
        throw ("{0}不存在：{1}（{2}）。请确认 -{2} 指向的位置{3}。" -f $Purpose, $Path, $ParamName, $hint)
    }
    return $Path
}
function Resolve-BuildGameRoot {
    param([string]$Path, [switch]$Required)
    $Fallback = ''
    if ($Required) { $Fallback = Join-Path $env:ProgramFiles 'Steam/steamapps/common/RimWorld' }
    try {
        return Resolve-BuildPath -Path $Path -EnvVar 'RIMWORLD_DIR' -Fallback $Fallback `
            -ParamName 'GameRoot' -Purpose 'RimWorld 游戏目录'
    } catch {
        throw $_.Exception.Message + ' 该目录应含 Assembly-CSharp.dll 与 Waylith.dll。'
    }
}
function Resolve-BuildUnityEditor {
    param([string]$Path)
    return Resolve-BuildPath -Path $Path -EnvVar 'UNITY_EDITOR_DIR' -ParamName 'UnityEditorDir' `
        -Purpose 'Unity 编辑器数据目录（含 Managed 子目录）'
}
function Resolve-BuildUnityProject {
    param([string]$Path)
    return Resolve-BuildPath -Path $Path -EnvVar 'UNITY_PROJECT' -ParamName 'UnityProject' `
        -Purpose '已导入 RimUnity 包的 Unity 工程目录（含 Library/ScriptAssemblies）'
}
