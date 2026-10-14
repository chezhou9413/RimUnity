param([Parameter(Mandatory)][string]$Destination, [string]$AssemblyDirectory = '')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$AssemblyDirectory) { $AssemblyDirectory = Join-Path $repo '1.7\Assemblies' }
if ((Test-Path -LiteralPath $Destination) -and (Get-ChildItem -LiteralPath $Destination -Force)) { throw '发布暂存目录必须为空。' }

#部署与压缩包使用同一份运行文件清单。
$files = @('About\About.xml', 'About\Preview.png', 'About\ModIcon.png', 'LoadFolders.xml', 'LICENSE', 'README.md', 'README.en.md',
    '1.6\README.txt', '1.7\Defs\RimUnityShaders.xml', '1.7\Defs\RimUnityStandardParticles.xml')
foreach ($directory in @('1.6\Languages', 'Docs\zh-CN', 'Docs\en', 'Docs\Images', 'ThirdParty')) {
    $files += Get-ChildItem -LiteralPath (Join-Path $repo $directory) -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($repo, $_.FullName) }
}
$files += Get-ChildItem -LiteralPath (Join-Path $repo '1.7\Shaders\RimUnity') -Filter '*.glsl' -File |
    Where-Object Name -ne 'PreviewOpaque.frag.glsl' | ForEach-Object { [IO.Path]::GetRelativePath($repo, $_.FullName) }
foreach ($relative in $files) {
    $target = Join-Path $Destination $relative
    [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
}
foreach ($name in @('RimUnity.Core.dll', 'RimUnity.dll')) {
    $target = Join-Path $Destination "1.7\Assemblies\$name"
    [IO.Directory]::CreateDirectory((Split-Path $target -Parent)) | Out-Null
    Copy-Item -LiteralPath (Join-Path $AssemblyDirectory $name) -Destination $target
}
