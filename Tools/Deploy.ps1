param([string]$GameRoot = 'E:\steam\steamapps\common\RimWorld')
$ErrorActionPreference = 'Stop'
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw '请先由你关闭 RimWorld，再部署程序集。脚本不会自动关闭游戏。' }
$repo = Split-Path $PSScriptRoot -Parent
$mods = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods'))
$destination = [IO.Path]::GetFullPath((Join-Path $mods 'RimUnity'))
if (!(Test-Path -LiteralPath (Join-Path $GameRoot 'Assembly-CSharp.dll'))) { throw '游戏目录无效。' }
if ([IO.Path]::GetFullPath($destination) -eq [IO.Path]::GetFullPath($repo)) { throw '部署目录不能等于开发目录。' }
if ((Split-Path $destination -Parent) -ne $mods) { throw '部署目录超出游戏 Mods 目录。' }
$id = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $mods ".rimunity-deploy-$id"
$backup = "E:\ModdevMics\Backups\RimUnity\Deploy-$id"
& (Join-Path $PSScriptRoot 'Copy-RuntimePackage.ps1') -Destination $stage
#发布身份随开发目录一起打包，替换整个发布目录以避免文件残留。
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw "游戏已启动，待部署文件保留在：$stage" }
if (Test-Path -LiteralPath $destination) {
    [IO.Directory]::CreateDirectory((Split-Path $backup -Parent)) | Out-Null
    Move-Item -LiteralPath $destination -Destination $backup
}
try { Move-Item -LiteralPath $stage -Destination $destination }
catch {
    if (Test-Path -LiteralPath $backup) { Move-Item -LiteralPath $backup -Destination $destination }
    throw
}
Write-Output "已部署：$destination"
Write-Output "旧版备份：$backup"
Write-Output '请自行在游戏中启用 Harmony 与 RimUnity；启用列表和存档未由脚本修改。'
