# RimUnity Exporter

[简体中文](README.md) | [English](README.en.md)

在 Unity 制作 Prefab，并导出为 RimWorld 1.7 可使用的明文资源。

## 安装

使用 Unity 2022.3，打开 `Window → Package Manager`。源码包通过 `+ → Add package from disk` 选择本包的 `package.json`；发布的 `.tgz` 通过 `+ → Add package from tarball` 安装，无需解压或复制脚本到 Assets。

下载入口：[GitHub Releases](https://github.com/chezhou9413/RimUnity/releases)。选择 `com.chezhou.rimunity-*.tgz`，游戏模组 ZIP 不能作为插件安装。若尚无附件，可从源码安装并构建 ShaderTool。完整步骤见[导出工具安装与第一次导出](https://github.com/chezhou9413/RimUnity/blob/main/Docs/zh-CN/ExporterSetup.md)。

制作时使用 Unity 原生 GameObject、Transform、UGUI、ParticleSystem 和受支持的模型／动画组件。

## 导出

1. 打开 `Tools → RimUnity → 导出明文预制体`。
2. 选择项目中的 Prefab，填写描述与逻辑路径。
3. 选择内容模组目录，该目录需要有效的 `About/About.xml`。
4. 自定义 Shader 需要设置 RimWorld 安装目录与 `RimUnity.ShaderTool.exe` 路径。
5. 导出后，将 `1.7/Data/RimUnity` 中的清单与依赖随内容模组分发。

导出器解析嵌套 Prefab 和 Variant，并收集材质、纹理、网格、字体、动画及组件引用。失败时保留诊断，不替换上一次成功清单。

批量粒子入口：`Tools → RimUnity → 一键导出项目粒子`。

离线说明书：`RimUnity → 百科全书`，支持主题导航、全文搜索及中英文原文阅读。

导出扩展：自定义 `ExportProcessor` 并标记 `[RimUnityExportProcessor]`，可对每个源资源及递归依赖执行前后处理；或通过 `ExportOptions.processors` 挂载函数。临时修改优先使用 `ExportContext.CreateExportCopy<T>()`，需要恢复的操作用 `DeferCleanup` 登记。完整示例见百科全书“给导出挂处理函数”。

脚本批量入口：`RimUnity.Editor.PrefabExporter.Export(modDirectory, ExportRequest[])`。

## 游戏侧使用

引用 `RimUnity.Core.dll` 和 `RimUnity.dll`，使用 RimUnity 对象、组件与资源类型。自定义脚本需要编译为游戏侧 .NET 10 程序集。

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
```

制作端与游戏端脚本使用相同完整类型名，字段和事件引用由导出器绑定。UGUI 控件使用 `RimUnity.UI`，指针接口使用 `RimUnity.EventSystems`。

完整入门、API 和支持范围见 [RimUnity 使用文档](https://github.com/chezhou9413/RimUnity#使用文档)。

## 许可

作者：chezhou9413、keeptpa。

本插件使用包内 MIT 许可证。导出的美术、字体和其他资源按各自许可证分发。
