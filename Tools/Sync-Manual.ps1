param([switch]$Check)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$manual = Join-Path $repo 'UnityPackage/Documentation~'
$files = @('README.md', 'README.en.md', 'UnityPackage/README.md', 'UnityPackage/README.en.md', 'Tools/Steam/README.md', '1.6/README.txt')
$files += @(Get-ChildItem -LiteralPath (Join-Path $repo 'Docs') -Recurse -File -Filter '*.md' | ForEach-Object { [IO.Path]::GetRelativePath($repo, $_.FullName).Replace('\', '/') })
$index = @()
foreach ($file in ($files | Sort-Object -Unique)) {
    $source = Join-Path $repo $file
    $destination = Join-Path $manual "Sources/$file"
    if ($Check) {
        if (!(Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $destination).Hash) { throw "离线文档未同步：$file" }
    } else {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
    $text = [IO.File]::ReadAllText($source)
    $title = [regex]::Match($text, '(?m)^# (.+)').Groups[1].Value.Trim()
    if (!$title) { $title = [IO.Path]::GetFileName($file) }
    $category = if ($file -match 'README.en|Docs/en/') { '原文·English' } elseif ($file -match 'Docs/zh-CN/|(^|/)README.md$') { '原文·中文' } else { '其他原文' }
    $index += [ordered]@{ title = "$title · $file"; source = $file; file = "Sources/$file"; category = $category }
}
$indexPath = Join-Path $manual 'sources.json'
$json = ConvertTo-Json -InputObject @($index) -Depth 4
if ($Check) {
    if (([IO.File]::ReadAllText($indexPath).Replace("`r`n", "`n").Trim()) -ne $json.Replace("`r`n", "`n").Trim()) { throw '离线文档索引未同步。' }
} else { [IO.File]::WriteAllText($indexPath, $json + "`n", [Text.UTF8Encoding]::new($false)) }
Write-Output "已核对 $($index.Count) 篇原始文档。"
