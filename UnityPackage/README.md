# RimUnity Exporter

在 Unity 制作 Prefab，并导出为 RimWorld 1.7 可使用的明文资源。

## 安装

使用 Unity 2022.3，在 Package Manager 中通过 **Add package from disk** 选择本包的 `package.json`，或安装发布的 TGZ。

制作时使用 Unity 原生 GameObject、Transform、UGUI、ParticleSystem 和受支持的模型／动画组件。

## 导出

1. 打开 `Tools → RimUnity → 导出明文预制体`。
2. 选择项目中的 Prefab，填写描述与逻辑路径。
3. 选择内容模组目录，该目录需要有效的 `About/About.xml`。
4. 自定义 Shader 需要设置 RimWorld 安装目录与 `RimUnity.ShaderTool.exe` 路径。
5. 导出后，将 `1.7/Data/RimUnity` 中的清单与依赖随内容模组分发。

导出器解析嵌套 Prefab 和 Variant，并收集材质、纹理、网格、字体、动画及组件引用。失败时保留诊断，不替换上一次成功清单。

批量粒子入口：`Tools → RimUnity → 一键导出项目粒子`。

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

本插件使用包内 MIT 许可证。导出的美术、字体和其他资源按各自许可证分发。
