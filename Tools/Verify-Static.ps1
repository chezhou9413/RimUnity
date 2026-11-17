# 静态与一致性检查：仓库 CI（.github/workflows/repo-checks.yml）与本地共用。
#
# 只做“不需要游戏安装、不需要编译”就能发现的仓库级错误。全部规则都来自本仓库真实踩过的坑：
#   1.  docs-mirror      UnityPackage/Documentation~ 的离线文档镜像与 Sources 索引没跟着源文件更新
#   2.  release-version  发布号漂移（About/About.xml、Source/Directory.Build.props、UnityPackage/package.json）
#   3.  download-links   README 与 EmbeddedRuntime.md 里的下载链接还指着旧的版本 tag
#   4.  compat-floor     内置运行时的兼容下限（targets / DeferredContent）与发布号不一致或高于它
#   5.  version-literal  版本号又出现在别处的字面量里（应统一走 RuntimeInfo.Version）
#   6.  ps1-bom          含非 ASCII 的 .ps1 缺 UTF-8 BOM：Windows PowerShell 5.1 在非 UTF-8 代码页下会乱码甚至解析失败
#   7.  absolute-path    被跟踪文件里的盘符绝对路径（在别人机器上必然不存在）
#   8.  parse-config     被跟踪的 XML / JSON 能被解析
#   9.  conflict-markers 冲突标记残留
#  10.  diff-check       （仅在给出 -BaseRevision 时）提交范围内的冲突标记与空白错误
#
# 既有缺口写在下面的 $KnownDebt 允许清单里：只提示、不让检查变红；修完请删掉对应条目。
#
# 用法：
#   pwsh -File Tools/Verify-Static.ps1                       # 本地全量
#   pwsh -File Tools/Verify-Static.ps1 -BaseRevision <sha>   # CI：额外检查提交范围
#   pwsh -File Tools/Verify-Static.ps1 -Strict               # 把已知缺口也当失败（清单销项用）
param(
    [string]$BaseRevision = '',
    [switch]$Strict
)

