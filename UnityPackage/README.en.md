# RimUnity Exporter

[简体中文](README.md) | [English](README.en.md)

Author prefabs in Unity and export plain-text assets for RimWorld 1.7.

## Installation

Use Unity 2022.3 and open `Window → Package Manager`. For source, choose `+ → Add package from disk` and select this package's `package.json`. For a released `.tgz`, choose `+ → Add package from tarball`. Do not extract it or copy scripts into Assets.

Check [GitHub Releases](https://github.com/chezhou9413/RimUnity/releases) for `com.chezhou.rimunity-*.tgz`. The game mod ZIP is not a Unity package. If no attachment is available, install from source and build ShaderTool. See [exporter installation and your first export](https://github.com/chezhou9413/RimUnity/blob/main/Docs/en/ExporterSetup.md) for complete steps.

Author content with native Unity GameObject, Transform, UGUI, ParticleSystem and supported model/animation components.

## Export

1. Open `Tools → RimUnity → 导出明文预制体` (Export plain-text prefab).
2. Select a prefab asset in the project and enter its description and logical path.
3. Select a content mod directory with a valid `About/About.xml`.
4. For custom shaders, configure the RimWorld installation and `RimUnity.ShaderTool.exe` paths.
5. Distribute the catalog and dependencies under `1.7/Data/RimUnity` with the content mod.

The exporter resolves nested prefabs and variants, then collects materials, textures, meshes, fonts, animations and component references. Failures retain diagnostics without replacing the previous successful catalog.

Batch particle menu: `Tools → RimUnity → 一键导出项目粒子` (Batch export project particles).

Script entry point: `RimUnity.Editor.PrefabExporter.Export(modDirectory, ExportRequest[])`.

Offline handbook: **RimUnity → 百科全书**, with topic navigation, full-text search and the original English/Chinese documentation.

Export hooks: derive from `ExportProcessor` and add `[RimUnityExportProcessor]` for automatic discovery, or attach `DelegateExportProcessor` functions through `ExportOptions.processors`. Hooks cover source assets and recursive dependencies. Use `ExportContext.CreateExportCopy<T>()` for temporary changes and `DeferCleanup` for guaranteed restoration. See the handbook's export processor chapter.

## Game-side usage

Reference `RimUnity.Core.dll` and `RimUnity.dll`, and use RimUnity object, component and asset types. Custom scripts must be compiled into a game-side .NET 10 assembly.

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
```

Use matching fully qualified script type names in the authoring and runtime projects. Exported field and event references are restored by the loader. UGUI controls use `RimUnity.UI`, and pointer interfaces use `RimUnity.EventSystems`.

For complete setup, API and support information, see [the English documentation](https://github.com/chezhou9413/RimUnity/blob/main/README.en.md#documentation).

## License

Authors: chezhou9413 and keeptpa.

This package uses its included MIT license. Exported artwork, fonts and other assets retain their own licenses.
