# 快速开始

1. 构建并安装 RimUnity，在游戏中启用 Harmony 与 RimUnity。
2. 在 Unity 2022.3 的 Package Manager 中安装 `UnityPackage/package.json` 或发布的 UPM TGZ。
3. 在自己的 Unity 工程制作 Prefab。目标内容模组应具备有效 `About/About.xml`，并声明 RimUnity 依赖和加载顺序。
4. 使用 `Tools → RimUnity → 导出明文预制体`，填写逻辑路径和描述，选择内容模组目录。批量粒子资源可使用粒子导出窗口。
5. 自定义 Shader 导出需要在工具设置中指定 `RimUnity.ShaderTool.exe` 和本机 RimWorld 安装目录。
6. 游戏端用 `PrefabLoader.Load(contentMod, logicalPath)` 获取模板，再通过 `RimUnity.Object.Instantiate` 创建实例。

资源清单与依赖位于使用方模组的 `1.7/Data/RimUnity`。发布内容模组时应包含清单引用的全部文件；不要将资源放入 RimUnity 框架目录。

API 用法见 [类型与脚本用法](Usage.md) 与 [粒子快速开始](ParticleQuickStart.md)。