[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$repo = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $repo

$passes   = New-Object System.Collections.ArrayList
$failures = New-Object System.Collections.ArrayList
$known    = New-Object System.Collections.ArrayList

function Add-Pass([string]$rule, [string]$message) { [void]$passes.Add(('[{0}] {1}' -f $rule, $message)) }
function Add-Fail([string]$rule, [string]$message) { [void]$failures.Add(('[{0}] {1}' -f $rule, $message)) }
function Add-Known([string]$rule, [string]$message) { [void]$known.Add(('[{0}] {1}' -f $rule, $message)) }

function Get-RepoText([string]$path) { return [IO.File]::ReadAllText((Join-Path $repo $path)) }
function Get-RepoLines([string]$path) { return [IO.File]::ReadAllLines((Join-Path $repo $path)) }

function Get-TrackedFiles([string[]]$patterns) {
    # quotePath=false：否则含非 ASCII 的路径会被 git 转义加引号输出，路径直接不可用
    $out = @(& git -c core.quotePath=false ls-files -- $patterns)
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files 失败：请在仓库工作区内运行本脚本。' }
    return @($out | Where-Object { $_ -and $_.Trim() -ne '' })
}

# ---------------------------------------------------------------------------
# 已知缺口允许清单
# 每条 = 规则 + 文件 + 原因。命中时只提示（-Strict 下仍算失败），修好后请删除条目，
# 被删除的条目对应的规则立刻变成硬失败。
# ---------------------------------------------------------------------------
$KnownDebt = @(
    # 当前没有已登记的缺口。新增条目格式：
    #   @{ Rule = 'ps1-bom'; Path = 'Tools/X.ps1'; Reason = '为什么先记账' }
)
$debtHit = @{}

function Test-KnownDebt([string]$rule, [string]$path) {
    foreach ($entry in $KnownDebt) {
        if ($entry.Rule -eq $rule -and $entry.Path -eq $path) {
            $debtHit[('{0}|{1}' -f $rule, $path)] = $true
            return $entry.Reason
        }
    }
    return $null
}

function Report-Violation([string]$rule, [string]$path, [string]$message) {
    $reason = Test-KnownDebt $rule $path
    if ($reason -and -not $Strict) { Add-Known $rule ('{0}（{1}；{2}）' -f $message, $path, $reason) }
    else { Add-Fail $rule ('{0}（{1}）' -f $message, $path) }
}

# 注意：变量名不能叫 $psEdition——$PSEdition 是 PowerShell 的只读自动变量
$psFlavor = $PSVersionTable.PSEdition
$psVersion = $PSVersionTable.PSVersion
Write-Host ('静态与一致性检查：{0} / PowerShell {1} / {2}' -f $psFlavor, $psVersion, $repo)

# ---------------------------------------------------------------------------
# 1. 离线文档镜像
# ---------------------------------------------------------------------------
$syncScript = Join-Path $PSScriptRoot 'Sync-Manual.ps1'
$syncBytes = [IO.File]::ReadAllBytes($syncScript)
$syncHasBom = ($syncBytes.Length -ge 3 -and $syncBytes[0] -eq 0xEF -and $syncBytes[1] -eq 0xBB -and $syncBytes[2] -eq 0xBF)
# PowerShell 7 默认按 UTF-8 读脚本；5.1 按 ANSI 代码页读，缺 BOM 的 UTF-8 中文会乱码
$decodesUtf8WithoutBom = ($psFlavor -eq 'Core') -or ([Text.Encoding]::Default.CodePage -eq 65001)
if ($syncHasBom -or $decodesUtf8WithoutBom) {
    try {
        & $syncScript -Check | Out-Null
        Add-Pass 'docs-mirror' '离线文档镜像与 sources.json 同步'
    } catch {
        $message = $_.Exception.Message
        $mismatch = [regex]::Match($message, '离线文档未同步：(.+?)\s*$')
        if ($mismatch.Success) {
            # 行尾自动转换（core.autocrlf）会让同一个 blob 的两个工作区副本字节不同。
            # 这里补一次 git 层面的比对，避免把“行尾差异”误读成“镜像内容没同步”。
            $file = $mismatch.Groups[1].Value
            $mirror = 'UnityPackage/Documentation~/Sources/' + $file
            try {
                $sourceBlob = (& git hash-object ('--path={0}' -f $file) $file)
                $mirrorBlob = (& git hash-object ('--path={0}' -f $mirror) $mirror)
                if ($sourceBlob -and $sourceBlob -eq $mirrorBlob) {
                    $message = '{0}；两文件在 git 层面内容相同（blob {1}），差异只来自工作区行尾转换——本地用 git -c core.autocrlf=false checkout -- 重取，CI 里请关掉 core.autocrlf' -f $message, $sourceBlob.Substring(0, 8)
                }
            } catch { }
        }
        Add-Fail 'docs-mirror' ('{0}；修复：pwsh Tools/Sync-Manual.ps1 后提交镜像' -f $message)
    }
} else {
    Add-Known 'docs-mirror' ('本宿主按 ANSI（代码页 {0}）读脚本，缺 BOM 的 Sync-Manual.ps1 无法可靠执行，跳过镜像检查（清单 P0-①）' -f [Text.Encoding]::Default.CodePage)
}

# ---------------------------------------------------------------------------
# 2. 发布号一致（About.xml / Directory.Build.props / package.json）
# ---------------------------------------------------------------------------
$releaseVersion = ''
$aboutVersion   = [regex]::Match((Get-RepoText 'About/About.xml'), '<modVersion>\s*([^<\s]+)\s*</modVersion>').Groups[1].Value
$packageVersion = [regex]::Match((Get-RepoText 'UnityPackage/package.json'), '"version"\s*:\s*"([^"]+)"').Groups[1].Value
$propsText      = Get-RepoText 'Source/Directory.Build.props'
$propsVersion   = [regex]::Match($propsText, '<Version>\s*([^<\s]+)\s*</Version>').Groups[1].Value
$fileVersion    = [regex]::Match($propsText, '<FileVersion>\s*([^<\s]+)\s*</FileVersion>').Groups[1].Value

if (-not ($aboutVersion -and $packageVersion -and $propsVersion -and $fileVersion)) {
    Add-Fail 'release-version' '没能从 About/About.xml、UnityPackage/package.json、Source/Directory.Build.props 里读全发布号'
} elseif ($aboutVersion -ne $propsVersion -or $aboutVersion -ne $packageVersion) {
    Add-Fail 'release-version' ('发布号不一致：About.xml={0}、Directory.Build.props:Version={1}、package.json={2}' -f $aboutVersion, $propsVersion, $packageVersion)
} elseif ($fileVersion -ne ($propsVersion + '.0')) {
    Add-Fail 'release-version' ('FileVersion 与发布号不匹配：Version={0}、FileVersion={1}（约定为 <Version>.0；AssemblyVersion 固定 1.0.0.0 不参与）' -f $propsVersion, $fileVersion)
} else {
    $releaseVersion = $aboutVersion
    Add-Pass 'release-version' ('发布号一致：{0}（About.xml / Directory.Build.props / package.json），FileVersion={1}' -f $releaseVersion, $fileVersion)
}

# ---------------------------------------------------------------------------
# 3. 下载链接指向当前版本
# ---------------------------------------------------------------------------
if ($releaseVersion) {
    $linkFiles = @('README.md', 'README.en.md', 'Docs/zh-CN/EmbeddedRuntime.md', 'Docs/en/EmbeddedRuntime.md')
    $badLinks = New-Object System.Collections.ArrayList
    foreach ($file in $linkFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $repo $file))) { continue }
        foreach ($m in [regex]::Matches((Get-RepoText $file), 'releases/download/v([0-9][^/\s\)\]]*)')) {
            if ($m.Groups[1].Value -ne $releaseVersion) { [void]$badLinks.Add(('{0} → v{1}' -f $file, $m.Groups[1].Value)) }
        }
    }
    if ($badLinks.Count) { Add-Fail 'download-links' ('下载链接不是 v{0}：{1}' -f $releaseVersion, ($badLinks -join '、')) }
    else { Add-Pass 'download-links' ('README 与 EmbeddedRuntime.md 的下载链接都指向 v{0}' -f $releaseVersion) }
}

