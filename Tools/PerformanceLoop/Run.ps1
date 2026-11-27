param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$Profile,
    [Parameter(Mandatory)][string]$Harmony,
    [string]$Baseline,
    [string]$Candidate,
    [switch]$BuildCandidate,
    [ValidateRange(5,180)][int]$Seconds = 60,
    [ValidateSet('Stages','BridgeOnly')][string]$CpuMode = 'Stages',
    [bool]$Bloom = $true,
    [switch]$RenderTrace,
    [switch]$Inspect,
    [string]$Output,
    [string]$RimUnitySettingsFile
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$game = (Resolve-Path -LiteralPath $GameRoot).Path
$profileRoot = (Resolve-Path -LiteralPath $Profile).Path
$harmonyFile = (Resolve-Path -LiteralPath $Harmony).Path
$modRoot = (Resolve-Path -LiteralPath (Join-Path $game 'Mods')).Path
$exe = Join-Path $game 'RimWorld.exe'
$gameLog = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Logs/Player.log'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw 'RimWorld.exe missing.' }
if (Get-Process RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue) { throw 'A RimWorld process is already running.' }
if ($Candidate -and $BuildCandidate) { throw 'Choose Candidate or BuildCandidate.' }
if ([bool]$Baseline -ne [bool]($Candidate -or $BuildCandidate)) {
    throw 'A/B requires Baseline and either Candidate or BuildCandidate.'
}
if ($Inspect -and $Baseline) { throw 'Inspect accepts the profile runtime. Omit Inspect for A/B.' }
if ($RimUnitySettingsFile -and !$Baseline) { throw 'RimUnitySettingsFile applies only to staged A/B runs.' }
if (!$Output) { $Output = Join-Path $repo ('artifacts/PerformanceLoop/' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)) }
$outputRoot = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $outputRoot) { throw "Output already exists: $outputRoot" }
$null = New-Item -ItemType Directory -Path $outputRoot
$configSnapshot = Join-Path $outputRoot 'profile-config'
$null = New-Item -ItemType Directory -Path $configSnapshot
Get-ChildItem -LiteralPath (Join-Path $profileRoot 'Config') -File -Filter '*.xml' |
    Copy-Item -Destination $configSnapshot
if (!(Test-Path -LiteralPath (Join-Path $configSnapshot 'ModsConfig.xml') -PathType Leaf)) {
    throw 'ModsConfig.xml is missing from the source profile.'
}
$runtimeSettings = $null
if ($Baseline) {
    $settingsFiles = @(Get-ChildItem -LiteralPath $configSnapshot -File -Filter 'Mod_*_RimUnityMod.xml')
    if ($RimUnitySettingsFile) {
        $runtimeSettings = $settingsFiles | Where-Object Name -EQ $RimUnitySettingsFile
        if (!$runtimeSettings) { throw "RimUnity settings file is not in the frozen profile: $RimUnitySettingsFile" }
    } elseif ($settingsFiles.Count -eq 1) {
        $runtimeSettings = $settingsFiles[0]
    } elseif ($settingsFiles.Count -gt 1) {
        throw "Multiple RimUnity settings files. Select one with -RimUnitySettingsFile: $($settingsFiles.Name -join ', ')"
    }
    if ($runtimeSettings) { Write-Output "A/B RimUnity settings: $($runtimeSettings.Name)" }
}
$nuget = Join-Path $outputRoot 'NuGet.Config'
'<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath $nuget -Encoding utf8
dotnet build (Join-Path $PSScriptRoot 'PerformanceLoop.csproj') -c Release "-p:RimWorldDir=$game" "-p:HarmonyPath=$harmonyFile" "-p:RestoreConfigFile=$nuget" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Performance loop mod failed to build.' }
if ($BuildCandidate) {
    $assemblies = Join-Path $outputRoot 'candidate-assemblies'
    $buildOptions = @('-c', 'Release', "-p:RimWorldDir=$game", "-p:HarmonyPath=$harmonyFile",
        "-p:RestoreConfigFile=$nuget", "-p:OutDir=$assemblies/", '-p:SkipEmbedded=true', '-p:NuGetAudit=false')
    dotnet build (Join-Path $repo 'Source/RimUnity.Spine/RimUnity.Spine.csproj') @buildOptions
    if ($LASTEXITCODE -ne 0) { throw 'Candidate failed to build.' }
    $Candidate = Join-Path $outputRoot 'candidate-package'
    & (Join-Path $repo 'Tools/Copy-RuntimePackage.ps1') -Destination $Candidate -AssemblyDirectory $assemblies
}

