param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$HarmonyPath, [Parameter(Mandatory)][string]$FixtureRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$baseline = Join-Path $repo 'artifacts/Embedded/RimUnity.Embedded.dll'
$run = Join-Path $FixtureRoot 'SelectionCases'
[IO.Directory]::CreateDirectory($run) | Out-Null
$baseZip = Join-Path $repo 'artifacts/Embedded/payload.zip'
$zip = [IO.Compression.ZipFile]::OpenRead($baseZip)
try {
    $reader = [IO.StreamReader]::new($zip.GetEntry('runtime.json').Open())
    try { $baseManifest = $reader.ReadToEnd() | ConvertFrom-Json -AsHashtable } finally { $reader.Dispose() }
} finally { $zip.Dispose() }
$baseHash = (Get-FileHash -LiteralPath $baseZip -Algorithm SHA256).Hash

function New-Variant([string]$Name, [hashtable]$Changes = @{}, [string]$RemoveEntry = '', [string]$CoordinatorTime = '') {
    $directory = Join-Path $run $Name
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $payload = Join-Path $directory 'payload.zip'
    Copy-Item -LiteralPath $baseZip -Destination $payload -Force
    $archive = [IO.Compression.ZipFile]::Open($payload, [IO.Compression.ZipArchiveMode]::Update)
    try {
        $entry = $archive.GetEntry('runtime.json')
        $manifest = $baseManifest.Clone()
        foreach ($key in $Changes.Keys) {
            if ($null -eq $Changes[$key]) { $manifest.Remove($key) } else { $manifest[$key] = $Changes[$key] }
        }
        $entry.Delete()
        $writer = [IO.StreamWriter]::new($archive.CreateEntry('runtime.json').Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($manifest | ConvertTo-Json -Compress)) } finally { $writer.Dispose() }
        if ($RemoveEntry) { $archive.GetEntry($RemoveEntry).Delete() }
    } finally { $archive.Dispose() }
    $hash = (Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash
    $identity = "$hash.$Name"
    & dotnet build (Join-Path $repo 'Source/RimUnity.Embedded/RimUnity.Embedded.csproj') -c Release --nologo "-p:RimWorldDir=$GameRoot" "-p:HarmonyPath=$HarmonyPath" "-p:PayloadPath=$payload" "-p:ContainerId=$identity" "-p:RimUnityBuiltAtUtc=$CoordinatorTime" "-p:OutputPath=$directory/bin/"
    if ($LASTEXITCODE) { throw "Variant build failed: $Name" }
    return @{ Path = (Join-Path $directory 'bin/RimUnity.Embedded.dll'); Hash = $hash; Assembly = "RimUnity.Embedded.$identity" }
}

function Check([string]$Name, [hashtable]$Expectation, [string]$Scenario = 'selection') {
    $Expectation | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $FixtureRoot 'selection-expectation.json') -Encoding utf8
    & dotnet (Join-Path $repo 'Tools/EmbeddedTests/bin/Release/net10.0/EmbeddedTests.dll') $repo $GameRoot $HarmonyPath $FixtureRoot $Scenario
    if ($LASTEXITCODE) { throw "Selection check failed: $Name" }
    Write-Output "PASS selection case: $Name"
}

