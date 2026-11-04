param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$HarmonyPath, [string]$RuntimeDirectory = '', [string]$OutputDirectory = '', [string]$BuiltAtUtc = '')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$repo = Split-Path $PSScriptRoot -Parent
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $repo 'artifacts/Embedded' }
$runtime = if ($RuntimeDirectory) { [IO.Path]::GetFullPath($RuntimeDirectory) } else { Join-Path $repo '1.7/Assemblies' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$payload = Join-Path $output 'payload.zip'
$version = (Get-Content -LiteralPath (Join-Path $repo 'UnityPackage/package.json') -Encoding UTF8 -Raw | ConvertFrom-Json).version
#构建时间只在创建产物时确定；复制和压缩分发必须保留。
if ($BuiltAtUtc) {
    $parsedTime = [DateTimeOffset]::MinValue
    $formats = [string[]]@("yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'")
    if (![DateTimeOffset]::TryParseExact($BuiltAtUtc, $formats, [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal, [ref]$parsedTime)) {
        throw 'BuiltAtUtc must be an ISO 8601 UTC time ending in Z.'
    }
} else { $parsedTime = [DateTimeOffset]::UtcNow }
$builtTime = $parsedTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [Globalization.CultureInfo]::InvariantCulture)
#固定归档条目顺序和条目时间；固定输入和 BuiltAtUtc 可复现相同载荷。
$files = [Collections.Generic.SortedDictionary[string,string]]::new([StringComparer]::Ordinal)
foreach ($name in @('RimUnity.Core.dll','RimUnity.dll','RimUnity.Spine.dll')) {
    $files.Add("Assemblies/$name", (Join-Path $runtime $name))
}
foreach ($folder in @('Defs','Shaders','Languages')) {
    $root = Join-Path $repo "1.7/$folder"
    if (Test-Path -LiteralPath $root) {
        Get-ChildItem -LiteralPath $root -File -Recurse | ForEach-Object {
            $files.Add("$folder/" + [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\','/'), $_.FullName)
        }
    }
}
$files.Add('LICENSE', (Join-Path $repo 'LICENSE'))
Get-ChildItem -LiteralPath (Join-Path $repo 'ThirdParty') -File -Recurse | ForEach-Object {
    $files.Add([IO.Path]::GetRelativePath($repo, $_.FullName).Replace('\','/'), $_.FullName)
}
$stream = [IO.File]::Create($payload)
$zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($pair in $files.GetEnumerator()) {
        $entry = $zip.CreateEntry($pair.Key, [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
        $input = [IO.File]::OpenRead($pair.Value)
        $target = $entry.Open()
        try { $input.CopyTo($target) } finally { $input.Dispose(); $target.Dispose() }
    }
    $entry = $zip.CreateEntry('runtime.json')
    $entry.LastWriteTime = [DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
    $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
    try { $writer.Write(([ordered]@{version=$version;protocol=1;builtAtUtc=$builtTime;coordinatorRevision=1} | ConvertTo-Json -Compress)) } finally { $writer.Dispose() }
} finally { $zip.Dispose(); $stream.Dispose() }
#容器身份包含载荷和引导代码，允许不同版本并存；运行时 DLL 的身份不变。
$identity = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
Get-ChildItem -LiteralPath (Join-Path $repo 'Source/RimUnity.Embedded') -File | Sort-Object Name | ForEach-Object {
    $identity += $_.Name + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
}
$containerId = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($identity)))
& dotnet build (Join-Path $repo 'Source/RimUnity.Embedded/RimUnity.Embedded.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:PayloadPath=$payload" "-p:ContainerId=$containerId" "-p:RimUnityBuiltAtUtc=$builtTime" "-p:OutputPath=$output/"
if ($LASTEXITCODE -ne 0) { throw '内置运行时编译失败。' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'RimUnity.Embedded.targets') -Destination $output -Force
$references = Join-Path $output 'References'
[IO.Directory]::CreateDirectory($references) | Out-Null
foreach ($name in @('RimUnity.Core.dll','RimUnity.dll','RimUnity.Spine.dll')) {
    Copy-Item -LiteralPath (Join-Path $runtime $name) -Destination $references -Force
}
Write-Output "内置运行时：$output"
