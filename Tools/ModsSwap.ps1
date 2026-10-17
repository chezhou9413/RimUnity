# `Mods/RimUnity` 的崩溃安全替换：把原模组移到同一卷上的状态目录，测试完再原子移回。
#
# 设计不变量（崩溃安全靠这三条，不靠 finally）：
#  1. 状态目录固定在 `<GameRoot>/.rimunity-ci`，与被替换目录同卷 → `Move-Item` 等价于
#     `Directory.Move` 的原子 rename；跨卷时直接拒绝（跨卷 Move 是复制+删除，不是原子的）。
#  2. 先落盘、再改文件系统：journal 用「只增不改」的世代文件（mods-swap-001.json、-002.json…），
#     写新世代是纯 rename，从不覆盖旧文件；读取时取序号最大且能解析的那个。
#     这样无论在哪一刻断电，最多丢最新一代，上一代仍然可读。
#  3. 任何时刻被强杀，`Repair-ModsSwap` 都能按 journal 把原模组还原，且可重复执行。
#
# 状态机：planned → backed-up（或 no-original）。测试方随后把 live 换成测试副本并打 marker；
# 修复时按 marker 区分「我们的测试副本」与「用户自己的 RimUnity」，后者一律不动。
# createdPaths 记录本次新建的测试模组目录，修复时一并删除——只删 journal 登记过、
# 位于 Mods 根目录下、且名字带约定前缀的路径。
param(
    [ValidateSet('Move', 'Repair', 'Status')][string]$Action = 'Status',
    [string]$GameRoot = ''
)
$ErrorActionPreference = 'Stop'

$script:SwapJournalPrefix = 'mods-swap-'
$script:SwapMarkerName = '.rimunity-ci-test-copy'
$script:SwapCreatedPrefix = 'RimUnity.EmbeddedVerification.'

function Get-ModsSwapPaths {
    param([Parameter(Mandatory)][string]$GameRoot)
    $game = [IO.Path]::GetFullPath($GameRoot)
    $mods = [IO.Path]::GetFullPath((Join-Path $game 'Mods'))
    $state = [IO.Path]::GetFullPath((Join-Path $game '.rimunity-ci'))
    return [pscustomobject]@{
        GameRoot = $game
        ModsRoot = $mods
        Live     = [IO.Path]::GetFullPath((Join-Path $mods 'RimUnity'))
        StateDir = $state
    }
}

