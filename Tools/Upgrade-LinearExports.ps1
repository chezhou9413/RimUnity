param(
    [Parameter(Mandatory=$true)][string]$DataRoot,
    [string]$GameRoot = '',
    [string]$BackupRoot = '',
    [string]$StageRoot = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-BuildPath.ps1')
$GameRoot = Resolve-BuildGameRoot -Path $(if ($PSBoundParameters.ContainsKey('GameRoot')) { $GameRoot } else { '' }) -Required
$BackupRoot = Resolve-BuildBackupRoot -Path $BackupRoot -GameRoot $GameRoot
if ([string]::IsNullOrEmpty($StageRoot)) { $StageRoot = Join-Path ([IO.Path]::GetFullPath([IO.Path]::GetTempPath())) 'RimUnity/LinearExports-Stage' }
#仅用于确认来自 Linear 工程、且程序已选择 Linear 分支的既有导出；Gamma 工程必须通过 Unity 重新导出。
$root = [IO.Path]::GetFullPath($DataRoot).TrimEnd('\','/')
$catalog = Get-Content -LiteralPath "$root/catalog.json" -Encoding UTF8 -Raw | ConvertFrom-Json
$tool = Join-Path $PSScriptRoot 'bin/ShaderTool/RimUnity.ShaderTool.exe'
$assembly = [Reflection.Assembly]::LoadFrom([IO.Path]::ChangeExtension($tool, '.dll'))
$convert = $assembly.GetType('RimUnity.Rendering.ShaderColorSpace').GetMethod('Convert', [Reflection.BindingFlags]'Static,NonPublic')
$stage = Join-Path ([IO.Path]::GetFullPath($StageRoot)) "Stage-$([Guid]::NewGuid().ToString('N'))"
$backup = Join-Path $BackupRoot "LinearExports-$([Guid]::NewGuid().ToString('N'))"
Write-Output "暂存目录：$stage"
Write-Output "计划备份目录：$backup"
$pending = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$verified = [Collections.Generic.HashSet[string]]::new()
$pairs = [Collections.Generic.HashSet[string]]::new()
$encoding = [Text.UTF8Encoding]::new($false)

#清单路径必须留在自己的数据目录内。
function Resolve-Asset([string]$relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if ([IO.Path]::IsPathRooted($relative) -or !$path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "资源路径越界：$relative" }
    return $path
}
function Stage-Text([string]$relative, [string]$text) {
    $source = Resolve-Asset $relative
    if ([IO.File]::ReadAllText($source) -eq $text) { return }
    $target = Join-Path $stage $relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
    [IO.File]::WriteAllText($target, $text, $encoding)
    $pending[$relative] = $target
}
function Mark-Linear([string]$relative, $data) {
    $data | Add-Member -NotePropertyName colorSpace -NotePropertyValue 'Linear' -Force
    Stage-Text $relative (ConvertTo-Json -InputObject $data -Depth 100)
}

foreach ($asset in $catalog.assets | Where-Object { $_.type -in @('Shader','Material') }) {
    $data = Get-Content -LiteralPath (Resolve-Asset $asset.file) -Encoding UTF8 -Raw | ConvertFrom-Json
    if ($asset.type -eq 'Shader') {
        $pair = $data.vertexFile + '|' + $data.fragmentFile
        if ($pairs.Add($pair)) {
            $vertex = [IO.File]::ReadAllText((Resolve-Asset $data.vertexFile))
            $fragment = [IO.File]::ReadAllText((Resolve-Asset $data.fragmentFile))
            $converted = $convert.Invoke($null, @($vertex, $fragment))
            Stage-Text $data.vertexFile $converted.Item1
            Stage-Text $data.fragmentFile $converted.Item2
            foreach ($relative in @($data.vertexFile, $data.fragmentFile)) {
                if (!$pending.ContainsKey($relative)) { continue }
                $path = $pending[$relative]
                $hash = (Get-FileHash -LiteralPath $path).Hash
                if ($verified.Add($hash)) {
                    & $tool $GameRoot verify $path
                    if ($LASTEXITCODE -ne 0) { throw "Shader 编译或布局校验失败，未发布本批次：$relative" }
                }
            }
        }
        $descriptor = Get-Content -LiteralPath (Resolve-Asset $data.descriptorFile) -Encoding UTF8 -Raw | ConvertFrom-Json
        Mark-Linear $data.descriptorFile $descriptor
    }
    Mark-Linear $asset.file $data
}
#全部转换成功后再发布，原内容及改动清单单独保存。
foreach ($entry in $pending.GetEnumerator()) {
    $path = Resolve-Asset $entry.Key
    $saved = Join-Path $backup $entry.Key
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($saved)) | Out-Null
    Copy-Item -LiteralPath $path -Destination $saved
    Copy-Item -LiteralPath $entry.Value -Destination $path -Force
}
[IO.Directory]::CreateDirectory($backup) | Out-Null
[IO.File]::WriteAllText((Join-Path $backup 'changes.json'), (ConvertTo-Json -InputObject @($pending.Keys) -Depth 5), $encoding)
Write-Output "已更新 $($pending.Count) 个文件，$($verified.Count) 个不同阶段程序通过编译；备份：$backup"
Write-Output "暂存目录：$stage"
