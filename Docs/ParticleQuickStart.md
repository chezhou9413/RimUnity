# 粒子导出与使用

在 Unity 使用原生 ParticleSystem 制作 Prefab，通过 RimUnity 的预制体或批量粒子导出窗口写入内容模组。材质、贴图、网格和受支持的 Shader 自动收集；不需要为每个材质手写 ShaderDef。

`contentMod` 是资源所属模组的 `Verse.ModContentPack`，`position` 是地图世界坐标。在游戏主线程调用：

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = position;
foreach (var particles in instance.GetComponentsInChildren<RimUnity.ParticleSystem>())
    particles.Play(false);
```

需要结束特效时，对粒子调用 `Stop()` 停止发射；需要立即移除时调用 `RimUnity.Object.Destroy(instance)`。这些调用应放在对应游戏事件中，不要紧接在创建之后执行。

组件使用 `RimUnity.ParticleSystem`，可通过类型别名保留原调用习惯。不要误用游戏兼容层中的同名组件。

内容作者应对照原 Prefab 检查方向、大小、透明度、发射时机和生命周期。导出诊断会定位不受支持的组件、Shader 或字段；允许缺失脚本时应明确接受相应逻辑缺失。

模块范围与边界见 [粒子能力](ParticleExpansion.md)，泛光接入见 [颜色与 Bloom](ColorAndBloom.md)。