function Test-ModsSwapSameVolume {
    param([Parameter(Mandatory)][string]$Left, [Parameter(Mandatory)][string]$Right)
    $leftRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Left))
    $rightRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Right))
    return [string]::Equals($leftRoot, $rightRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Get-ModsSwapJournalFiles {
    param([Parameter(Mandatory)]$Paths)
    if (-not (Test-Path -LiteralPath $Paths.StateDir -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $Paths.StateDir -File -Filter ($script:SwapJournalPrefix + '*.json') | Sort-Object Name)
}

function Get-ModsSwapJournalSequence {
    param([string]$Name)
    $m = [regex]::Match($Name, 'mods-swap-(\d+)\.json$')
    if (-not $m.Success) { return -1 }
    return [int]$m.Groups[1].Value
}

function Write-ModsSwapJournal {
    param(
        [Parameter(Mandatory)]$Paths,
        [Parameter(Mandatory)][ValidateSet('planned', 'backed-up', 'no-original')][string]$State,
        [string]$Backup = '',
        [string[]]$CreatedPaths = @()
    )
    [IO.Directory]::CreateDirectory($Paths.StateDir) | Out-Null
    $payload = [ordered]@{
        version      = 1
        state        = $State
        gameRoot     = $Paths.GameRoot
        live         = $Paths.Live
        backup       = $Backup
        marker       = $script:SwapMarkerName
        createdPaths = @($CreatedPaths)
        pid          = $PID
        startedUtc   = (Get-Date).ToUniversalTime().ToString('o')
    }
    $json = ConvertTo-Json -InputObject $payload -Depth 4
    $sequence = 0
    foreach ($file in (Get-ModsSwapJournalFiles $Paths)) {
        $value = Get-ModsSwapJournalSequence $file.Name
        if ($value -gt $sequence) { $sequence = $value }
    }
    $sequence++
    $name = '{0}{1:000}.json' -f $script:SwapJournalPrefix, $sequence
    $temp = Join-Path $Paths.StateDir ($name + '.tmp')
    $final = Join-Path $Paths.StateDir $name
    [IO.File]::WriteAllText($temp, $json + "`n", [Text.UTF8Encoding]::new($false))
    # 目标文件名是新世代，不覆盖旧文件 → 纯 rename，没有半截 journal 的窗口。
    Move-Item -LiteralPath $temp -Destination $final
    # 只保留最近两代：更早的删掉，避免状态目录堆积。
    foreach ($file in (Get-ModsSwapJournalFiles $Paths)) {
        if ((Get-ModsSwapJournalSequence $file.Name) -lt ($sequence - 1)) { Remove-Item -LiteralPath $file.FullName -Force }
    }
}

function Read-ModsSwapJournal {
    param([Parameter(Mandatory)]$Paths)
    $best = $null
    $bestSequence = -1
    foreach ($file in (Get-ModsSwapJournalFiles $Paths)) {
        $sequence = Get-ModsSwapJournalSequence $file.Name
        try { $journal = ([IO.File]::ReadAllText($file.FullName)) | ConvertFrom-Json } catch { continue }
        if ($sequence -gt $bestSequence) {
            $journal | Add-Member -NotePropertyName '__file' -NotePropertyValue $file.FullName -Force
            $best = $journal
            $bestSequence = $sequence
        }
    }
    return $best
}

function Remove-ModsSwapJournals {
    param([Parameter(Mandatory)]$Paths)
    if (-not (Test-Path -LiteralPath $Paths.StateDir -PathType Container)) { return }
    foreach ($file in @(Get-ChildItem -LiteralPath $Paths.StateDir -File -Filter ($script:SwapJournalPrefix + '*.json*'))) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
}

function Test-ModsSwapTestCopy {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Journal)
    $marker = Join-Path $Path $Journal.marker
    if (-not (Test-Path -LiteralPath $marker -PathType Leaf)) { return $false }
    return (([IO.File]::ReadAllText($marker)).Trim()) -eq [string]$Journal.pid
}

function Assert-ModsSwapGameNotRunning {
    $running = Get-Process -Name RimWorld, RimWorldWin64 -ErrorAction SilentlyContinue
    if ($running) { throw '游戏正在运行；交换 Mods/RimUnity 前必须先关闭游戏。' }
}

function Remove-ModsSwapPath {
    # 进程退出后杀毒/文件映射可能短暂占用 DLL；清理失败要重试，最终仍删不掉就抛，
    # 让 journal 留下、下一次运行继续修。这段重试沿用 c06c6cd 在游戏内测试里的实现。
    param([Parameter(Mandatory)][string]$Path)
    for ($attempt = 0; $attempt -lt 10; $attempt++) {
        try {
            if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Recurse -Force }
            return
        } catch [IO.IOException], [UnauthorizedAccessException] {
            if ($attempt -eq 9) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Invoke-ModsSwapMove {
    # 把原模组挪到状态目录并写 journal；返回 backup（没有原模组时为空串）。
    param([Parameter(Mandatory)][string]$GameRoot, [string[]]$CreatedPaths = @())
    $paths = Get-ModsSwapPaths $GameRoot
    Assert-ModsSwapGameNotRunning
    if (-not (Test-Path -LiteralPath $paths.ModsRoot -PathType Container)) { throw "Mods 目录不存在：$($paths.ModsRoot)" }
    $stale = Read-ModsSwapJournal $paths
    if ($stale) { throw ("发现未完成的上一次交换（state={0}，{1}）；请先运行 Repair。" -f $stale.state, $stale.__file) }
    if (-not (Test-Path -LiteralPath $paths.Live -PathType Container)) {
        Write-ModsSwapJournal -Paths $paths -State 'no-original' -CreatedPaths $CreatedPaths
        return [pscustomobject]@{ Paths = $paths; Backup = ''; HadOriginal = $false }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $paths.Live 'About/About.xml') -PathType Leaf)) {
        throw "RimUnity 目录不像有效模组（缺少 About/About.xml）：$($paths.Live)"
    }
    if ((Get-Item -LiteralPath $paths.Live).LinkType) { throw "不处理链接形式的 RimUnity：$($paths.Live)" }
    $backup = [IO.Path]::GetFullPath((Join-Path $paths.StateDir ('original-RimUnity-' + (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + $PID)))
    if (-not (Test-ModsSwapSameVolume -Left $paths.Live -Right $backup)) {
        throw ("状态目录与 Mods 不在同一卷，无法保证原子移动：{0} ↔ {1}" -f $paths.Live, $backup)
    }
    # 先记 planned 再动文件系统：崩溃点要么「还没移」，要么「移了但 journal 还是 planned」，
    # 两种情况 Repair 都能判定并处理。
    Write-ModsSwapJournal -Paths $paths -State 'planned' -Backup $backup -CreatedPaths $CreatedPaths
    Move-Item -LiteralPath $paths.Live -Destination $backup
    if ((Test-Path -LiteralPath $paths.Live) -or -not (Test-Path -LiteralPath $backup -PathType Container)) {
        throw "移动后状态校验失败：live=$($paths.Live) backup=$backup"
    }
    Write-ModsSwapJournal -Paths $paths -State 'backed-up' -Backup $backup -CreatedPaths $CreatedPaths
    return [pscustomobject]@{ Paths = $paths; Backup = $backup; HadOriginal = $true }
}

function New-ModsSwapTestCopyMarker {
    # 在测试副本里放 marker：修复时靠它区分「我们的测试副本」和「用户自己的 RimUnity」。
    param([Parameter(Mandatory)]$Swap)
    $journal = Read-ModsSwapJournal $Swap.Paths
    $marker = Join-Path $Swap.Paths.Live $journal.marker
    [IO.File]::WriteAllText($marker, [string]$journal.pid + "`n", [Text.UTF8Encoding]::new($false))
}

function Repair-ModsSwap {
    # 按 journal 还原到原始布局；可重复执行。返回本次实际做了什么。
    param([Parameter(Mandatory)][string]$GameRoot)
    $paths = Get-ModsSwapPaths $GameRoot
    $journal = Read-ModsSwapJournal $paths
    $result = [ordered]@{
        JournalFound     = $false
        JournalState     = ''
        RemovedTestCopy  = $false
        RestoredOriginal = $false
        RemovedCreated   = 0
        Cleared          = $false
    }
    if (-not $journal) { return [pscustomobject]$result }
    $result.JournalFound = $true
    $result.JournalState = [string]$journal.state
    Assert-ModsSwapGameNotRunning

    # 1) 删除本次登记过的测试模组目录（前缀 + 落在 Mods 根目录下，双重校验）。
    foreach ($created in @($journal.createdPaths)) {
        if (-not $created) { continue }
        $full = [IO.Path]::GetFullPath([string]$created)
        if ([IO.Path]::GetDirectoryName($full) -ne $paths.ModsRoot) { continue }
        if ([IO.Path]::GetFileName($full) -notlike "$script:SwapCreatedPrefix*") { continue }
        if (Test-Path -LiteralPath $full) {
            Remove-ModsSwapPath -Path $full
            $result.RemovedCreated = [int]$result.RemovedCreated + 1
        }
    }

    $live = [IO.Path]::GetFullPath([string]$journal.live)
    $backup = if ($journal.backup) { [IO.Path]::GetFullPath([string]$journal.backup) } else { '' }
    $hasLive = Test-Path -LiteralPath $live -PathType Container
    $hasBackup = $backup -and (Test-Path -LiteralPath $backup -PathType Container)
    $isTestCopy = $hasLive -and (Test-ModsSwapTestCopy -Path $live -Journal $journal)

    if ($hasBackup) {
        if ($hasLive -and -not $isTestCopy) {
            throw ("状态目录里既有备份、live 又是别的副本，无法自动判断：live={0} backup={1}。请人工确认后删除状态目录里的 journal。" -f $live, $backup)
        }
        if ($hasLive -and $isTestCopy) {
            Remove-ModsSwapPath -Path $live
            $result.RemovedTestCopy = $true
        }
        Move-Item -LiteralPath $backup -Destination $live
        if (-not (Test-Path -LiteralPath $live -PathType Container)) { throw "还原后 live 不存在：$live" }
        $result.RestoredOriginal = $true
    } elseif ($hasLive -and $isTestCopy) {
        # 没有备份却只剩我们的测试副本：原模组已丢失，删掉测试副本并明确报错。
        Remove-ModsSwapPath -Path $live
        $result.RemovedTestCopy = $true
        throw ("原模组的备份缺失，无法还原；已删除测试副本。请重新安装 RimUnity。journal={0}" -f $journal.__file)
    } elseif ($journal.state -ne 'planned' -and $journal.state -ne 'no-original' -and -not $hasLive) {
        throw ("原模组备份缺失且 live 不存在，无法自动恢复；journal 保留在 {0} 供排查。" -f $journal.__file)
    }

    Remove-ModsSwapJournals $paths
    $result.Cleared = $true
    return [pscustomobject]$result
}

# ---- 命令行入口（人工排查与自测脚本用）----
if ($GameRoot) {
    $paths = Get-ModsSwapPaths $GameRoot
    switch ($Action) {
        'Move'   { $r = Invoke-ModsSwapMove -GameRoot $GameRoot; Write-Output ("已移走：{0}" -f $r.Backup) }
        'Repair' { $r = Repair-ModsSwap -GameRoot $GameRoot; Write-Output ("修复：状态={0} 还原={1} 删测试副本={2} 删测试模组={3}" -f $r.JournalState, $r.RestoredOriginal, $r.RemovedTestCopy, $r.RemovedCreated) }
        'Status' {
            $j = Read-ModsSwapJournal $paths
            if (-not $j) { Write-Output '没有未完成的交换。' }
            else { Write-Output ("未完成：state={0} live={1} backup={2} journal={3}" -f $j.state, $j.live, $j.backup, $j.__file) }
        }
    }
}
