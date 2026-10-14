# Exporting and using particles

[简体中文](../zh-CN/ParticleQuickStart.md) | [English](ParticleQuickStart.md)

Author a prefab with Unity's native ParticleSystem, then export it to a content mod through RimUnity's prefab or batch particle window. Materials, textures, meshes and supported shaders are collected automatically. You do not need to write a ShaderDef for every material.

`contentMod` is the owning mod's `Verse.ModContentPack`, and `position` is a map world position. This example requires a ParticleSystem on the prefab root. Call it on the game main thread:

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = position;
instance.SetActive(true);
instance.GetComponent<RimUnity.ParticleSystem>().Play();
```

Call `Stop()` on the particle system to stop emission, or `RimUnity.Object.Destroy(instance)` to remove the effect immediately. Call these in the corresponding game events, rather than directly after creation.

Use `RimUnity.ParticleSystem`. Type aliases can preserve familiar call sites; do not accidentally use the identically named component from the game's compatibility layer.

`Play()` includes children and associated sub-emitters by default. Do not blindly play every child system individually, since this can independently start sub-emitters that should be event-driven. Control multiple independent emission roots through your own script.

Compare exported content with the source prefab for orientation, scale, transparency, emission timing and lifetime. Export diagnostics identify unsupported components, shaders and fields. Allow missing scripts only when you accept the resulting loss of script behavior.

See [particle modules](ParticleExpansion.md) for features and limitations, and [color and Bloom](ColorAndBloom.md) for glow settings.