$tag = [guid]::NewGuid().ToString('N')
$owned = [Collections.Generic.List[string]]::new()
$runtimeAssemblyNames = @('RimUnity', 'RimUnity.Core', 'RimUnity.Spine')
$process = $null
function New-TestMod([string]$name) {
    $path = Join-Path $modRoot "RimUnity.PerformanceLoop.$tag.$name"
    $null = New-Item -ItemType Directory -Path $path
    $owned.Add($path)
    $path
}
function Stage-Runtime([string]$source, [string]$name) {
    $source = (Resolve-Path -LiteralPath $source).Path
    $about = Join-Path $source 'About/About.xml'
    foreach ($assemblyName in $runtimeAssemblyNames) {
        $dll = "$assemblyName.dll"
        if (!(Test-Path -LiteralPath (Join-Path $source "1.7/Assemblies/$dll") -PathType Leaf)) { throw "Missing $dll in $source" }
    }
    if (!(Test-Path -LiteralPath (Join-Path $source '1.7/Defs') -PathType Container) -or
        !(Test-Path -LiteralPath (Join-Path $source '1.7/Shaders') -PathType Container)) { throw "Incomplete runtime package: $source" }
    $target = New-TestMod $name
    $null = New-Item -ItemType Directory -Path (Join-Path $target 'About')
    $metadata = [xml](Get-Content -LiteralPath $about -Raw)
    $id = "chezhou.rimunity.perf.$tag.$name".ToLowerInvariant()
    $metadata.ModMetaData.packageId = $id
    $metadata.Save((Join-Path $target 'About/About.xml'))
    Copy-Item -LiteralPath (Join-Path $source 'LoadFolders.xml'),(Join-Path $source '1.7') -Destination $target -Recurse
    $hashes = @{}
    foreach ($assemblyName in $runtimeAssemblyNames) {
        $hashes[$assemblyName] = (Get-FileHash -LiteralPath (Join-Path $target "1.7/Assemblies/$assemblyName.dll")).Hash
    }
    # Verse.Mod.GetSettings keys on the mod folder name. This generated name is already filename-safe.
    [pscustomobject]@{ Id=$id; Hashes=$hashes; SettingsFile="Mod_$([IO.Path]::GetFileName($target))_RimUnityMod.xml" }
}

