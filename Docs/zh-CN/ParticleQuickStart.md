# 粒子导出与使用

[简体中文](ParticleQuickStart.md) | [English](../en/ParticleQuickStart.md)

在 Unity 使用原生 ParticleSystem 制作 Prefab，通过 RimUnity 的预制体或批量粒子导出窗口写入内容模组。材质、贴图、网格和受支持的 Shader 自动收集；不需要为每个材质手写 ShaderDef。

`contentMod` 是资源所属模组的 `Verse.ModContentPack`，`position` 是地图世界坐标。以下示例要求预制体根节点挂有 ParticleSystem，在游戏主线程调用：

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = position;
instance.SetActive(true);
instance.GetComponent<RimUnity.ParticleSystem>().Play();
```

需要结束特效时，对粒子调用 `Stop()` 停止发射；需要立即移除时调用 `RimUnity.Object.Destroy(instance)`。这些调用应放在对应游戏事件中，不要紧接在创建之后执行。

组件使用 `RimUnity.ParticleSystem`，可通过类型别名保留原调用习惯。不要误用游戏兼容层中的同名组件。

`Play()` 默认包含子级及关联子发射器。不要无条件遍历所有子粒子逐个播放，以免独立启动本应由事件触发的子发射器；多个独立发射根应由自己的控制脚本分别启动。

内容作者应对照原 Prefab 检查方向、大小、透明度、发射时机和生命周期。导出诊断会定位不受支持的组件、Shader 或字段；允许缺失脚本时应明确接受相应逻辑缺失。

模块范围与边界见 [粒子能力](ParticleExpansion.md)，泛光接入见 [颜色与 Bloom](ColorAndBloom.md)。
