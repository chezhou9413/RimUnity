# Tools 使用索引

## 日常构建只用根入口

安装 .NET 10 SDK 后，在仓库根目录运行 Windows 的 `.\build.cmd`，或 Linux/macOS 的 `sh ./build.sh`。这会构建运行时、内置接入包和 ShaderTool；自动查找 Steam 与 Harmony，不要求设置环境变量。

`build.cmd docs` / `sh build.sh docs` 同步已登记的文档，`build.cmd pack` / `sh build.sh pack` 打包已有产物。非标准安装可使用 `--game`、`--harmony`，或将 `build.example.json` 复制为不提交的 `build.local.json`。详见根目录 README。`Build/` 是这些命令共用的 C# 实现。

下面的脚本和测试工程供维护者按需使用，均不是日常构建前置步骤。保留其路径以兼容已有 CI 和源码引用。

## 可选 PowerShell 维护入口

- `Build.ps1`：构建运行时、延迟载荷和 ShaderTool，不执行部署。`-SkipEmbedded` 仅构建独立版和工具，不生成内置载荷。
- `Build-Embedded.ps1`：生成内置载荷和编译引用；由运行时构建调用。
- `Compile-Editor.ps1`：离线编译 Unity 插件源码，需要 Unity Editor 和工程路径。
- `Verify.ps1`：构建并检查 GLSL，可额外检查指定资源目录。此入口调用 Build，不执行部署。
- `Verify-Embedded.ps1`：离线验证版本选择、程序集身份及延迟内容加载。可用 `-WorkshopRoot` 指定实际工坊运行时，验证本地版存在但未启用的组合。
- `Test-EmbeddedInGame.ps1`：启动隔离存档目录的真实游戏测试，临时安装测试模组，结束后恢复原独立版。指定 `-WorkshopRoot` 时额外验证游戏实际生成的 `_steam` 副本标识。
- `Deploy.ps1`：备份并替换游戏中的独立版。
- `PerformanceLoop/Run.ps1`：用隔离配置启动特效测试场、自动采样和导出，支持运行时 A/B 对照及帧尾等待拆分。
- `Pack.ps1`：生成独立版、内置版与 Unity UPM 发布包。
- `Sync-Manual.ps1`：同步原始文档到 Unity 包；`-Check` 仅核对。
- `ModsSwap.ps1`：崩溃安全的 `Mods/RimUnity` 交换（`-Action Status|Move|Repair`）。状态目录在 `<游戏目录>/.rimunity-ci`，用只增不改的 journal 记录每一步，进程被强杀后 `-Action Repair` 能把原模组还原。

`Resolve-BuildPath.ps1`、`Copy-RuntimePackage.ps1` 是上述脚本共用的路径和发布清单实现。`RimUnity.Embedded.targets` 是内容模组的构建接入文件。`Directory.Build.props` 提供 Tools 工程共用的 Unity 路径配置。

## 回归测试

这些工程没有被主构建自动调用，但覆盖当前源码，不属于废弃文件：

- `EmbeddedTests`：独立版普通 DLL 兼容、内置加载器选版、跨程序集脚本与游戏启动夹具。
- `CatalogPolicyTests`：资源目录加载策略。
- `ExportProcessorTests`：Unity 导出处理器与文档库。
- `PublishingTests`：资源发布、回滚、清单及增量导出。
- `MemoryAttributionTests`：显存归责模型。
- `RenderDiagnosticsTests`：运行时渲染诊断；需要游戏与 Harmony，由第二层 CI 运行。
- `ScreenEfxTests`：屏幕特效播放与参数快照；需要游戏目录（参数或环境变量 `RIMWORLD_DIR`）。
- `SpikeCaptureTests`：性能尖峰采样和诊断数据。
- `TrailGeometryTests`：拖尾几何、折射与资源绑定；参数见其 README。
- `WarmupTests`：加载预热任务调度。

各项目所需参数和外部引用不同，运行前查看对应 csproj、Program.cs 或领域文档。

## 离线入口回归

这三个脚本不需要游戏；第一层 CI 会跑它们（见 `.github/workflows/README.md`）：

- `Test-BuildWorkflow.ps1`：在临时夹具里检查 `Build.ps1` / `Verify.ps1` / `Pack.ps1` 的调用顺序、`-SkipBuild` / `-SkipEmbedded` 语义与失败传播。
- `Test-ModsSwap.ps1`：`ModsSwap.ps1` 的 10 个崩溃窗口自测，全部在临时目录里跑。
- `Test-OfflineProjects.ps1`：编译并运行 `CatalogPolicyTests`、`MemoryAttributionTests`、`SpikeCaptureTests`、`WarmupTests` 四个不需要游戏的工程。

## 按需工具与素材

- `ResourcePacker`：整理已有资源导出、共享去重并生成 ZIP；输出到新目录，不是独立运行库打包入口。用法见 `Docs/zh-CN/ResourceLoading.md`。
- `Upgrade-LinearExports.ps1`：修正已确认来自 Linear 工程的旧导出；会修改指定资源并生成备份。不能用于 Gamma 工程，也不属于日常构建步骤。
- `Artwork`：封面、图标和工坊说明图的生成源码。
- `Steam`：工坊标题、双语说明和配图。

## 生成文件

- `bin/ShaderTool` 是 ShaderTool 的构建输出，仍被检查、资源升级和打包脚本使用。缺失时重新构建 `Source/RimUnity.ShaderTool/RimUnity.ShaderTool.csproj`。
- 测试工程的 `bin`、`obj` 可重新构建，不需要进入版本控制。
- 临时诊断构建与旧备份应放在仓库 `artifacts/`，不要继续堆放在 Tools 下。

`Source/.build`、`Tools/**/bin`、`Tools/**/obj` 和 `artifacts` 均为本地生成目录，已被 Git 忽略；其中的机器绝对路径不会进入源码仓库。
