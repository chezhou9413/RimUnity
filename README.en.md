# RimUnity

[简体中文](README.md) | [English](README.en.md)

![RimUnity](Docs/Images/RimUnity-Cover.png)

RimUnity is a Unity asset exporter and runtime framework for RimWorld 1.7. Create prefabs, particles, materials, UI and models in Unity, export their data and dependencies, and load them through the game's Waylith engine. Familiar GameObject, Transform and MonoBehaviour APIs connect your asset workflow to your mod code. See [supported features and limitations](Docs/en/Status.md) for the available functionality.

## Quick start

**0.1.9 Preview: [Standalone ZIP](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/RimUnity-0.1.9-preview.zip) · [Embedded runtime ZIP](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/RimUnity.Embedded-0.1.9-preview.zip) · [Unity exporter TGZ](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/com.chezhou.rimunity-0.1.9.tgz) · [SHA-256](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.9-preview/SHA256SUMS.txt)**

| Integration | Usage |
| --- | --- |
| Standalone dependency | Enable Harmony, RimUnity and content mods. All content shares the standalone runtime. |
| Embedded runtime | Resource-only mods carry `RimUnity.Embedded.dll`. C# content mods must publish deferred scripts through the included `.targets`. See the [integration guide](Docs/en/EmbeddedRuntime.md). |

**Both modes require Harmony.** An enabled `Chezhou.RimUnity` always wins; embedded resources are not mounted. An incompatible standalone version reports an error. Otherwise, the newest compatible embedded version satisfying script requirements is selected, and identical copies run once. `References` is for compilation only; never deploy it as runtime DLLs. There are no online checks or automatic updates; offline play is supported.

RimUnity provides compatibility APIs on Waylith and **does not include or load a separate, real Unity engine**. This preview passed compilation, .NET 10 offline checks and actual game startup with three embedded test mods, with and without standalone. Long-running map rendering and visual UI checks remain unverified. See [validation scope](Docs/en/Release-0.1.9.md).

Players only need Harmony, RimUnity and the relevant content mod; the Unity Editor is not required. Asset authors should start with [installing the exporter and exporting a first prefab](Docs/en/ExporterSetup.md).

Players should extract the standalone ZIP into the game's `Mods` directory. It includes Core, the main runtime and Spine. Build a source checkout first; release packages need no compilation.

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
- [Embedded runtime integration](Docs/en/EmbeddedRuntime.md)
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
- [Clip screen effects](Docs/en/ScreenEfx.md)
- [Supported features and limitations](Docs/en/Status.md)
- [Built-in defs](Docs/en/Defs.md) · [Shader directory](Docs/en/Shaders.md)

## License

Contributors: chezhou9413, keeptpa and Karl-441. New maintainers, contributors and content mod authors are welcome! [GitHub repository](https://github.com/chezhou9413/RimUnity) · [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3819570122).

RimUnity is available under the [MIT license](LICENSE). See the [third-party notices](ThirdParty/NOTICE.md) for additional copyright information. Artwork, fonts and other assets in content mods retain their own licenses.
