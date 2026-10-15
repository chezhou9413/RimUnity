# RimUnity

[简体中文](README.md) | [English](README.en.md)

![RimUnity](Docs/Images/RimUnity-Cover.png)

RimUnity is a Unity asset exporter and runtime framework for RimWorld 1.7. Create prefabs, particles, materials, UI and models in Unity, export their data and dependencies, and load them through the game's Waylith engine. Familiar GameObject, Transform and MonoBehaviour APIs connect your asset workflow to your mod code. See [supported features and limitations](Docs/en/Status.md) for the available functionality.

## Quick start

**Download: [Unity exporter 0.1.5 with ShaderTool](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.5/com.chezhou.rimunity-0.1.5.tgz) · [Matching game runtime and all releases](https://github.com/chezhou9413/RimUnity/releases)**

Players only need Harmony, RimUnity and the relevant content mod; the Unity Editor is not required. Asset authors should start with [installing the exporter and exporting a first prefab](Docs/en/ExporterSetup.md).

The repository includes ready-to-use `RimUnity.Core.dll` and `RimUnity.dll` files under `1.7/Assemblies`. After downloading or cloning it, place the entire repository root containing `About` and `LoadFolders.xml` in the game's `Mods` directory. No runtime compilation is required.

1. Install and enable Harmony and RimUnity in RimWorld.
2. In Unity 2022.3, use **Add package from disk** in Package Manager to install `UnityPackage/package.json`, or install a released UPM TGZ. Selected features of Built-in and URP 14 projects are supported.
3. Create a prefab and export it to your content mod through `Tools → RimUnity`.
4. Target .NET 10 in your game-side project and reference `RimUnity.Core.dll`, `RimUnity.dll` and the game assemblies you need.

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = new RimUnity.Vector3(x, y, z);
```

`contentMod` is the mod that owns the assets. Exported resources live in its `1.7/Data/RimUnity` directory. Load them using the logical path entered in the export window.

## Documentation

Open **RimUnity → 百科全书** in Unity for the offline handbook, full-text search, and bundled README/documentation originals. Export callbacks are described in [export processors](Docs/en/ExportProcessors.md).

- [Getting started](Docs/en/QuickStart.md)
- [Installing the exporter and your first export](Docs/en/ExporterSetup.md)
- [API quick start](Docs/en/ApiQuickStart.md)
- [MonoBehaviour and common methods](Docs/en/MonoBehaviour.md)
- [Types and scripts](Docs/en/Usage.md)
- [Loading assets and directory layout](Docs/en/ResourceLoading.md)
- [Authoring, exporting and publishing](Docs/en/Workflow.md)
- [Plain-text asset build manager](Docs/en/Publishing.md)
- [Exporting and using particles](Docs/en/ParticleQuickStart.md) · [Particle modules](Docs/en/ParticleExpansion.md)
- [Custom shaders](Docs/en/CustomShaderConversion.md) · [IMGUI materials](Docs/en/ImmediateUI.md)
- [Color and Bloom](Docs/en/ColorAndBloom.md)
- [Supported features and limitations](Docs/en/Status.md)
- [Built-in defs](Docs/en/Defs.md) · [Shader directory](Docs/en/Shaders.md)

## License

Authors: chezhou9413 and keeptpa.

RimUnity is available under the [MIT license](LICENSE). See the [third-party notices](ThirdParty/NOTICE.md) for additional copyright information. Artwork, fonts and other assets in content mods retain their own licenses.