$a = Join-Path $FixtureRoot 'A/1.7/Assemblies/RimUnity.Embedded.dll'
$b = Join-Path $FixtureRoot 'B/1.7/Assemblies/RimUnity.Embedded.dll'
$c = Join-Path $FixtureRoot 'C/1.7/Assemblies/RimUnity.Embedded.dll'
[IO.Directory]::CreateDirectory((Split-Path $c -Parent)) | Out-Null
try {
    $variants = @{}
    foreach ($spec in @(
        @{Name='later'; Changes=@{builtAtUtc='2030-01-01T00:00:00Z'}},
        @{Name='earlier'; Changes=@{builtAtUtc='2000-01-01T00:00:00Z'}},
        @{Name='missing-time'; Changes=@{builtAtUtc=$null}},
        @{Name='invalid-time'; Changes=@{builtAtUtc='not-a-UTC-time'}},
        @{Name='tie'; Changes=@{testMarker='different-payload'}},
        @{Name='missing-file'; Changes=@{builtAtUtc='2030-01-01T00:00:00Z'}; RemoveEntry='Assemblies/RimUnity.Core.dll'},
        @{Name='missing-manifest'; Changes=@{}; RemoveEntry='runtime.json'},
        @{Name='missing-version'; Changes=@{version=$null}},
        @{Name='missing-protocol'; Changes=@{protocol=$null}},
        @{Name='invalid-version'; Changes=@{version='not-a-version'}},
        @{Name='wrong-version-type'; Changes=@{version=19}},
        @{Name='wrong-protocol-type'; Changes=@{protocol='1'}},
        @{Name='overflow-protocol'; Changes=@{protocol=2147483648L}},
        @{Name='old-a'; Changes=@{version='0.1.9'; builtAtUtc='2040-01-01T00:00:00Z'}},
        @{Name='old-b'; Changes=@{version='0.1.9'; builtAtUtc='2041-01-01T00:00:00Z'}},
        @{Name='coordinator-late'; Changes=@{builtAtUtc='2000-01-01T00:00:00Z'}; CoordinatorTime='2040-01-01T00:00:00Z'},
        @{Name='wrong-protocol'; Changes=@{protocol=2}},
        @{Name='future'; Changes=@{version='99.0.0'; builtAtUtc='2040-01-01T00:00:00Z'}}
    )) {
        # Capture only the final hashtable; build output remains visible without contaminating the return value.
        $result = @(New-Variant @spec)
        $variants[$spec.Name] = $result[-1]
        $result | Select-Object -SkipLast 1 | Write-Host
    }
    foreach ($name in @('later','earlier','missing-time','invalid-time','tie','missing-file','missing-manifest','wrong-protocol','missing-version','missing-protocol','invalid-version','wrong-version-type','wrong-protocol-type','overflow-protocol')) {
        Copy-Item -LiteralPath $baseline -Destination $a -Force
        Copy-Item -LiteralPath $variants[$name].Path -Destination $b -Force
        $expected = if ($name -eq 'later') { 'test.b' } elseif ($name -eq 'tie' -and [string]::CompareOrdinal($variants[$name].Hash, $baseHash) -lt 0) { 'test.b' } else { 'test.a' }
        $expectation = @{provider=$expected}
        if ($name -eq 'invalid-time') { $expectation.warning = 'Invalid builtAtUtc' }
        if ($name -in @('missing-file','missing-manifest','wrong-protocol','missing-version','missing-protocol','invalid-version','wrong-version-type','wrong-protocol-type','overflow-protocol')) { $expectation.warning = 'Skipping embedded candidate' }
        Check $name $expectation
        Check "$name-reverse" $expectation 'reverse'
    }
    # Also put malformed A before healthy B in the selector's package ordering.
    foreach ($name in @('missing-version','missing-protocol','invalid-version','wrong-version-type','wrong-protocol-type','overflow-protocol')) {
        Copy-Item -LiteralPath $variants[$name].Path -Destination $a -Force
        Copy-Item -LiteralPath $baseline -Destination $b -Force
        Check "$name-bad-first" @{provider='test.b';warning='Skipping embedded candidate'}
        Check "$name-bad-first-reverse" @{provider='test.b';warning='Skipping embedded candidate'} 'reverse'
    }
    Copy-Item -LiteralPath $variants['missing-version'].Path -Destination $a -Force
    Copy-Item -LiteralPath $variants['missing-protocol'].Path -Destination $b -Force
    Check 'all-manifests-invalid-fields' @{error='No compatible embedded runtime';errorDetails=@('Missing or invalid runtime version','Missing or invalid runtime protocol')}

    # A has no carrier: its content remains discoverable through B's coordinator.
    Remove-Item -LiteralPath $a
    Copy-Item -LiteralPath $baseline -Destination $b -Force
    Check 'content-without-carrier' @{provider='test.b'}
    Copy-Item -LiteralPath $baseline -Destination $a -Force
    Check 'duplicates-stable-provider' @{provider='test.a'} 'reverse'

    Copy-Item -LiteralPath $variants['old-a'].Path -Destination $a -Force
    Copy-Item -LiteralPath $variants['old-b'].Path -Destination $c -Force
    Check 'older-conflict-does-not-block-highest' @{provider='test.b';warning='Different embedded payloads'} 'selection-third'

    Copy-Item -LiteralPath $baseline -Destination $a -Force
    Copy-Item -LiteralPath $variants['coordinator-late'].Path -Destination $b -Force
    Check 'coordinator-election' @{provider='test.a';coordinator=$variants['coordinator-late'].Assembly}
    Check 'coordinator-election-reverse' @{provider='test.a';coordinator=$variants['coordinator-late'].Assembly} 'reverse'

    Copy-Item -LiteralPath $variants['future'].Path -Destination $b -Force
    Check 'standalone-still-wins' @{provider='chezhou.rimunity'} 'standalone-first'
    Copy-Item -LiteralPath $baseline -Destination $b -Force
    Check 'commit-failure-no-fallback' @{error='fallback is disabled';initializations=1} 'commit-failure'
    Copy-Item -LiteralPath $variants['missing-file'].Path -Destination $a -Force
    Copy-Item -LiteralPath $variants['missing-manifest'].Path -Destination $b -Force
    Check 'all-candidates-invalid' @{error='No compatible embedded runtime'}
} finally {
    Copy-Item -LiteralPath $baseline -Destination $a -Force
    Copy-Item -LiteralPath $baseline -Destination $b -Force
    if (Test-Path -LiteralPath (Join-Path $FixtureRoot 'selection-expectation.json')) { Remove-Item -LiteralPath (Join-Path $FixtureRoot 'selection-expectation.json') }
}
Write-Output 'PASS embedded selection matrix (managed startup logic only; native game acceptance remains separate).'
