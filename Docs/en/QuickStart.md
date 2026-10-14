# Getting started

[简体中文](../zh-CN/QuickStart.md) | [English](QuickStart.md)

Players only need the game-side dependencies and content mods. The steps below are for authors. Start with [exporter installation and your first export](ExporterSetup.md) for downloads, Package Manager instructions and tool path setup.

1. Build and install RimUnity, then enable Harmony and RimUnity in the game.
2. In Unity 2022.3, install `UnityPackage/package.json` or the released UPM TGZ through Package Manager.
3. Create a prefab in your Unity project. Your destination content mod needs a valid `About/About.xml`, a dependency on RimUnity and the appropriate load order.
4. Open `Tools → RimUnity → 导出明文预制体` (Export plain-text prefab). Enter a logical path and description, then select the content mod directory. Use the particle export window for batches of particle prefabs.
5. To export custom shaders, configure the paths to `RimUnity.ShaderTool.exe` and your local RimWorld installation.
6. In the game, call `PrefabLoader.Load(contentMod, logicalPath)` to obtain a template, then create an instance with `RimUnity.Object.Instantiate`.

The catalog and dependencies are written to the content mod's `1.7/Data/RimUnity` directory. Include every file referenced by the catalog when distributing that mod. Keep content assets in their own mod, outside the RimUnity framework directory.

For API examples, see [types and scripts](Usage.md) and [the particle quick start](ParticleQuickStart.md).
