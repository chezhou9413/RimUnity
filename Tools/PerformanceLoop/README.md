# RimUnity 性能测试闭环

用 PowerShell 7、.NET 10 SDK 和 Python 3 运行。`-GameRoot` 指向实际 RimWorld 1.7 安装目录，`-Profile` 指向 RimCrow 的配置根目录，`-Harmony` 指向当前游戏版本的 `0Harmony.dll`。

在仓库根目录运行：

`pwsh -File Tools/PerformanceLoop/Run.ps1 -GameRoot <游戏目录> -Profile <RimCrow配置目录> -Harmony <Harmony.dll>`

- `-Inspect` 用配置现有的运行时跑一轮。从新游戏自动进入同一个 96×96 特效测试场，建立 48 个混合粒子实例，预热后采样并导出。省略这个开关时，运行器默认做两轮同版本基线，测量重复波动。
- `-Baseline <A包目录> -Candidate <B包目录>` 使用两份独立版运行时包跑 `A1 → B1 → B2 → A2`。目录须包含 `About/About.xml`、`LoadFolders.xml`、`1.7/Assemblies`、`1.7/Defs`、`1.7/Shaders`。脚本核对实机加载的 RimUnity、Core 和 Spine 与暂存包的 SHA-256。
- `-Seconds 60` 是每轮采样时长。`-CpuMode BridgeOnly` 可做低开销复测。`-Bloom:$false` 可用相同负载检查关闭 Bloom 的场景。
- `-Baseline <A包目录> -BuildCandidate` 直接构建当前工作区的 Release 到本批次目录，打包后进入相同的 A/B 流程。每次改动可用相同基线重跑。
- `-RenderTrace` 开启渲染线程诊断，额外导出取帧、呈现节奏等待、提交及呈现和队列完成通知。`other_ms` 包含指令录制和其余工作。最慢帧按相同单调时钟匹配与主线程积压等待重叠的渲染包，并记录最后一次队列完成通知到主线程恢复的时间。正式 A/B 默认关闭这组探针。

批次开始时将源配置 XML 冻结到 `profile-config`。每轮从该快照复制配置，使用单独的 `-savedatafolder`，并由 `quicktest` 新建临时游戏。测试安装在本次专用的临时模组目录。脚本退出时清理这些目录。原始配置、存档和已安装模组继续使用原位置。

A/B 将冻结的 `Mod_*_RimUnityMod.xml` 按暂存模组的文件夹名复制给 A、B 两组。只有一个匹配文件时自动使用，存在多个时传入 `-RimUnitySettingsFile <文件名>`，配置中没有匹配文件时使用各运行时的默认设置。汇总记录运行时实际读取的地图 HDR 模式，独立于 `-Bloom` 控制的测试场 Bloom。

`artifacts/PerformanceLoop/<批次>/summary.json` 包含各轮的帧时间、Bridge、提交前窗口和渲染积压等待的均值、P95 和 P99。每轮保留原始实验室报告和按报告帧序号对齐的 `.waits.csv`。`Throttle` 已包含积压等待和限帧睡眠两个子阶段。报告列出最慢八帧，可以用该文件检查尖峰来自哪个窗口。

对照先核对场景配置、地图 HDR 模式、CPU 探针版本、渲染跟踪开关、实测负载和同组重复的三份程序集哈希。各轮必须使用同类构建。使用分析构建时，汇总标记 `diagnostic_only`，正式性能基线使用普通构建。实例和系统数必须匹配，粒子与几何量允许 2% 的实测差异。无效对照仍保存汇总，分析器以退出码 2 结束。缺少校验字段的历史报告仍可查看原始测量。差值超过两组各自的重复波动时，才给出方向判定。这个判定适用于这批测量。渲染积压等待测到的是主线程阻塞时间，渲染线程内部阶段及 GPU 执行时间需要独立测量。
