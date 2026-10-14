# RimUnity

在 Unity 制作资源，导出明文 Prefab 与依赖，在 RimWorld 1.7 中加载和使用。

## 快速开始

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

## 使用文档

- [快速开始](Docs/QuickStart.md)
- [API 快速接入](Docs/ApiQuickStart.md)
- [MonoBehaviour 与常用方法](Docs/MonoBehaviour.md)
- [类型与脚本用法](Docs/Usage.md)
- [资源载入与目录约定](Docs/ResourceLoading.md)
- [制作、导出与发布工作流](Docs/Workflow.md)
- [Unity 明文资源构建管理器](Docs/Publishing.md)
- [粒子导出与使用](Docs/ParticleQuickStart.md) · [粒子模块](Docs/ParticleExpansion.md)
- [自定义 Shader](Docs/CustomShaderConversion.md) · [IMGUI 材质](Docs/ImmediateUI.md)
- [颜色与 Bloom](Docs/ColorAndBloom.md)
- [支持范围与限制](Docs/Status.md)

## 许可

本项目使用 [MIT 许可证](LICENSE)。第三方许可见 [版权声明](ThirdParty/NOTICE.md)。内容模组的美术、字体和其他资源按各自许可证分发。
