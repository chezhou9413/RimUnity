param([Parameter(Mandatory)][string]$Destination, [string]$AssemblyDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ((Test-Path -LiteralPath $Destination) -and (Get-ChildItem -LiteralPath $Destination -Force)) { throw '发布暂存目录必须为空。' }
if (!$AssemblyDirectory) { $AssemblyDirectory = Join-Path $repo '1.7/Assemblies' }
#独立版直接提供运行时，普通 DLL 的类型扫描不依赖延迟加载入口。
$runtimeFiles = @('RimUnity.Core.dll','RimUnity.dll','RimUnity.Spine.dll')
[xml]$about = Get-Content -LiteralPath (Join-Path $repo 'About/About.xml') -Raw
foreach ($name in $runtimeFiles) {
    $path = Join-Path $AssemblyDirectory $name
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少运行时产物：$name；请先执行 Tools/Build.ps1。" }
    if (([version][Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion).ToString(3) -ne ([version]$about.ModMetaData.modVersion).ToString(3)) {
        throw "运行时与 About 版本不一致，请重新构建：$name"
    }
}
#部署与压缩包复用清单；独立版的 DLL、Def、Shader 由原版正常加载。
$files = @('About\About.xml', 'About\PublishedFileId.txt', 'About\Preview.png', 'About\ModIcon.png', 'LoadFolders.xml', 'Analyzer.xml', 'LICENSE', 'README.md', 'README.en.md', '1.6\README.txt')
foreach ($directory in @('1.6\Languages', '1.7\Defs', '1.7\Shaders', '1.7\Languages', 'Docs\zh-CN', 'Docs\en', 'Docs\Images', 'ThirdParty')) {
    $root = Join-Path $repo $directory
    if (Test-Path -LiteralPath $root) {
        $files += Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object Name -ne 'PreviewOpaque.frag.glsl' | ForEach-Object { [IO.Path]::GetRelativePath($repo, $_.FullName) }
    }
}
foreach ($relative in $files) {
    $target = Join-Path $Destination $relative
    [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
}
foreach ($name in $runtimeFiles) {
    $target = Join-Path $Destination "1.7/Assemblies/$name"
    [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $AssemblyDirectory $name) -Destination $target
}
