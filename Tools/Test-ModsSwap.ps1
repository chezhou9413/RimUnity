# ModsSwap 的崩溃窗口自测：不启动游戏、不碰真实安装，全部在临时目录里跑。
#
# 覆盖的窗口（对应「进程被强杀」时 journal 可能停在哪一步）：
#   1 正常：移走 → 放测试副本（带 marker）→ 修复，原模组回到原位。
#   2 只写了 planned、还没移动：修复只清 journal，不动任何文件。
#   3 已移动、journal 还是 planned（崩在两次写之间）：修复靠 backup 还原。
#   4 已移动、journal 是 backed-up：修复还原。
#   5 live 被换成非测试副本（没有 marker）：禁止自动删，保留现场并报错。
#   6 backup 丢失且 live 不在：明确报「无法还原」，保留 journal 供排查。
#   7 修复可重复执行：第二次是 no-op。
#   8 登记过的测试模组目录会被删，未登记的同名前缀目录不会被动。
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'ModsSwap.ps1')

$script:Failures = 0
$script:Root = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/ModsSwap-' + [Guid]::NewGuid().ToString('N'))

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw "断言失败：$message" }
}
function Read-Text([string]$path) { if (-not (Test-Path -LiteralPath $path)) { return $null }; return [IO.File]::ReadAllText($path) }
function New-Fixture([string]$name) {
    # 造一个最小「游戏根目录」：只有 Mods/RimUnity，内容用文件名区分来源。
    $game = [IO.Path]::GetFullPath((Join-Path $script:Root $name))
    $about = Join-Path $game 'Mods/RimUnity/About'
    [IO.Directory]::CreateDirectory($about) | Out-Null
    [IO.File]::WriteAllText((Join-Path $about 'About.xml'), '<ModMetaData><packageId>Chezhou.RimUnity</packageId></ModMetaData>', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $game 'Mods/RimUnity/payload.txt'), "original:$name", [Text.UTF8Encoding]::new($false))
    return $game
}
function Install-TestCopy($Swap) {
    # 模拟 Test-EmbeddedInGame 用 Copy-RuntimePackage 填 live，再打 marker。
    [IO.Directory]::CreateDirectory((Join-Path $Swap.Paths.Live 'About')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $Swap.Paths.Live 'payload.txt'), 'test-copy', [Text.UTF8Encoding]::new($false))
    New-ModsSwapTestCopyMarker $Swap
}
function Invoke-Case([string]$name, [scriptblock]$body) {
    try {
        & $body
        Write-Host "PASS  $name"
    } catch {
        $script:Failures++
        Write-Host "FAIL  $name -> $($_.Exception.Message)"
        Write-Host ($_.ScriptStackTrace)
    }
}

