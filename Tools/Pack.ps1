param(
    [string]$Output = '',
    [string]$AssemblyDirectory = '',
    [string]$TempRoot = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
& (Join-Path $PSScriptRoot 'Sync-Manual.ps1')
if ([string]::IsNullOrEmpty($Output)) { $Output = Join-Path $repo 'artifacts' }
if ([string]::IsNullOrEmpty($TempRoot)) { $TempRoot = Join-Path ([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) "RimUnity/Packaging-$([Guid]::NewGuid().ToString('N'))" }
if ([string]::IsNullOrEmpty($AssemblyDirectory)) { $AssemblyDirectory = Join-Path $repo '1.7\Assemblies' }
#发布前确认两侧产物齐全，避免生成缺少 Core 或离线工具的包。
$runtimeFiles = @('RimUnity.dll','RimUnity.Core.dll','RimUnity.Spine.dll')
$toolFiles = @('RimUnity.ShaderTool.exe','RimUnity.ShaderTool.dll','RimUnity.ShaderTool.deps.json','RimUnity.ShaderTool.runtimeconfig.json','RimUnity.ShaderTool.pdb')
$toolDirectory = Join-Path $repo 'Tools\bin\ShaderTool'
foreach ($name in $runtimeFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $AssemblyDirectory $name) -PathType Leaf)) { throw "缺少运行时产物：$name；请先执行 Tools/Build.ps1。" }
}
foreach ($name in $toolFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $toolDirectory $name) -PathType Leaf)) { throw "缺少离线工具产物：$name；请先编译 Source/RimUnity.ShaderTool。" }
}
$version = (Get-Content -LiteralPath (Join-Path $repo 'UnityPackage\package.json') -Encoding UTF8 -Raw | ConvertFrom-Json).version
$stage = Join-Path $TempRoot ([Guid]::NewGuid().ToString('N'))
$mod = Join-Path $stage 'RimUnity'
[IO.Directory]::CreateDirectory($Output) | Out-Null
& (Join-Path $PSScriptRoot 'Copy-RuntimePackage.ps1') -Destination $mod -AssemblyDirectory $AssemblyDirectory
#框架只发布运行时和内置 Shader；导出资源由各内容模组自行打包。
$modZip = Join-Path $Output "RimUnity-$version-preview.zip"
Compress-Archive -LiteralPath $mod -DestinationPath $modZip
$package = Join-Path $stage 'package'
Copy-Item -LiteralPath (Join-Path $repo 'UnityPackage') -Destination $package -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $package
$tool = Join-Path $package 'Tools~\ShaderTool'
[IO.Directory]::CreateDirectory($tool) | Out-Null
foreach ($name in $toolFiles) { Copy-Item -LiteralPath (Join-Path $toolDirectory $name) -Destination $tool }
$tgz = Join-Path $Output "com.chezhou.rimunity-$version.tgz"
& tar -czf $tgz -C $stage package
if ($LASTEXITCODE -ne 0) { throw 'UPM 包压缩失败。' }
$embeddedSource = Join-Path $repo 'artifacts/Embedded'
$embedded = Join-Path $stage 'RimUnity.Embedded'
[IO.Directory]::CreateDirectory($embedded) | Out-Null
foreach ($name in @('RimUnity.Embedded.dll','RimUnity.Embedded.targets','References')) {
    Copy-Item -LiteralPath (Join-Path $embeddedSource $name) -Destination $embedded -Recurse
}
Copy-Item -LiteralPath (Join-Path $repo 'Docs/zh-CN/EmbeddedRuntime.md') -Destination (Join-Path $embedded 'README.zh-CN.md')
Copy-Item -LiteralPath (Join-Path $repo 'Docs/en/EmbeddedRuntime.md') -Destination (Join-Path $embedded 'README.en.md')
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE') -Destination $embedded
Copy-Item -LiteralPath (Join-Path $repo 'ThirdParty') -Destination $embedded -Recurse
$embeddedZip = Join-Path $Output "RimUnity.Embedded-$version-preview.zip"
Compress-Archive -LiteralPath $embedded -DestinationPath $embeddedZip
$sums = @($modZip, $embeddedZip, $tgz) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $Output 'SHA256SUMS.txt'), $sums, [Text.UTF8Encoding]::new($false))
Write-Output "模组包：$modZip"
Write-Output "内置包：$embeddedZip"
Write-Output "UPM 包：$tgz"
Write-Output "打包暂存：$stage"