# ---------------------------------------------------------------------------
# 4. 内置运行时的兼容下限
# ---------------------------------------------------------------------------
if ($releaseVersion) {
    $targetsFloor = [regex]::Match((Get-RepoText 'Tools/RimUnity.Embedded.targets'), 'RimUnityMinimumVersion[^>]*>\s*([^<\s]+)\s*<').Groups[1].Value
    $floorMatch = [regex]::Match((Get-RepoText 'Source/RimUnity.Embedded/DeferredContent.cs'), 'Minimum\s*=\s*new\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)')
    $deferredFloor = ''
    if ($floorMatch.Success) { $deferredFloor = ('{0}.{1}.{2}' -f $floorMatch.Groups[1].Value, $floorMatch.Groups[2].Value, $floorMatch.Groups[3].Value) }
    if (-not ($targetsFloor -and $deferredFloor)) {
        Add-Fail 'compat-floor' '没能读到兼容下限（Tools/RimUnity.Embedded.targets 的 RimUnityMinimumVersion / DeferredContent.cs 的 Minimum）'
    } elseif ([Version]$targetsFloor -ne [Version]$deferredFloor) {
        Add-Fail 'compat-floor' ('兼容下限两处不一致：targets={0}、DeferredContent.cs={1}' -f $targetsFloor, $deferredFloor)
    } elseif ([Version]$targetsFloor -gt [Version]$releaseVersion) {
        Add-Fail 'compat-floor' ('兼容下限 {0} 高于发布号 {1}：同版本内置载荷会被判成不兼容' -f $targetsFloor, $releaseVersion)
    } else {
        Add-Pass 'compat-floor' ('兼容下限两处一致且不高于发布号：{0}' -f $targetsFloor)
    }
}

