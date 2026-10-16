param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$FixtureRoot)
$ErrorActionPreference = 'Stop'
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw '游戏正在运行；请结束当前会话后再执行测试。' }
$repo = Split-Path $PSScriptRoot -Parent
$run = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/GameVerification-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$modsRoot = [IO.Path]::GetFullPath((Join-Path $GameRoot 'Mods'))
$live = [IO.Path]::GetFullPath((Join-Path $modsRoot 'RimUnity'))
$backup = [IO.Path]::GetFullPath((Join-Path $run 'Original-RimUnity'))
$testMods = @('A','B','C') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $modsRoot "RimUnity.EmbeddedVerification.$_")) }
#所有移动、删除目标都先核对绝对路径；原模组备份在游戏扫描目录之外。
foreach ($path in @($live) + $testMods) {
    if ([IO.Path]::GetDirectoryName($path) -ne $modsRoot) { throw "测试路径越界：$path" }
}
if (!$backup.StartsWith([IO.Path]::GetFullPath($run) + [IO.Path]::DirectorySeparatorChar)) { throw '备份路径越界。' }
foreach ($path in $testMods) { if (Test-Path -LiteralPath $path) { throw "已有测试目录，未覆盖：$path" } }
if ((Test-Path -LiteralPath $live) -and (Get-Item -LiteralPath $live).LinkType) { throw '测试不会替换链接形式的 RimUnity；请指定独立测试安装。' }
$logs = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs'
if (Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination (Join-Path $run 'Logs-before') -Recurse }
$hadOriginal = Test-Path -LiteralPath $live
$movedOriginal = $false
$liveCreated = $false
$process = $null
try {
    if ($hadOriginal) { Move-Item -LiteralPath $live -Destination $backup; $movedOriginal = $true }
    $liveCreated = $true
    & (Join-Path $PSScriptRoot 'Copy-RuntimePackage.ps1') -Destination $live
    for ($i = 0; $i -lt 3; $i++) {
        $letter = @('A','B','C')[$i]
        $target = $testMods[$i]
        if ($letter -ne 'C') { Copy-Item -LiteralPath (Join-Path $FixtureRoot $letter) -Destination $target -Recurse }
        foreach ($folder in @('About','1.7/Assemblies','1.7/Defs')) { [IO.Directory]::CreateDirectory((Join-Path $target $folder)) | Out-Null }
        $id = $letter.ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $target 'About/About.xml'), "<ModMetaData><name>RimUnity Embedded Test $letter</name><author>RimUnity verification</author><packageId>test.$id</packageId><supportedVersions><li>1.7</li></supportedVersions><description>Temporary embedded runtime verification fixture.</description><modDependencies><li><packageId>brrainz.harmony</packageId><displayName>Harmony</displayName></li></modDependencies></ModMetaData>", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $target 'LoadFolders.xml'), '<loadFolders><v1.7><li>1.7</li></v1.7></loadFolders>', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $target '1.7/Defs/Marker.xml'), "<Defs><Fixture.MarkerDef><defName>EmbeddedTest_$id</defName></Fixture.MarkerDef></Defs>", [Text.UTF8Encoding]::new($false))
        Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination (Join-Path $target '1.7/Assemblies') -Force
    }
    foreach ($mode in @('standalone','embedded')) {
        $save = Join-Path $run $mode
        [IO.Directory]::CreateDirectory((Join-Path $save 'Config')) | Out-Null
        $standalone = if ($mode -eq 'standalone') { '<li>chezhou.rimunity</li>' } else { '' }
        [IO.File]::WriteAllText((Join-Path $save 'Config/ModsConfig.xml'), "<ModsConfigData><version>1.7.4980 rev60</version><activeMods><li>brrainz.harmony</li><li>test.a</li>$standalone<li>ludeon.rimworld</li><li>test.b</li><li>test.c</li></activeMods></ModsConfigData>", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $save 'Config/Prefs.xml'), '<PrefsData><screenWidth>1024</screenWidth><screenHeight>768</screenHeight><fullscreen>False</fullscreen><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><devMode>True</devMode></PrefsData>', [Text.UTF8Encoding]::new($false))
        $process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorld.exe') -WorkingDirectory $GameRoot -ArgumentList "-savedatafolder=$save", '-RimUnityEmbeddedTest' -WindowStyle Hidden -PassThru
        Write-Output "启动测试：$mode；PID=$($process.Id)；数据目录=$save"
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (!$process.HasExited -and $timer.Elapsed.TotalSeconds -lt 240) { Start-Sleep -Seconds 2; $process.Refresh() }
        if (!$process.HasExited) { throw "测试进程超时：$mode" }
        if (Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination (Join-Path $save 'Logs') -Recurse }
        $reportPath = Join-Path $save 'RimUnity-embedded-report.json'
        if (!(Test-Path -LiteralPath $reportPath)) { throw "游戏未生成测试报告：$save" }
        $report = Get-Content -LiteralPath $reportPath -Encoding UTF8 -Raw | ConvertFrom-Json
        if (!$report.passed -or $report.source -ne $mode -or $report.copies -ne 3) { throw "游戏测试未通过：$reportPath" }
        if ($mode -eq 'standalone' -and (Test-Path -LiteralPath (Join-Path $save 'RimUnity/Embedded'))) { throw '独立模式不应创建内置缓存。' }
        Write-Output "PASS 游戏启动：$mode；3 个内置测试模组；报告=$reportPath"
    }
} finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force }
    #只清理本次预先核对过、由本脚本创建的路径，并恢复原前置。
    foreach ($path in $testMods) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force } }
    if ($liveCreated -and (Test-Path -LiteralPath $live)) { Remove-Item -LiteralPath $live -Recurse -Force }
    if ($movedOriginal) { Move-Item -LiteralPath $backup -Destination $live }
    Write-Output "原模组已恢复，测试资料保留：$run"
}
