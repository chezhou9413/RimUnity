# 制作、导出、接入与发布工作流

[简体中文](Workflow.md) | [English](../en/Workflow.md)

## 1. 准备两侧环境

制作端使用 Unity 2022.3 LTS，支持 Built-in 或 URP 14 的指定功能。游戏端使用 Windows x64、RimWorld 1.7 Waylith 和 .NET 10。支持范围见 [功能说明](Status.md)。

通过 Package Manager 的 **Add package from disk** 选择 `UnityPackage/package.json`，或安装发布的 TGZ。从源码安装时需先构建运行时与 ShaderTool。

从源码安装只需 .NET 10 SDK，在仓库根目录运行：

```text
Windows:       .\build.cmd
Linux / macOS: sh ./build.sh
```

默认构建 Core、Runtime、Spine、内置接入包与 ShaderTool；`--skip-embedded` 仅跳过内置包。根入口会读取 Steam 的库清单并自动寻找游戏与 Harmony。非标准安装使用 `--game` / `--harmony`，或把 `build.example.json` 复制成 Git 忽略的 `build.local.json` 并填写 `game` / `harmony`。无需全局环境变量。配置文件路径相对仓库，命令参数相对终端当前目录；多个候选会提示选择。

根入口也提供 `docs` 和 `pack`，全程使用 .NET，不调用 PowerShell 或外部 tar。`docs` 同步 `UnityPackage/Documentation~/sources.json` 已登记的原文；新增文档应先登记索引。构建中间文件位于 `Source/.build`，不依赖 Windows 的 `TEMP`。跨平台构建不代表游戏渲染或 Unity 导出器的全部原生依赖已经完成跨平台验收。

以下是需要 PowerShell 7 的**可选维护入口**，日常构建不需要运行它们：

- `Build.ps1`：构建 Core、Runtime、Spine、内置接入包和 ShaderTool。只构建独立版和工具时可用 `-SkipEmbedded`；完整发版还需构建内置包。
- `Verify.ps1`：默认先调用一次 Build，再校验着色器。`-SkipBuild` 检查已有产物；`-Embedded` 加跑内置运行时离线测试；`-DataRoot` 加跑指定资源目录的 Prefab 检查。`-SkipBuild` 不会重建发布运行时和 ShaderTool，但内置测试仍会编译临时夹具和测试程序。
- `Pack.ps1`：检查现有运行时、工具、内置接入包和文档镜像，生成发布包；不编译，也不更新文档。打包前应对当前源码完成 Build/Verify，文件存在不代表产物一定是最新的。
- `Deploy.ps1`：显式部署独立模组到游戏并备份旧版本。Build、Verify、Pack 不调用它。

一次构建并验证可直接运行 `./Tools/Verify.ps1 -Embedded`，无需先 Build。已有产物的 CI 则运行 `Build.ps1` → `Verify.ps1 -SkipBuild -Embedded`。`Verify-Embedded.ps1` 仍可单独调用；它不构建发行运行时，需要先 Build。真实游戏测试由 `Test-EmbeddedInGame.ps1` 单独执行，离线验证不替代游戏内渲染验证。

不需要游戏或 SDK 的入口回归检查：`pwsh -File ./Tools/Test-BuildWorkflow.ps1`。它验证调用顺序、跳过构建、失败中断和打包前置检查，不代替真实编译。

产物位置：运行时在 `1.7/Assemblies`，ShaderTool 在 `Tools/bin/ShaderTool`，内置接入包在 `artifacts/Embedded`，默认发布包在 `artifacts`。编译会改写仓库跟踪的 Runtime/Core DLL，提交源码时应排除无意的二进制变更。


希望直接构建单个 csproj 时，仍可明确传入 MSBuild 属性；该模式不读取根入口的本地配置或 Steam 自动发现结果：

```text
dotnet build Source/RimUnity.Spine/RimUnity.Spine.csproj -c Release -p:RimWorldDir="<游戏程序集目录>" -p:HarmonyPath="<0Harmony.dll 路径>"
```

两个值都不提供时，引用会解析不到（`warning MSB3245` 指出找不到哪个程序集，但不写出缺失的路径），随后在用到这些类型的编译步骤报 `CS0246`；无论哪种方式都不会静默使用某台机器的私有目录。

构建脚本在入口不只检查目录存在，还核对该用途的必需文件：游戏目录要含 `Assembly-CSharp.dll` 与 `Waylith.dll`，Unity Editor/Data 要含 `Managed/Newtonsoft.Json.dll` 与 UnityEngine 程序集，Unity 工程要含 UGUI／TMP 程序集。路径指错时脚本直接说明缺哪个文件，不会走到编译才失败。`Tools/` 下的 `ResourcePacker` 与 `PublishingTests` 通过 `Tools/Directory.Build.props` 读取同一个 `UNITY_EDITOR_DIR`，也可以用 `-p:UnityEditorDir=` 覆盖。

部署和升级的备份根默认取本次 `-GameRoot` 或 `RIMWORLD_DIR` 解析出的游戏安装目录下的 `.rimunity-backups`（位于 `Mods` 之外，不会被模组扫描读到），也可用 `-BackupRoot` 或环境变量 `RIMUNITY_BACKUP_DIR` 指定。备份子目录按需创建，不会因默认路径不可用而改存临时目录。升级的暂存目录仍在系统临时区；转换前打印暂存与计划备份路径，完成后再次打印实际路径。部署失败会保留原始原因，并区分旧版已恢复、回滚失败和没有旧版备份的情况，给出对应恢复位置。

运行时的 `RimUnity.Core.dll` 和 `RimUnity.dll` 输出到 `1.7/Assemblies`，离线工具输出到 `Tools/bin/ShaderTool`。离线工具需要配置目标游戏安装目录。发布包不包含游戏 DLL。

