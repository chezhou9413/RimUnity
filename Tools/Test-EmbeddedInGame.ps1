param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$FixtureRoot, [string]$LegacyCarrier = '', [string]$WorkshopRoot = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ModsSwap.ps1')
if ($WorkshopRoot -and !(Test-Path -LiteralPath (Join-Path $WorkshopRoot '1.7/Assemblies/RimUnity.dll'))) { throw '工坊运行时目录无效。' }
if ($LegacyCarrier -and !(Test-Path -LiteralPath $LegacyCarrier -PathType Leaf)) { throw '历史加载器文件不存在。' }
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw '游戏正在运行；请结束当前会话后再执行测试。' }

$repo = Split-Path $PSScriptRoot -Parent
# 交换 Mods/RimUnity 的崩溃安全由 ModsSwap 的 journal 保证：即使本进程被强杀，
# 下一次运行也会在这里按 journal 先把原模组还原，再开始新的一次。
$stale = Repair-ModsSwap -GameRoot $GameRoot
if ($stale.JournalFound) {
    Write-Output ("检测到上次未完成的交换（state={0}），已自动还原：原模组={1} 删测试副本={2} 删测试模组={3}" -f $stale.JournalState, $stale.RestoredOriginal, $stale.RemovedTestCopy, $stale.RemovedCreated)
}

$run = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/GameVerification-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$paths = Get-ModsSwapPaths $GameRoot
$modsRoot = $paths.ModsRoot
$live = $paths.Live
$testMods = @('A','B','C','Ordinary') | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $modsRoot "RimUnity.EmbeddedVerification.$_")) }
#所有移动、删除目标都先核对绝对路径；原模组备份由 ModsSwap 移到游戏根目录下的状态目录。
foreach ($path in @($live) + $testMods) {
    if ([IO.Path]::GetDirectoryName($path) -ne $modsRoot) { throw "测试路径越界：$path" }
}
foreach ($path in $testMods) { if (Test-Path -LiteralPath $path) { throw "已有测试目录，未覆盖：$path" } }
if ((Test-Path -LiteralPath $live) -and (Get-Item -LiteralPath $live).LinkType) { throw '测试不会替换链接形式的 RimUnity；请指定独立测试安装。' }

