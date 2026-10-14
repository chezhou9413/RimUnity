# 制作、导出、接入与发布工作流

[简体中文](Workflow.md) | [English](../en/Workflow.md)

## 1. 准备两侧环境

制作端使用 Unity 2022.3 LTS，支持 Built-in 或 URP 14 的指定功能。游戏端使用 Windows x64、RimWorld 1.7 Waylith 和 .NET 10。支持范围见 [功能说明](Status.md)。

通过 Package Manager 的 **Add package from disk** 选择 `UnityPackage/package.json`，或安装发布的 TGZ。从源码安装时需先构建运行时与 ShaderTool。

从源码安装可在仓库根目录构建（替换自己的路径）：

```powershell
dotnet build ./Source/RimUnity/RimUnity.csproj -c Release `
  -p:RimWorldDir='C:/Games/RimWorld' `
  -p:HarmonyPath='C:/Mods/Harmony/1.7/Assemblies/0Harmony.dll'

dotnet build ./Source/RimUnity.ShaderTool/RimUnity.ShaderTool.csproj -c Release `
  -p:RimWorldDir='C:/Games/RimWorld'
```

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

```powershell
./Tools/Pack.ps1 -Output 'C:/Builds/RimUnity' -TempRoot 'C:/BuildTemp/RimUnity'
```

输出为框架 Mod ZIP 和 UPM TGZ，携带 MIT 及第三方许可。框架包不包含内容资源、Unity 工程、缓存、游戏程序集或测试素材。TGZ 包中 `Tools~/ShaderTool` 附带本项目工具，仍需配置本机游戏安装目录。

内容模组需单独打包：自己的 About、LoadFolders、业务 DLL、Defs 和 `1.7/Data/RimUnity` 全部依赖。不能只复制最新 generation，清单中的其他入口可能仍引用之前的文件。

本仓库采用 MIT；内容模组的美术、字体和第三方插件按其各自许可证处理。