try {
    $runner = New-TestMod 'Runner'
    $null = New-Item -ItemType Directory -Path (Join-Path $runner 'About'),(Join-Path $runner 'Assemblies')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'About.xml') -Destination (Join-Path $runner 'About/About.xml')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin/Release/net10.0/RimUnity.PerformanceLoop.dll') -Destination (Join-Path $runner 'Assemblies')
    if ($Baseline) {
        $a = Stage-Runtime $Baseline 'A'
        $b = Stage-Runtime $Candidate 'B'
        $runs = [ordered]@{ A1=$a; B1=$b; B2=$b; A2=$a }
    } elseif ($Inspect) {
        $runs = [ordered]@{ Profile=$null }
    } else {
        $runs = [ordered]@{ A1=$null; A2=$null }
    }
    foreach ($run in $runs.Keys) {
        $save = Join-Path $outputRoot $run
        $config = Join-Path $save 'Config'
        $null = New-Item -ItemType Directory -Path $config
        Get-ChildItem -LiteralPath $configSnapshot -File -Filter '*.xml' |
            Copy-Item -Destination $config
        $modConfig = Join-Path $config 'ModsConfig.xml'
        $mods = [xml](Get-Content -LiteralPath $modConfig -Raw)
        $active = $mods.SelectSingleNode('/ModsConfigData/activeMods')
        if ($runs[$run]) {
            if ($runtimeSettings) {
                Copy-Item -LiteralPath $runtimeSettings.FullName -Destination (Join-Path $config $runs[$run].SettingsFile)
            }
            foreach ($item in @($active.SelectNodes('li'))) {
                if ($item.InnerText -in 'chezhou.rimunity','chezhou.rimunity_steam') { $null = $active.RemoveChild($item) }
            }
            $entry = $mods.CreateElement('li')
            $entry.InnerText = $runs[$run].Id
            $core = $active.SelectSingleNode("li[text()='ludeon.rimworld']")
            $null = $active.InsertBefore($entry, $core)
        }
        $entry = $mods.CreateElement('li')
        $entry.InnerText = 'chezhou.rimunity.performanceloop'
        $null = $active.AppendChild($entry)
        $mods.Save($modConfig)
        $prefPath = Join-Path $config 'Prefs.xml'
        $prefs = if (Test-Path -LiteralPath $prefPath) { [xml](Get-Content -LiteralPath $prefPath -Raw) } else { [xml]'<PrefsData />' }
        $dev = $prefs.DocumentElement.SelectSingleNode('devMode')
        if (!$dev) { $dev = $prefs.CreateElement('devMode'); $null = $prefs.DocumentElement.AppendChild($dev) }
        $dev.InnerText = 'True'
        $prefs.Save($prefPath)
        @{ Seconds=$Seconds; Bloom=$Bloom; CpuMode=$CpuMode; RenderTrace=[bool]$RenderTrace } | ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $save 'rimunity-performance-request.json') -Encoding utf8
        $launchTime = Get-Date
        $process = Start-Process -FilePath $exe -WorkingDirectory $game -ArgumentList ('-savedatafolder="' + $save + '"'),'-quicktest','-rimunityPerformanceLoop' -WindowStyle Hidden -PassThru
        Write-Output "$run started. PID=$($process.Id) Reports=$save"
        if (!$process.WaitForExit(($Seconds + 300) * 1000)) { throw "$run timed out. Game log: $gameLog" }
        $log = Get-Item -LiteralPath $gameLog -ErrorAction SilentlyContinue
        if ($log -and $log.LastWriteTime -ge $launchTime) {
            Copy-Item -LiteralPath $gameLog -Destination (Join-Path $save 'Player.log')
        }
        $resultFile = Join-Path $save 'rimunity-performance-result.json'
        if (!(Test-Path -LiteralPath $resultFile -PathType Leaf)) { throw "$run exited $($process.ExitCode) without a result. Reports: $save" }
        $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json -AsHashtable
        if ($process.ExitCode -ne 0 -or !$result.Passed) { throw "$run failed. $($result.Error)" }
        if ($runs[$run]) {
            foreach ($assemblyName in $runtimeAssemblyNames) {
                if ($result.RuntimeHashes[$assemblyName] -cne $runs[$run].Hashes[$assemblyName]) {
                    throw "$run loaded an unexpected $assemblyName assembly. Reports: $save"
                }
            }
        }
        Write-Output "$run exported $($result.Report)"
        $process = $null
    }
    python (Join-Path $PSScriptRoot 'Analyze.py') $outputRoot
    if ($LASTEXITCODE -ne 0) { throw "Analysis exited with code ${LASTEXITCODE}. Reports: $outputRoot" }
} finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
    foreach ($path in $owned) {
        if (!(Test-Path -LiteralPath $path)) { continue }
        $actual = Get-Item -LiteralPath $path
        if ($actual.Parent.FullName -ne $modRoot -or !$actual.Name.StartsWith("RimUnity.PerformanceLoop.$tag.") -or $actual.LinkType) {
            throw "Refusing to clean unexpected path: $path"
        }
        Remove-Item -LiteralPath $actual.FullName -Recurse -Force
    }
}
