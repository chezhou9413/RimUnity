# 导出工具安装与第一次导出

[简体中文](ExporterSetup.md) | [English](../en/ExporterSetup.md)

## 先选对需要的文件

| 使用者 | 需要什么 |
|---|---|
| 只玩模组的玩家 | Harmony、RimUnity 游戏模组，以及依赖它的内容模组；不需要 Unity 或导出工具 |
| 制作资源的作者 | Unity 2022.3 LTS、RimUnity Exporter UPM 插件、本机 RimWorld 1.7；编译游戏侧脚本需要 .NET 10 SDK |

工坊中的 RimUnity 提供游戏运行时。作者工具单独以 `com.chezhou.rimunity-<版本>.tgz` 分发，内含 Unity 插件与离线 ShaderTool。Unity 编辑器本体由作者自行安装，不包含在模组或插件包内。

**[直接下载导出工具 0.1.5](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.5/com.chezhou.rimunity-0.1.5.tgz)**，或在 [GitHub Releases](https://github.com/chezhou9413/RimUnity/releases) 选择与游戏侧 RimUnity 对应版本的 TGZ。`RimUnity-<版本>-preview.zip` 是游戏模组包，不能作为 Unity 插件安装。本地打包产物的文件名还会带时间戳。也可使用下面的源码安装方式。

## 安装到 Unity

**从 TGZ 安装：**在 Unity 中打开 `Window → Package Manager`，点击 `+ → Add package from tarball`，选择下载的 `.tgz`，等待依赖导入与脚本编译完成。无需解压 TGZ，也不用将其中脚本复制到 Assets。操作说明见 [Unity 官方文档](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-tarball.html)。

**从源码安装：**下载或克隆 [RimUnity 仓库](https://github.com/chezhou9413/RimUnity)，在 Package Manager 中选择 `+ → Add package from disk`，打开 `UnityPackage/package.json`。保留仓库目录，Unity 会持续引用其中的文件。源码安装还需按[工作流](Workflow.md)构建运行时与 ShaderTool；仓库源码不等于已打包的工具附件。操作说明见 [Unity 官方文档](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-local.html)。

制作端使用 Built-in 或 URP 14 的受支持功能。包会声明 UGUI、TMP 和 Newtonsoft.Json 依赖。若没有出现 RimUnity 菜单，先查看 Console 的编译或包导入错误。

## 配置导出目录和 ShaderTool

目标是自己的内容模组根目录，例如 `C:/Mods/MyContentMod`，其中应有有效的 `About/About.xml`。不要选择 `1.7` 子目录，也不要把内容输出到 RimUnity 框架里。

| 字段 | 应填写的路径 |
|---|---|
| 目标 Mod 目录 | 包含 `About/About.xml` 的内容模组根目录 |
| RimWorld 安装目录 | 含 `Waylith.dll` 的 RimWorld 1.7 游戏根目录，不是 Mods 目录 |
| ShaderTool exe | `RimUnity.ShaderTool.exe` 的完整路径 |

TGZ 的工具位于已安装包的 `Tools~/ShaderTool`。可以先打开 `Tools → RimUnity → 一键导出项目粒子`：没有有效的已保存工具路径时，该窗口会查找随包附带的工具；将显示的 `ShaderTool exe` 路径复制到单个预制体窗口或构建管理器即可。若窗口仍显示旧工具路径，重新选择与当前版本对应的工具。

源码安装时，构建后的工具位于仓库 `Tools/bin/ShaderTool/RimUnity.ShaderTool.exe`。不要只移动其中的 EXE，需保留同目录下的 DLL、deps.json 和 runtimeconfig.json。运行该工具需要可用的 .NET 10 环境；安装 .NET 10 SDK 也可用于后续脚本编译。

ShaderTool 使用本机游戏的编译接口。游戏程序集不随插件分发，作者必须设置自己的游戏安装目录。单个导出中的自定义 Shader 转换需要这两项工具路径；构建管理器也会检查它们。

## 第一次导出一个粒子预制体

1. 在 Unity 中制作一个简单的 ParticleSystem，先确认原效果能播放，再将它保存为项目中的 Prefab 资产。
2. 打开 `Tools → RimUnity → 导出明文预制体`，将 Prefab 资产拖入 `Prefab` 字段。
3. 将逻辑路径填为 `Effects/BasicBurst`，写上便于识别的描述。
4. 选择内容模组根目录，填写游戏和 ShaderTool 路径，然后点击“导出”。
5. 检查窗口结果和 Console。成功后，内容模组中应出现 `1.7/Data/RimUnity/catalog.json` 及其依赖文件。

选择项目中的 Prefab 资产，不要选择场景实例。导出器自动收集所需材质、贴图等依赖；遇到不支持的 Shader 或组件时，根据诊断处理。导出成功表示文件已生成，实际显示仍需在游戏里核对。

## 在游戏中使用

内容模组声明 RimUnity 依赖，引用框架程序集，并在地图已加载后的游戏主线程调用：

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = new RimUnity.Vector3(x, y, z);
instance.SetActive(true);
```

`contentMod` 必须是资源实际所属模组；`x`、`y`、`z` 是调用方提供的地图世界坐标。显式播放与停止粒子见[粒子快速开始](ParticleQuickStart.md)，依赖声明、类型引用与销毁示例见 [API 快速接入](ApiQuickStart.md)。导出器不替作者自动触发游戏里的特效，也不自动编译业务脚本。

更新文件后重启游戏。发布内容模组时携带自己的业务程序集、清单与全部资源依赖；玩家不需要随包获得 Unity 工程或 ShaderTool。

## 资源多了以后

- 按目录批量导出粒子：`Tools → RimUnity → 一键导出项目粒子`。
- 管理 AB 标签、独立入口及多个输出目录：`RimUnity → 明文资源构建管理器`，用法见[构建管理器](Publishing.md)。这些标签只用于组织明文导出，不会生成旧式二进制 AssetBundle。
- 完整制作与发布流程见[工作流](Workflow.md)，组件和 Shader 的边界见[支持范围](Status.md)。