try {
    [IO.Directory]::CreateDirectory($script:Root) | Out-Null

    Invoke-Case '1 正常往返：移走 → 测试副本 → 修复' {
        $game = New-Fixture 'normal'
        $swap = Invoke-ModsSwapMove -GameRoot $game
        Assert-True (Test-Path -LiteralPath $swap.Backup -PathType Container) '备份应存在'
        Assert-True (-not (Test-Path -LiteralPath $swap.Paths.Live)) '移走后 live 应不存在'
        Install-TestCopy $swap
        $r = Repair-ModsSwap -GameRoot $game
        Assert-True $r.JournalFound '应发现 journal'
        Assert-True $r.RestoredOriginal '应还原原模组'
        Assert-True $r.RemovedTestCopy '应删掉测试副本'
        Assert-True $r.Cleared '应清除 journal'
        Assert-True ((Read-Text (Join-Path $swap.Paths.Live 'payload.txt')) -eq 'original:normal') '还原内容应一致'
        Assert-True (-not (Test-Path -LiteralPath $swap.Backup)) '备份应已移回'
        Assert-True (@(Get-ModsSwapJournalFiles $swap.Paths).Count -eq 0) 'journal 应已删除'
    }

    Invoke-Case '2 只写了 planned、还没移动：修复不动文件' {
        $game = New-Fixture 'planned-only'
        $paths = Get-ModsSwapPaths $game
        Write-ModsSwapJournal -Paths $paths -State 'planned'
        $r = Repair-ModsSwap -GameRoot $game
        Assert-True $r.Cleared '应清除 journal'
        Assert-True ((Read-Text (Join-Path $paths.Live 'payload.txt')) -eq 'original:planned-only') 'live 不应被动'
    }

    Invoke-Case '3 已移动但 journal 停在 planned：修复靠 backup 还原' {
        $game = New-Fixture 'crash-between'
        $swap = Invoke-ModsSwapMove -GameRoot $game
        Write-ModsSwapJournal -Paths $swap.Paths -State 'planned' -Backup $swap.Backup
        $r = Repair-ModsSwap -GameRoot $game
        Assert-True $r.RestoredOriginal '应靠 backup 还原'
        Assert-True ((Read-Text (Join-Path $swap.Paths.Live 'payload.txt')) -eq 'original:crash-between') '内容应一致'
    }

    Invoke-Case '4 已移动、journal 是 backed-up：修复还原' {
        $game = New-Fixture 'backed-up'
        $swap = Invoke-ModsSwapMove -GameRoot $game
        $r = Repair-ModsSwap -GameRoot $game
        Assert-True $r.RestoredOriginal '应还原'
        Assert-True ((Read-Text (Join-Path $swap.Paths.Live 'payload.txt')) -eq 'original:backed-up') '内容应一致'
    }

    Invoke-Case '5 live 是非测试副本：拒绝自动删除' {
        $game = New-Fixture 'ambiguous'
        $swap = Invoke-ModsSwapMove -GameRoot $game
        [IO.Directory]::CreateDirectory((Join-Path $swap.Paths.Live 'About')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $swap.Paths.Live 'payload.txt'), 'someone-else', [Text.UTF8Encoding]::new($false))
        $threw = $false
        try { Repair-ModsSwap -GameRoot $game | Out-Null } catch { $threw = $true }
        Assert-True $threw '应拒绝处理'
        Assert-True ((Read-Text (Join-Path $swap.Paths.Live 'payload.txt')) -eq 'someone-else') '不能动别人的副本'
        Assert-True (Test-Path -LiteralPath $swap.Backup -PathType Container) '备份应保留'
        Assert-True (@(Get-ModsSwapJournalFiles $swap.Paths).Count -gt 0) 'journal 应保留供排查'
    }

    Invoke-Case '6 backup 丢失且 live 不存在：明确报无法还原' {
        $game = New-Fixture 'lost-backup'
        $swap = Invoke-ModsSwapMove -GameRoot $game
        Remove-Item -LiteralPath $swap.Backup -Recurse -Force
        $threw = $false
        try { Repair-ModsSwap -GameRoot $game | Out-Null } catch { $threw = $true }
        Assert-True $threw '应报错'
        Assert-True (@(Get-ModsSwapJournalFiles $swap.Paths).Count -gt 0) 'journal 应保留'
    }

    Invoke-Case '7 修复可重复执行' {
        $game = New-Fixture 'idempotent'
        $swap = Invoke-ModsSwapMove -GameRoot $game
        $first = Repair-ModsSwap -GameRoot $game
        $second = Repair-ModsSwap -GameRoot $game
        Assert-True $first.RestoredOriginal '第一次应还原'
        Assert-True (-not $second.JournalFound) '第二次应为 no-op'
    }

    Invoke-Case '8 只清理登记过的测试模组目录' {
        $game = New-Fixture 'created-paths'
        $mods = (Get-ModsSwapPaths $game).ModsRoot
        $registered = Join-Path $mods 'RimUnity.EmbeddedVerification.A'
        $decoy = Join-Path $mods 'RimUnity.EmbeddedVerification.SomeoneElse'
        $swap = Invoke-ModsSwapMove -GameRoot $game -CreatedPaths @($registered)
        [IO.Directory]::CreateDirectory($registered) | Out-Null
        [IO.Directory]::CreateDirectory($decoy) | Out-Null
        Repair-ModsSwap -GameRoot $game | Out-Null
        Assert-True (-not (Test-Path -LiteralPath $registered)) '登记过的目录应被删'
        Assert-True (Test-Path -LiteralPath $decoy) '未登记的目录不能动'
    }

    Invoke-Case '9 状态目录与 live 同卷（原子移动的前提）' {
        $game = New-Fixture 'same-volume'
        $paths = Get-ModsSwapPaths $game
        Assert-True (Test-ModsSwapSameVolume -Left $paths.Live -Right $paths.StateDir) '状态目录应在同一卷'
        Assert-True ([IO.Path]::GetFullPath($paths.StateDir).StartsWith([IO.Path]::GetFullPath($game))) '状态目录应在游戏根目录下'
    }
} finally {
    if (Test-Path -LiteralPath $script:Root) { Remove-Item -LiteralPath $script:Root -Recurse -Force }
}

if ($script:Failures -gt 0) { throw "ModsSwap 自测失败：$($script:Failures) 个用例" }
Write-Host 'ModsSwap 自测全部通过（9 个窗口）'
