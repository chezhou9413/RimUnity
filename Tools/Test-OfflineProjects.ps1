# 离线测试工程：不需要游戏安装、不需要 Unity，只需要 .NET 10 SDK。
#
# 基础策略与诊断测试，以及源码直连的测试场 CPU 采样回归均可离线运行。
# RenderDiagnosticsTests 需要 <RimUnity.dll> <game> <0Harmony.dll> 三个参数，属于第二层，
# 由 .github/workflows/manual-verify.yml 的「渲染诊断测试」步骤运行，不在这里。
#
# 注意：不要给 `dotnet run` 追加 --nologo 之类的开关——它会被当成**程序参数**传进去，
# 而 MemoryAttributionTests 带参数时会进入需要游戏 DLL 的反射分支。
param([string[]]$Projects = @('CatalogPolicyTests', 'MemoryAttributionTests', 'SpikeCaptureTests', 'WarmupTests', 'LabCpuMetricsTests', 'ParticleDetailTests'))
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$repo = Split-Path -Parent $PSScriptRoot
$failed = @()
foreach ($name in $Projects) {
    $project = Join-Path $repo "Tools/$name"
    if (-not (Test-Path -LiteralPath (Join-Path $project "$name.csproj") -PathType Leaf)) {
        throw "找不到测试工程：$project/$name.csproj"
    }
    Write-Host "== $name =="
    & dotnet run --project $project -c Release
    if ($LASTEXITCODE -ne 0) { $failed += $name }
}

if ($failed.Count) {
    throw ("离线测试工程失败：{0}" -f ($failed -join '、'))
}
Write-Host ("离线测试工程全部通过：{0}" -f ($Projects -join '、'))