$logs = Join-Path $env:USERPROFILE 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs'
if (Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination (Join-Path $run 'Logs-before') -Recurse }
$process = $null
try {
    # 移走原模组（同卷原子 rename，并先落 journal）；测试模组目录一并登记，修复时自动清理。
    Invoke-ModsSwapMove -GameRoot $GameRoot -CreatedPaths $testMods | Out-Null
    & (Join-Path $PSScriptRoot 'Copy-RuntimePackage.ps1') -Destination $live
    # 在测试副本里打 marker：崩溃后靠它区分「我们的测试副本」与「用户自己的 RimUnity」。
    New-ModsSwapTestCopyMarker ([pscustomobject]@{ Paths = $paths })
    for ($i = 0; $i -lt 3; $i++) {
        $letter = @('A','B','C')[$i]
        $target = $testMods[$i]
        if ($letter -ne 'C') { Copy-Item -LiteralPath (Join-Path $FixtureRoot $letter) -Destination $target -Recurse }
        foreach ($folder in @('About','1.7/Assemblies','1.7/Defs')) { [IO.Directory]::CreateDirectory((Join-Path $target $folder)) | Out-Null }
        $id = $letter.ToLowerInvariant()
        [IO.File]::WriteAllText((Join-Path $target 'About/About.xml'), "<ModMetaData><name>RimUnity Embedded Test $letter</name><author>RimUnity verification</author><packageId>test.$id</packageId><supportedVersions><li>1.7</li></supportedVersions><description>Temporary embedded runtime verification fixture.</description><modDependencies><li><packageId>brrainz.harmony</packageId><displayName>Harmony</displayName></li></modDependencies></ModMetaData>", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $target 'LoadFolders.xml'), '<loadFolders><v1.7><li>1.7</li></v1.7></loadFolders>', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $target '1.7/Defs/Marker.xml'), "<Defs><Fixture.MarkerDef><defName>EmbeddedTest_$id</defName></Fixture.MarkerDef></Defs>", [Text.UTF8Encoding]::new($false))
        $carrier = if ($letter -eq 'A' -and $LegacyCarrier) { $LegacyCarrier } else { Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll' }
        Copy-Item -LiteralPath $carrier -Destination (Join-Path $target '1.7/Assemblies/RimUnity.Embedded.dll') -Force
    }
    $ordinary = $testMods[3]
    Copy-Item -LiteralPath (Join-Path $FixtureRoot 'Ordinary') -Destination $ordinary -Recurse
    [IO.Directory]::CreateDirectory((Join-Path $ordinary 'About')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $ordinary 'About/About.xml'), '<ModMetaData><name>RimUnity Ordinary DLL Test</name><author>RimUnity verification</author><packageId>test.ordinary</packageId><supportedVersions><li>1.7</li></supportedVersions><description>Ordinary DLL compatibility fixture.</description><modDependencies><li><packageId>Chezhou.RimUnity</packageId><displayName>RimUnity</displayName></li></modDependencies><loadAfter><li>Chezhou.RimUnity</li></loadAfter></ModMetaData>', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $ordinary 'LoadFolders.xml'), '<loadFolders><v1.7><li>1.7</li></v1.7></loadFolders>', [Text.UTF8Encoding]::new($false))
    $modes = @('standalone','embedded')
    if ($WorkshopRoot) { $modes += 'workshop' }
    foreach ($mode in $modes) {
        $save = Join-Path $run $mode
        [IO.Directory]::CreateDirectory((Join-Path $save 'Config')) | Out-Null
        $standalone = if ($mode -eq 'standalone') { '<li>chezhou.rimunity</li><li>test.ordinary</li>' } elseif ($mode -eq 'workshop') { '<li>chezhou.rimunity_steam</li><li>test.ordinary</li>' } else { '' }
        [IO.File]::WriteAllText((Join-Path $save 'Config/ModsConfig.xml'), "<ModsConfigData><version>1.7.4980 rev60</version><activeMods><li>brrainz.harmony</li><li>test.a</li>$standalone<li>ludeon.rimworld</li><li>test.b</li><li>test.c</li></activeMods></ModsConfigData>", [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $save 'Config/Prefs.xml'), '<PrefsData><screenWidth>1024</screenWidth><screenHeight>768</screenHeight><fullscreen>False</fullscreen><volumeMaster>0</volumeMaster><langFolderName>English</langFolderName><devMode>True</devMode></PrefsData>', [Text.UTF8Encoding]::new($false))
        $process = Start-Process -FilePath (Join-Path $GameRoot 'RimWorld.exe') -WorkingDirectory $GameRoot -ArgumentList "-savedatafolder=$save", '-RimUnityEmbeddedTest' -WindowStyle Hidden -PassThru
        Write-Output "启动测试：$mode；PID=$($process.Id)；数据目录=$save"
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while (!$process.HasExited -and $timer.Elapsed.TotalSeconds -lt 240) { Start-Sleep -Seconds 2; $process.Refresh() }
        if (!$process.HasExited) { throw "测试进程超时：$mode" }
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "游戏测试异常退出：$mode；exit=$($process.ExitCode)；目录=$save" }
        if (Test-Path -LiteralPath $logs) { Copy-Item -LiteralPath $logs -Destination (Join-Path $save 'Logs') -Recurse }
        $reportPath = Join-Path $save 'RimUnity-embedded-report.json'
        if (!(Test-Path -LiteralPath $reportPath)) { throw "游戏未生成测试报告：$save" }
        $report = Get-Content -LiteralPath $reportPath -Encoding UTF8 -Raw | ConvertFrom-Json
        $expectedSource = if ($mode -eq 'embedded') { 'embedded' } else { 'standalone' }
        if (!$report.passed -or $report.source -ne $expectedSource -or $report.copies -ne 3) { throw "游戏测试未通过：$reportPath" }
        $hasCache = Test-Path -LiteralPath (Join-Path $save 'RimUnity/Embedded')
        if ($hasCache -ne ($mode -eq 'embedded')) { throw '独立版不应释放内置载荷；内置版应创建资源缓存。' }
        if ($mode -ne 'embedded' -and !$report.ordinaryDll) { throw '普通 DLL 未通过真实游戏兼容验证。' }
        if ($mode -eq 'workshop') {
            if ($report.runtimeOwner -ne 'chezhou.rimunity_steam' -or
                [IO.Path]::GetFullPath($report.resourceRoot) -ne [IO.Path]::GetFullPath((Join-Path $WorkshopRoot '1.7'))) {
                throw "没有使用指定的工坊副本：$reportPath"
            }
        }
        Write-Output "PASS 游戏启动：$mode；3 个内置测试模组；报告=$reportPath"
    }
} finally {
    # 先停掉本脚本启动的游戏进程，再按 journal 还原：测试副本、登记的测试模组目录、原模组一起处理。
    # 即使这里再次被强杀，journal 仍在，下次运行开头会自动修复。
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force }
    $repaired = Repair-ModsSwap -GameRoot $GameRoot
    Write-Output ("已还原：原模组={0} 删测试副本={1} 删测试模组={2}；测试资料保留：{3}" -f $repaired.RestoredOriginal, $repaired.RemovedTestCopy, $repaired.RemovedCreated, $run)
}
