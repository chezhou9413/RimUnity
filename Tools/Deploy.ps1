param(
    [string]$GameRoot = '',
    [string]$BackupRoot = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-BuildPath.ps1')
$GameRoot = Resolve-BuildGameRoot -Path $(if ($PSBoundParameters.ContainsKey('GameRoot')) { $GameRoot } else { '' }) -Required
$repo = Split-Path $PSScriptRoot -Parent
$mods = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods'))
$BackupRoot = Resolve-BuildBackupRoot -Path $BackupRoot -GameRoot $GameRoot
$destination = [IO.Path]::GetFullPath((Join-Path $mods 'RimUnity'))
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'Assembly-CSharp.dll'))) { throw '游戏目录无效。' }
if ([IO.Path]::GetFullPath($destination) -eq [IO.Path]::GetFullPath($repo)) { throw '部署目录不能等于开发目录。' }
if ((Split-Path $destination -Parent) -ne $mods) { throw '部署目录超出游戏 Mods 目录。' }
$id = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $mods ".rimunity-deploy-$id"
$backup = Join-Path ([IO.Path]::GetFullPath($BackupRoot)) "Deploy-$id"
& (Join-Path $PSScriptRoot 'Copy-RuntimePackage.ps1') -Destination $stage
#发布身份随开发目录一起打包，替换整个发布目录以避免文件残留。
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw "游戏已启动，待部署文件保留在：$stage；已存在的发布目录尚未替换，未产生备份。" }
if (Test-Path -LiteralPath $destination) {
    [IO.Directory]::CreateDirectory((Split-Path $backup -Parent)) | Out-Null
    if (Test-Path -LiteralPath $backup) { throw "备份目录已存在，可能无效或重复运行：$backup" }
    Move-Item -LiteralPath $destination -Destination $backup
}
try { Move-Item -LiteralPath $stage -Destination $destination }
catch {
    $deployError = $_.Exception
    if (Test-Path -LiteralPath $backup) {
        try {
            if (Test-Path -LiteralPath $destination) { throw "目标目录仍存在，无法直接恢复旧版：$destination" }
            Move-Item -LiteralPath $backup -Destination $destination
        }
        catch {
            throw [InvalidOperationException]::new("部署失败：$($deployError.Message)；回滚也失败：$($_.Exception.Message)。请检查目标：$destination；备份：$backup；暂存：$stage", $deployError)
        }
        throw [InvalidOperationException]::new("部署失败：$($deployError.Message)；旧版已恢复到：$destination；待部署文件：$stage", $deployError)
    }
    throw [InvalidOperationException]::new("部署失败：$($deployError.Message)；没有旧版备份可回滚。目标：$destination；待部署文件：$stage", $deployError)
}
Write-Output "已部署：$destination"
if (Test-Path -LiteralPath $backup) { Write-Output "旧版备份：$backup" }
Write-Output '请自行在游戏中启用 Harmony 与 RimUnity；启用列表和存档未由脚本修改。'
