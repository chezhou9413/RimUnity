# Installing the exporter and exporting your first prefab

[简体中文](../zh-CN/ExporterSetup.md) | [English](ExporterSetup.md)

## Choose the files you need

| User | Requirements |
|---|---|
| Players | Harmony, the RimUnity game mod and a content mod that uses it. Unity and the exporter are not required. |
| Asset authors | Unity 2022.3 LTS, the RimUnity Exporter UPM package and a local RimWorld 1.7 installation. Compiling game-side scripts requires the .NET 10 SDK. |

The Workshop mod supplies the game runtime. Authoring tools are distributed separately as `com.chezhou.rimunity-<version>.tgz`, containing the Unity package and offline ShaderTool. Install the Unity Editor separately; it is not included in either package.

**[Download exporter 0.1.5 directly](https://github.com/chezhou9413/RimUnity/releases/download/v0.1.5/com.chezhou.rimunity-0.1.5.tgz)**, or choose a TGZ matching your game-side version from [GitHub Releases](https://github.com/chezhou9413/RimUnity/releases). `RimUnity-<version>-preview.zip` is the game mod, not a Unity package. Local packaging output also includes a timestamp in its filenames. Source installation is available below as well.

## Install into Unity

**From a TGZ:** open `Window → Package Manager` in Unity, select `+ → Add package from tarball`, and choose the downloaded `.tgz`. Wait for dependency import and script compilation. You do not need to extract it or copy scripts into Assets. See [Unity's installation instructions](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-tarball.html).

**From source:** download or clone [the RimUnity repository](https://github.com/chezhou9413/RimUnity), select `+ → Add package from disk` in Package Manager, and open `UnityPackage/package.json`. Keep the repository directory in place: Unity references those files. Follow [the workflow guide](Workflow.md) to build the runtime and ShaderTool as well; a source checkout is not a prebuilt tool package. See [Unity's local package instructions](https://docs.unity3d.com/2022.3/Documentation/Manual/upm-ui-local.html).

Use supported features of Built-in or URP 14. The package declares UGUI, TMP and Newtonsoft.Json dependencies. If the RimUnity menus do not appear, check the Console for compilation or package import errors first.

## Configure output and ShaderTool paths

Target your own content mod root, such as `C:/Mods/MyContentMod`, containing a valid `About/About.xml`. Select the root, not its `1.7` subdirectory, and keep your content outside the RimUnity framework directory.

| Field | Required path |
|---|---|
| Target Mod directory | Content mod root containing `About/About.xml` |
| RimWorld installation directory | RimWorld 1.7 game root containing `Waylith.dll`, not the Mods directory |
| ShaderTool exe | Full path to `RimUnity.ShaderTool.exe` |

The TGZ includes the tool under the installed package's `Tools~/ShaderTool`. Open `Tools → RimUnity → 一键导出项目粒子` (Batch export project particles) first: when no valid saved tool path exists, this window looks for the bundled tool. Copy the displayed `ShaderTool exe` path into the single-prefab window or build manager. If an older tool path is still shown, select the tool matching your current version.

For a source installation, the compiled tool is at `Tools/bin/ShaderTool/RimUnity.ShaderTool.exe` in the repository. Keep its DLL, deps.json and runtimeconfig.json alongside the executable; do not move the EXE alone. The tool needs a working .NET 10 environment. Installing the .NET 10 SDK also prepares you to compile game-side scripts.

ShaderTool uses compiler interfaces from your local game. Game assemblies are not distributed with the package, so authors must configure their own installation path. Custom shader conversion in single-asset export requires both tool paths; the build manager checks them as well.

## Export your first particle prefab

1. Create a simple ParticleSystem in Unity, confirm it plays correctly, and save it as a prefab asset in the project.
2. Open `Tools → RimUnity → 导出明文预制体` (Export plain-text prefab) and assign the asset to the `Prefab` field.
3. Set its logical path to `Effects/BasicBurst` and enter a useful description.
4. Select your content mod root, configure the game and ShaderTool paths, and click `导出` (Export).
5. Check the window result and Console. A successful export creates `1.7/Data/RimUnity/catalog.json` and dependency files in the content mod.

Select a project prefab asset, not a scene instance. The exporter collects materials, textures and other dependencies. Resolve unsupported shader or component diagnostics as reported. Export success confirms that files were generated; visual correctness still needs to be checked in the game.

## Use it in the game

Declare the RimUnity dependency, reference the framework assemblies, and call this on the game main thread after a map has loaded:

```csharp
var template = RimUnity.Assets.PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
instance.transform.position = new RimUnity.Vector3(x, y, z);
instance.SetActive(true);
```

`contentMod` must own the exported resources. The caller supplies `x`, `y` and `z` as map world coordinates. For explicit particle playback and stopping, see [the particle quick start](ParticleQuickStart.md). For dependencies, type references and disposal, see [the API quick start](ApiQuickStart.md). The exporter does not automatically trigger effects in gameplay or compile your mod scripts.

Restart the game after replacing files. Distribute your mod assemblies, catalog and all resource dependencies with the content mod. Players do not need the Unity project or ShaderTool.

## Working with more assets

- Export particle prefabs by folder through `Tools → RimUnity → 一键导出项目粒子`.
- Manage AssetBundle labels, standalone entries and multiple output directories through `RimUnity → 明文资源构建管理器`; see [the build manager guide](Publishing.md). Labels organize plain-text exports rather than generating legacy binary AssetBundles.
- See [the workflow guide](Workflow.md) for complete authoring and distribution, and [supported features](Status.md) for component and shader limitations.
