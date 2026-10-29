param(
    [Parameter(Mandatory)][string]$GameRoot,
    [Parameter(Mandatory)][string]$HarmonyPath,
    [Parameter(Mandatory)][string]$BaselineDirectory,
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [string]$OutputDirectory = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repo ('Artifacts/verify/trail-abba-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')) }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$baseline = [IO.Path]::GetFullPath($BaselineDirectory)
$candidate = [IO.Path]::GetFullPath($CandidateDirectory)
$baselineHash = (Get-FileHash -LiteralPath (Join-Path $baseline 'RimUnity.dll')).Hash
$candidateHash = (Get-FileHash -LiteralPath (Join-Path $candidate 'RimUnity.dll')).Hash
$runs = @()
foreach ($job in @(
    @{Name='A1'; Runtime=$baseline}, @{Name='B1'; Runtime=$candidate},
    @{Name='B2'; Runtime=$candidate}, @{Name='A2'; Runtime=$baseline}
)) {
    $file = Join-Path $OutputDirectory ($job.Name + '.json')
    & dotnet run --project (Join-Path $PSScriptRoot 'ParticleDrawSpaceTests') -c Release --no-restore -- $job.Runtime $GameRoot (Split-Path $HarmonyPath -Parent) --trail-reserve-single $file
    if ($LASTEXITCODE -ne 0) { throw "Trail benchmark failed: $($job.Name)" }
    $runs += Get-Content -LiteralPath $file -Raw -Encoding UTF8 | ConvertFrom-Json
}
if ($baselineHash -ne (Get-FileHash -LiteralPath (Join-Path $baseline 'RimUnity.dll')).Hash -or
    $candidateHash -ne (Get-FileHash -LiteralPath (Join-Path $candidate 'RimUnity.dll')).Hash) { throw 'Runtime changed during benchmark.' }
$comparisons = @()
for ($index = 0; $index -lt $runs[0].Rows.Count; $index++) {
    $a1 = $runs[0].Rows[$index]; $a2 = $runs[3].Rows[$index]
    $b1 = $runs[1].Rows[$index]; $b2 = $runs[2].Rows[$index]
    $a = ($a1.MedianMs + $a2.MedianMs) / 2
    $b = ($b1.MedianMs + $b2.MedianMs) / 2
    $spread = [Math]::Max([Math]::Abs($a1.MedianMs - $a2.MedianMs), [Math]::Abs($b1.MedianMs - $b2.MedianMs))
    $delta = $b - $a
    $comparisons += [ordered]@{
        Layout=$a1.Layout; Ribbon=$a1.Ribbon; Points=$a1.Points; Trails=$a1.Trails
        BaselineMs=$a; CandidateMs=$b; DeltaMs=$delta; RelativeChange=$delta / $a; ObservedRepeatSpreadMs=$spread
        Status=$(if ([Math]::Abs($delta) -le $spread) {'WithinObservedSpread'} elseif ($delta -gt 0) {'Slower'} else {'Faster'})
    }
}
$summary = Join-Path $OutputDirectory 'summary.json'
[ordered]@{
    BaselineHash=$baselineHash; CandidateHash=$candidateHash; Runs=$runs; Comparisons=$comparisons
    Caveats='Separate processes, one runtime each in default loading context, ABBA order. Repeated-run spread is not a confidence interval. Synthetic CPU geometry only; no GPU/submission/whole-frame savings claim. Do not use cross-AssemblyLoadContext ratios.'
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $summary -Encoding UTF8
Write-Output "Trail comparison: $summary"
