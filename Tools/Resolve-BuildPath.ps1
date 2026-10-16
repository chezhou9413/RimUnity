#供 Tools 下的构建、验证、部署脚本共用：把缺失的绝对路径变成带指引的报错，并校验必需文件。
#规则：显式参数优先，其次环境变量，最后内置回退值；目录必须存在，且要满足该用途的必需文件。
#检查必需文件，通配符只用于文件名。
function Test-BuildPathItem {
    param([string]$Root, [string]$Item)
    # 文件名含通配符时按模式匹配（如 Managed/UnityEngine/*.dll），否则按精确文件检查。
    # 注意不能用 EndsWith('*')：'Managed/UnityEngine/*.dll' 的末字符是 'l'。
    $leaf = [IO.Path]::GetFileName($Item)
    if ([WildcardPattern]::ContainsWildcardCharacters($leaf)) {
        # -Filter 不支持路径分隔符，必须把目录部分与文件名通配拆开。
        $dir = [IO.Path]::GetDirectoryName($Item)
        $base = if ($dir) { Join-Path $Root $dir } else { $Root }
        if ([WildcardPattern]::ContainsWildcardCharacters($base)) {
            throw ("内部错误：不支持该通配写法：{0}" -f $Item)
        }
        if (-not (Test-Path -LiteralPath $base -PathType Container)) { return $false }
        return @(Get-ChildItem -LiteralPath $base -Filter $leaf -File -ErrorAction Stop).Count -gt 0
    }
    return Test-Path -LiteralPath (Join-Path $Root $Item) -PathType Leaf
}
#汇总当前目录缺少的必需文件。
function Test-BuildPathContent {
    param([string]$Path, [string[]]$Require = @())
    $missing = @()
    foreach ($item in $Require) {
        if (-not (Test-BuildPathItem -Root $Path -Item $item)) { $missing += $item }
    }
    return $missing
}
#按参数、环境变量、默认值顺序解析并检查路径。
function Resolve-BuildPath {
    param(
        # 不能用 Mandatory：调用方在未传参时会传空字符串占位，
        # Mandatory 会先抛 "Cannot bind argument ... empty string"，把下面带指引的报错整个吃掉。
        [string]$Path,
        [string]$EnvVar = '',
        [string]$Fallback = '',
        [Parameter(Mandatory = $true)][string]$ParamName,
        [string]$Purpose = '所需目录',
        [ValidateSet('Container', 'Leaf')][string]$Kind = 'Container',
        [string[]]$Require = @(),
        # 输出位置（如备份根）首次运行时还不存在，允许调用方随后自行创建。
        [switch]$AllowMissing
    )
    if ($Require.Count -and $Kind -ne 'Container') {
        throw ("内部错误：Kind={0} 时不能做内容校验。" -f $Kind)
    }
    if (-not $Path) { $Path = [Environment]::GetEnvironmentVariable($EnvVar) }
    if (-not $Path) { $Path = $Fallback }
    if ($Path -and $Path.Contains('$(')) { throw ("路径参数 {0} 未展开：{1}" -f $ParamName, $Path) }
    if (-not $Path) {
        $hint = if ($EnvVar) { "，或设置环境变量 $EnvVar" } else { '' }
        throw ("未提供{0}：{1}。请用 -{1} 指定{2}；也可在 Source/Directory.Build.props 中覆盖同名属性。" -f $Purpose, $ParamName, $hint)
    }
    if ((-not $AllowMissing -or (Test-Path -LiteralPath $Path)) -and -not (Test-Path -LiteralPath $Path -PathType $Kind)) {
        $hint = if ($EnvVar) { "，或修正环境变量 $EnvVar" } else { '' }
        throw ("{0}不存在：{1}（{2}）。请确认 -{2} 指向的位置{3}。" -f $Purpose, $Path, $ParamName, $hint)
    }
    $missing = Test-BuildPathContent -Path $Path -Require $Require
    if ($missing.Count) {
        # 一次列全缺失文件，避免读者反复试错。
        throw ("{0}缺少必需文件：{1}（{2}）。请确认该目录指向的位置，或用 -{3} 指定正确的目录。" -f $Purpose, ($missing -join '、'), $Path, $ParamName)
    }
    return $Path
}
#游戏引用必须来自包含目标程序集的安装目录。
function Resolve-BuildGameRoot {
    param([string]$Path, [switch]$Required)
    $Fallback = ''
    if ($Required) { $Fallback = Join-Path $env:ProgramFiles 'Steam/steamapps/common/RimWorld' }
    return Resolve-BuildPath -Path $Path -EnvVar 'RIMWORLD_DIR' -Fallback $Fallback `
        -ParamName 'GameRoot' -Purpose 'RimWorld 游戏目录' `
        -Require @('Assembly-CSharp.dll', 'Waylith.dll')
}
#编辑器目录须包含编译导出工具使用的托管程序集。
function Resolve-BuildUnityEditor {
    param([string]$Path)
    return Resolve-BuildPath -Path $Path -EnvVar 'UNITY_EDITOR_DIR' -ParamName 'UnityEditorDir' `
        -Purpose 'Unity 编辑器数据目录' `
        -Require @('Managed/Newtonsoft.Json.dll', 'Managed/UnityEngine/*.dll')
}
#编辑器源码编译依赖工程中已生成的 UGUI 与 TMP 程序集。
function Resolve-BuildUnityProject {
    param([string]$Path)
    return Resolve-BuildPath -Path $Path -EnvVar 'UNITY_PROJECT' -ParamName 'UnityProject' `
        -Purpose '已导入 RimUnity 包的 Unity 工程目录' `
        -Require @('Library/ScriptAssemblies/UnityEngine.UI.dll', 'Library/ScriptAssemblies/Unity.TextMeshPro.dll')
}
#沿用调用方已解析的游戏目录；备份子目录由部署或升级流程按需创建。
function Resolve-BuildBackupRoot {
    param([string]$Path, [Parameter(Mandatory = $true)][string]$GameRoot)
    $fallback = Join-Path $GameRoot '.rimunity-backups'
    $root = Resolve-BuildPath -Path $Path -EnvVar 'RIMUNITY_BACKUP_DIR' -Fallback $fallback `
        -ParamName 'BackupRoot' -Purpose '备份根目录' -AllowMissing
    return [IO.Path]::GetFullPath($root)
}