`Compile-Editor.ps1` 是可选的插件源码编译入口，需要指定 Unity Editor/Data 和一个已导入 UGUI／TMP 的 UnityProject。它不启动 Unity，不替代编辑器自身的包导入检查。

## 2. 制作与组织资源

在自己的 Unity 工程中使用原生组件制作。建议区分 Effects、UI、Models 和 Materials 目录，并将需要独立加载的资源设为独立导出入口。嵌套 Prefab 和 Variant 由 Unity 解析后导出最终层级。

为需要泛光的对象设置专用 Layer，子对象也需设置；不同内容模组应协调层编号。不要把灯光、后处理或 Shader Graph 的存在理解成已获得等效支持。自定义脚本需要在游戏端适配并重新编译。

目标是使用方的内容模组根目录，应有有效 `About/About.xml` 和 packageId。不要把内容导出到 RimUnity 框架本身。

## 3. 单个 Prefab 导出

打开 `Tools → RimUnity → 导出明文预制体`：

1. 选择项目里的 Prefab 资产，不能选择场景实例。
2. 填写描述和逻辑路径，例如 `Effects/Portal`。
3. 选择目标内容模组目录。
4. 自定义 Shader 需要填写 RimWorld 安装目录和 `RimUnity.ShaderTool.exe` 路径。
5. 点击导出，检查 Unity Console 的错误与降级警告。

工具路径保存于本机 EditorPrefs，不是内容模组需要携带的绝对路径。输出使用 UTF-8 JSON、GLSL、图片和字体等依赖文件。

## 4. 批量与独立资源导出

一个事务可通过编辑器脚本导出多个入口：

```csharp
using RimUnity.Editor;
using UnityEditor;
using UnityEngine;

//放在自己的 Editor 脚本方法中，modDirectory 指向内容模组根目录。
PrefabExporter.Export(modDirectory,
    new ExportRequest {
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Effects/Portal.prefab"),
        path = "Effects/Portal", description = "传送门"
    },
    new ExportRequest {
        asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/Flow.mat"),
        ui = true, path = "Materials/UIFlow", description = "界面流光材质"
    });
```

`ui = true` 用于独立 UI 材质／Shader 的 GUI 顶点布局；世界材质使用默认 false。一次 `PrefabExporter.Export` 整体成功后才替换 catalog，失败保留旧成功清单；未重新导出的入口仍保留。

批量粒子窗口位于 `Tools → RimUnity → 一键导出项目粒子`，按目录查找带粒子的 Prefab。它逐个提交，所以可能部分成功。自动化可调用：

```powershell
& 'C:/Unity/Editor/Unity.exe' -batchmode -quit `
  -projectPath 'C:/Projects/MyEffects' `
  -executeMethod RimUnity.Editor.ParticleBatchExporter.Batch `
  -rimUnityMod 'C:/Mods/MyContentMod' `
  -rimUnityFolder 'Assets/Effects' `
  -rimUnityReport 'C:/BuildReports/particles.json' `
  -rimUnityShaderTool 'C:/Projects/RimUnity/Tools/bin/ShaderTool/RimUnity.ShaderTool.exe' `
  -rimUnityGame 'C:/Games/RimWorld' `
  -logFile 'C:/BuildReports/unity-export.log'
```

这条命令会启动 Unity；不要针对已被另一个编辑器打开的同一工程执行。先创建报告目录。批次可能捕获单项错误后正常退出，因此不能只凭进程退出码判断全部成功，必须检查报告中的 `exported`、`degraded`、`error` 和 `warnings`。

导出贴图时保留图形设备，不要添加 `-nographics`。非可读或压缩贴图需要 GPU 拷贝和读回；Windows 自动化可添加 `-force-d3d11`。当前设备为 Null 时，导出器会拒绝这类贴图并保留上一份成功产物。

带 AB 标签的资源可由 `BundleAssetExporter.Export(modDirectory, reportFile, bundleNames)` 导出为 `Bundles/...` 入口。游戏加载导出的明文资源，不读取二进制 AB。

## 5. 游戏侧接入

按 [API 快速接入](ApiQuickStart.md) 声明依赖、引用程序集并编译业务代码。在地图或界面需要资源时，通过所属 ModContentPack 和逻辑路径加载，实例化模板，并由拥有者负责停止及销毁。

更新游戏资源前先退出游戏，复制业务 DLL、catalog 和全部引用文件后重新启动。没有运行时热重载。加载问题按 [资源载入](ResourceLoading.md) 检查。

## 6. 打包交付

框架在构建运行时及 ShaderTool 后执行：

```text
Windows:       .\build.cmd docs
               .\build.cmd pack
Linux / macOS: sh ./build.sh docs
               sh ./build.sh pack
```

`pack --output "<输出目录>"` 可选择目录；已有同名归档时会要求改用新目录，避免覆盖。`pack` 只检查和打包已有产物，不自动重编译或修改文档。ShaderTool 的启动文件按当前构建系统打包，运行时所需的原生库仍来自相应游戏安装。

输出为框架 Mod ZIP 和 UPM TGZ，携带 MIT 及第三方许可。框架包不包含内容资源、Unity 工程、缓存、游戏程序集或测试素材。TGZ 包中 `Tools~/ShaderTool` 附带本项目工具，仍需配置本机游戏安装目录。

内容模组需单独打包：自己的 About、LoadFolders、业务 DLL、Defs 和 `1.7/Data/RimUnity` 全部依赖。不能只复制最新 generation，清单中的其他入口可能仍引用之前的文件。

本仓库采用 MIT；内容模组的美术、字体和第三方插件按其各自许可证处理。