# ---------------------------------------------------------------------------
# 5. 版本字面量（应统一走 RuntimeInfo.Version）
# ---------------------------------------------------------------------------
$literalFiles = Get-TrackedFiles @('Source/RimUnity/Settings/*.cs')
$literalViolations = 0
foreach ($file in $literalFiles) {
    $hits = New-Object System.Collections.ArrayList
    $lines = Get-RepoLines $file
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '"[^"]*\d+\.\d+\.\d+[^"]*"') { [void]$hits.Add(($i + 1)) }
    }
    if ($hits.Count -eq 0) { continue }
    $literalViolations++
    Report-Violation 'version-literal' $file ('{0}:{1} 出现版本字面量' -f $file, ($hits -join ','))
}
if ($literalViolations -eq 0) { Add-Pass 'version-literal' '界面代码里没有版本字面量' }

# ---------------------------------------------------------------------------
# 6. 含非 ASCII 的 .ps1 必须有 UTF-8 BOM
# ---------------------------------------------------------------------------
$ps1Files = Get-TrackedFiles @('*.ps1')
$bomViolations = 0
$bomChecked = 0
foreach ($file in $ps1Files) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $repo $file))
    $hasNonAscii = $false
    foreach ($b in $bytes) { if ($b -ge 0x80) { $hasNonAscii = $true; break } }
    if (-not $hasNonAscii) { continue }
    $bomChecked++
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ($hasBom) { continue }
    $bomViolations++
    Report-Violation 'ps1-bom' $file '含非 ASCII 却没有 UTF-8 BOM'
}
if ($bomViolations -eq 0) { Add-Pass 'ps1-bom' ('{0} 个含非 ASCII 的 .ps1 都带 UTF-8 BOM' -f $bomChecked) }

# ---------------------------------------------------------------------------
# 7. 被跟踪文件里的盘符绝对路径
# ---------------------------------------------------------------------------
$scanExtensions = @('.cs', '.ps1', '.psm1', '.sh', '.cmd', '.json', '.xml', '.props', '.targets', '.csproj', '.sln', '.yml', '.yaml', '.txt', '.glsl', '.shader', '.editorconfig', '.gitattributes', '.gitignore')
$pathViolations = 0
$pathChecked = 0
foreach ($file in (Get-TrackedFiles @('*'))) {
    #文档允许路径示例，但不能把某台机器的本地文件当作公开下载或参考链接。
    if ($file -like '*.md') {
        $pathChecked++
        if ((Get-RepoText $file) -match '\]\((?:[A-Za-z]:[\\/]|/(?:Users|home)/)') {
            $pathViolations++
            Report-Violation 'absolute-path' $file 'Markdown 链接指向机器本地文件，请使用仓库相对路径或公开网址'
        }
        continue
    }
    if ($file -like 'Docs/*' -or $file -like 'UnityPackage/Documentation~/*' -or
        $file -like '1.6/*' -or $file -like 'Tools/Steam/*' -or $file -like 'README*') { continue }
    $extension = [IO.Path]::GetExtension($file).ToLowerInvariant()
    if ($scanExtensions -notcontains $extension) { continue }
    $pathChecked++
    $hits = New-Object System.Collections.ArrayList
    $lines = Get-RepoLines $file
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '(^|[^0-9A-Za-z_])[A-Za-z]:[\\/]') { [void]$hits.Add(($i + 1)) }
    }
    if ($hits.Count -eq 0) { continue }
    $pathViolations++
    Report-Violation 'absolute-path' $file ('{0}:{1} 出现盘符绝对路径' -f $file, ($hits -join ','))
}
if ($pathViolations -eq 0) { Add-Pass 'absolute-path' ('{0} 个被跟踪文件没有机器路径或本地文档链接' -f $pathChecked) }

