# RimUnity

[简体中文](README.md) | [English](README.en.md)

![RimUnity](Docs/Images/RimUnity-Cover.png)

RimUnity 是面向 RimWorld 1.7 的 Unity 资源导出与运行框架。在 Unity 中制作预制体、粒子、材质、UI 和模型，导出明文资源与依赖，再由游戏的 Waylith 引擎加载和运行。框架提供熟悉的 GameObject、Transform、MonoBehaviour 等接口，让资源制作与模组代码衔接起来。具体功能见[支持范围与限制](Docs/zh-CN/Status.md)。

## 快速开始

**0.1.9 Preview：[独立模组 ZIP](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/RimUnity-0.1.9-preview.zip) · [内置运行时 ZIP](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/RimUnity.Embedded-0.1.9-preview.zip) · [Unity 导出工具 TGZ](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/com.chezhou.rimunity-0.1.9.tgz) · [SHA-256](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/SHA256SUMS.txt)**

| 接入方式 | 使用方法 |
| --- | --- |
| 独立前置 | 玩家启用 Harmony、RimUnity 和内容模组；所有内容共用独立前置。 |
| 内置运行时 | 纯资源模组携带 `RimUnity.Embedded.dll`；C# 内容模组必须通过包内 `.targets` 发布延迟脚本。见[内置接入指南](Docs/zh-CN/EmbeddedRuntime.md)。 |

**两种方式均需要 Harmony。** 启用独立 `Chezhou.RimUnity` 时始终优先使用独立版，内置载荷不挂载；独立版不兼容会明确报错。没有独立版时选择满足脚本要求的最新兼容内置版，相同副本只运行一次。`References` 仅用于编译，不能额外复制到模组。没有联网检测或自动更新，支持离线游玩。

RimUnity 基于 Waylith 提供兼容接口，**不包含、也不另行加载真正的 Unity 引擎**。本版通过编译、.NET 10 离线加载检查，以及三个内置测试模组在有/无独立前置时的真实启动验证。地图长期渲染和 UI 目视检查尚未覆盖；详见[验证范围](Docs/zh-CN/Release-0.1.9.md)。

只玩模组的玩家安装 Harmony、RimUnity 和对应内容模组即可，不需要 Unity 编辑器。制作资源的作者请先阅读[导出工具安装与第一次导出](Docs/zh-CN/ExporterSetup.md)。

玩家请下载独立模组 ZIP，解压到游戏 `Mods`；其中包含 Core、主运行时和 Spine。源码仓库需先构建，完整发行包无需自行编译。

1. 在 RimWorld 中安装并启用 Harmony 和 RimUnity。
2. 在 Unity 2022.3 的 Package Manager 中选择 **Add package from disk**，安装 `UnityPackage/package.json`，也可以安装发布的 UPM TGZ。支持 Built-in 与 URP 14 的指定功能。
3. 制作 Prefab，通过 `Tools → RimUnity` 导出到内容模组目录。
4. 游戏侧使用 .NET 10，引用 `RimUnity.Core.dll`、`RimUnity.dll` 和所需游戏程序集。

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = new RimUnity.Vector3(x, y, z);
```

`contentMod` 是资源所属模组。导出资源位于该模组的 `1.7/Data/RimUnity`，加载时使用导出窗口中填写的逻辑路径。

## 从源码构建

只使用框架或安装发行版时无需编译。修改框架源码则需要 RimWorld 1.7 安装目录、Harmony 的 `0Harmony.dll` 和 .NET 10 SDK。

```powershell
$env:HARMONY_PATH = 'C:\Mods\Harmony\1.7\Assemblies\0Harmony.dll'
.\Tools\Build.ps1 -GameRoot 'C:\Games\RimWorld'
.\Tools\Verify.ps1 -GameRoot 'C:\Games\RimWorld'
.\Tools\Pack.ps1 -Output 'C:\out'
```

脚本在入口统一解析路径：显式参数优先，其次环境变量 `RIMWORLD_DIR` / `HARMONY_PATH` / `UNITY_EDITOR_DIR` / `UNITY_PROJECT` / `RIMUNITY_BACKUP_DIR`，最后是脚本默认值。目录存在之外还会核对必需文件（游戏目录要含 `Assembly-CSharp.dll` 与 `Waylith.dll`；Unity Editor/Data 要含 `Managed/Newtonsoft.Json.dll` 与 UnityEngine 程序集；Unity 工程要含 UGUI/TMP 程序集），路径指错时在入口直接说明缺什么，而不是等到编译或运行时才报错。部署与升级备份默认放在游戏安装目录下的 `.rimunity-backups`（在 `Mods` 之外，不会被模组扫描读到）。

直接调用 `dotnet build` 时需自行传入 `-p:RimWorldDir=`、`-p:HarmonyPath=`，或先设置同名环境变量（`Directory.Build.props` 不再提供默认值）。路径缺失时引用会解析不到，先给 `warning MSB3245`（只指出找不到哪个程序集），再在用到这些类型的编译步骤报 `CS0246`。完整步骤见[制作、导出、接入与发布工作流](Docs/zh-CN/Workflow.md)。

## 使用文档

Unity 内可打开 **RimUnity → 百科全书**：按主题整理的离线说明书、全文搜索，以及以下文档和 README 原文。导出扩展见[导出前后处理](Docs/zh-CN/ExportProcessors.md)。

- [快速开始](Docs/zh-CN/QuickStart.md)
- [内置运行时接入](Docs/zh-CN/EmbeddedRuntime.md)
- [导出工具安装与第一次导出](Docs/zh-CN/ExporterSetup.md)
- [API 快速接入](Docs/zh-CN/ApiQuickStart.md)
- [MonoBehaviour 与常用方法](Docs/zh-CN/MonoBehaviour.md)
- [类型与脚本用法](Docs/zh-CN/Usage.md)
- [资源载入与目录约定](Docs/zh-CN/ResourceLoading.md)
- [制作、导出与发布工作流](Docs/zh-CN/Workflow.md)
- [Unity 明文资源构建管理器](Docs/zh-CN/Publishing.md)
- [粒子导出与使用](Docs/zh-CN/ParticleQuickStart.md) · [粒子模块](Docs/zh-CN/ParticleExpansion.md)
- [自定义 Shader](Docs/zh-CN/CustomShaderConversion.md) · [IMGUI 材质](Docs/zh-CN/ImmediateUI.md)
- [颜色与 Bloom](Docs/zh-CN/ColorAndBloom.md)
- [Clip 屏幕后处理](Docs/zh-CN/ScreenEfx.md)
- [支持范围与限制](Docs/zh-CN/Status.md)
- [内置 Defs](Docs/zh-CN/Defs.md) · [Shader 目录](Docs/zh-CN/Shaders.md)

## 许可

贡献者：chezhou9413、keeptpa、Karl-441。欢迎新的维护者、贡献者和内容 Mod 作者加入我们！[GitHub 仓库](https://github.com/chezhou9413/RimUnity) · [Steam 创意工坊](https://steamcommunity.com/sharedfiles/filedetails/?id=3819570122)。

本项目使用 [MIT 许可证](LICENSE)。第三方许可见 [版权声明](ThirdParty/NOTICE.md)。内容模组的美术、字体和其他资源按各自许可证分发。
