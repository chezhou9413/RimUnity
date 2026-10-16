param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$HarmonyPath)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$fixtures = Join-Path ([IO.Path]::GetTempPath()) ('RimUnity/Embedded-' + [Guid]::NewGuid().ToString('N'))
foreach ($name in @('A','B')) {
    $about = Join-Path $fixtures "$name/About"
    [IO.Directory]::CreateDirectory($about) | Out-Null
    [IO.File]::WriteAllText((Join-Path $about 'About.xml'), "<ModMetaData><name>$name</name><packageId>test.$name</packageId></ModMetaData>", [Text.UTF8Encoding]::new($false))
}
& dotnet build (Join-Path $PSScriptRoot 'EmbeddedTests/Fixtures/Consumer/Consumer.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:FixtureRoot=$fixtures" "-p:EmbeddedPackage=$repo/artifacts/Embedded"
if ($LASTEXITCODE -ne 0) { throw '内容程序集接入构建失败。' }
& dotnet build (Join-Path $PSScriptRoot 'EmbeddedTests/EmbeddedTests.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath"
if ($LASTEXITCODE -ne 0) { throw '离线验证程序编译失败。' }
foreach ($scenario in @('single','duplicates','reverse','standalone-first','standalone-middle','standalone-last','scale')) {
    & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $scenario
    if ($LASTEXITCODE -ne 0) { throw "离线验证失败：$scenario；夹具目录：$fixtures" }
}
#提升内容版本要求，验证不兼容前置不会被内置载荷替代。
$manifest = Join-Path $fixtures 'A/1.7/Assemblies/EmbeddedFixture.Library.rimunity.json'
$originalManifest = [IO.File]::ReadAllText($manifest)
[IO.File]::WriteAllText($manifest, '{"protocol":1,"minimumVersion":"0.1.10"}', [Text.UTF8Encoding]::new($false))
& dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures 'incompatible-standalone'
if ($LASTEXITCODE -ne 0) { throw '不兼容独立前置验证失败。' }
[IO.File]::WriteAllText($manifest, $originalManifest, [Text.UTF8Encoding]::new($false))
#测试容器只改变归档元数据或许可字节；实际运行时仍使用本次编译的 DLL。
foreach ($variant in @('older','conflict')) {
    $variantRoot = Join-Path $fixtures $variant
    [IO.Directory]::CreateDirectory($variantRoot) | Out-Null
    $variantZip = Join-Path $variantRoot 'payload.zip'
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/payload.zip') -Destination $variantZip
    $zip = [IO.Compression.ZipFile]::Open($variantZip, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $zip.GetEntry($(if ($variant -eq 'older') { 'runtime.json' } else { 'LICENSE' }))
        $name = $entry.FullName
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $value = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $entry.Delete()
        if ($variant -eq 'older') { $value = '{"protocol":1,"version":"0.1.8"}' } else { $value += "`n" }
        $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($value) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $containerId = (Get-FileHash -LiteralPath $variantZip -Algorithm SHA256).Hash
    & dotnet build (Join-Path $repo 'Source/RimUnity.Embedded/RimUnity.Embedded.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:PayloadPath=$variantZip" "-p:ContainerId=$containerId" "-p:OutputPath=$variantRoot/bin/"
    if ($LASTEXITCODE -ne 0) { throw "测试容器编译失败：$variant" }
    $destination = if ($variant -eq 'older') { 'A' } else { 'B' }
    Copy-Item -LiteralPath (Join-Path $variantRoot 'bin/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$destination/1.7/Assemblies/RimUnity.Embedded.dll") -Force
    $scenario = if ($variant -eq 'older') { 'newest' } else { 'conflict' }
    & dotnet (Join-Path $PSScriptRoot 'EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $fixtures $scenario
    if ($LASTEXITCODE -ne 0) { throw "候选载荷验证失败：$scenario" }
    Copy-Item -LiteralPath (Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll') -Destination (Join-Path $fixtures "$destination/1.7/Assemblies/RimUnity.Embedded.dll") -Force
}
Write-Output "离线加载验证通过。夹具与缓存：$fixtures。未验证游戏内渲染和原生挂载。"