# ---------------------------------------------------------------------------
# 8. XML / JSON 能被解析
# ---------------------------------------------------------------------------
$parseViolations = 0
$parseChecked = 0
foreach ($file in (Get-TrackedFiles @('*.json'))) {
    $parseChecked++
    try { $null = (Get-RepoText $file | ConvertFrom-Json) } catch { $parseViolations++; Add-Fail 'parse-config' ('{0} 不是合法 JSON：{1}' -f $file, $_.Exception.Message) }
}
foreach ($file in (Get-TrackedFiles @('*.xml', '*.csproj', '*.props', '*.targets'))) {
    $parseChecked++
    try { $null = [xml](Get-RepoText $file) } catch { $parseViolations++; Add-Fail 'parse-config' ('{0} 不是合法 XML：{1}' -f $file, $_.Exception.Message) }
}
if ($parseViolations -eq 0) { Add-Pass 'parse-config' ('{0} 个 XML / JSON 都能解析' -f $parseChecked) }

# ---------------------------------------------------------------------------
# 9. 冲突标记残留
# ---------------------------------------------------------------------------
$markerViolations = 0
foreach ($file in (Get-TrackedFiles @('*.cs', '*.ps1', '*.psm1', '*.sh', '*.cmd', '*.md', '*.json', '*.xml', '*.props', '*.targets', '*.csproj', '*.yml', '*.yaml', '*.txt'))) {
    $lines = Get-RepoLines $file
    $hits = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^(<<<<<<<|>>>>>>>)') { [void]$hits.Add(($i + 1)) }
    }
    if ($hits.Count -eq 0) { continue }
    $markerViolations++
    Add-Fail 'conflict-markers' ('{0}:{1} 残留冲突标记' -f $file, ($hits -join ','))
}
if ($markerViolations -eq 0) { Add-Pass 'conflict-markers' '没有冲突标记残留' }

# ---------------------------------------------------------------------------
# 10. 提交范围检查（CI 传 PR 的 base SHA）
# ---------------------------------------------------------------------------
if ($BaseRevision) {
    $output = @(& git diff --check ('{0}...HEAD' -f $BaseRevision) 2>&1)
    if ($LASTEXITCODE -ne 0) {
        Add-Fail 'diff-check' ('范围 {0}...HEAD 里有冲突标记或空白错误：{1}' -f $BaseRevision, (($output | Select-Object -First 5) -join ' / '))
    } else {
        Add-Pass 'diff-check' ('范围 {0}...HEAD 没有冲突标记与空白错误' -f $BaseRevision)
    }
}

# ---------------------------------------------------------------------------
# 汇总
# ---------------------------------------------------------------------------
Write-Host ''
foreach ($item in $passes)   { Write-Host ('  [通过]     ' + $item) }
foreach ($item in $known)    { Write-Host ('  [已知缺口] ' + $item) }
foreach ($item in $failures) { Write-Host ('  [失败]     ' + $item) -ForegroundColor Red }

if (-not $Strict) {
    foreach ($entry in $KnownDebt) {
        if (-not $debtHit.ContainsKey(('{0}|{1}' -f $entry.Rule, $entry.Path))) {
            Write-Host ('  [提示]     允许清单条目已不再命中，修好后请从 $KnownDebt 删除：{0} / {1}' -f $entry.Rule, $entry.Path)
        }
    }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host ('检查未通过：{0} 项失败（{1} 项通过、{2} 处已知缺口）。' -f $failures.Count, $passes.Count, $known.Count) -ForegroundColor Red
    exit 1
}
Write-Host ('检查通过：{0} 项通过、{1} 处已知缺口（见 $KnownDebt 允许清单）。' -f $passes.Count, $known.Count)
exit 0
