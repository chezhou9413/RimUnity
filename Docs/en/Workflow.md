# Authoring, exporting, integration and publishing

[简体中文](../zh-CN/Workflow.md) | [English](Workflow.md)

## 1. Prepare both environments

Author assets in Unity 2022.3 LTS, using supported features of Built-in or URP 14. The runtime targets Windows x64, RimWorld 1.7 with Waylith, and .NET 10. See [supported features](Status.md).

In Package Manager, choose **Add package from disk** and select `UnityPackage/package.json`, or install a released TGZ. When installing from source, build the runtime and ShaderTool first.

Build from the repository root, substituting your own paths:

```powershell
dotnet build ./Source/RimUnity/RimUnity.csproj -c Release `
  -p:RimWorldDir='C:/Games/RimWorld' `
  -p:HarmonyPath='C:/Mods/Harmony/1.7/Assemblies/0Harmony.dll'

dotnet build ./Source/RimUnity.ShaderTool/RimUnity.ShaderTool.csproj -c Release `
  -p:RimWorldDir='C:/Games/RimWorld'
```

`Directory.Build.props` ships no default paths. Besides the `-p:` properties above, you can set environment variables and then call `dotnet build` directly:

```powershell
$env:RIMWORLD_DIR = 'C:/Games/RimWorld'                       # $(RimWorldDir)
$env:HARMONY_PATH  = 'C:/Mods/Harmony/1.7/Assemblies/0Harmony.dll'   # $(HarmonyPath)
$env:UNITY_EDITOR_DIR = 'C:/Program Files/Unity/2022.3.35f1/Editor/Data'
$env:UNITY_PROJECT = 'C:/Projects/RimUnityBuiltIn'
```

When neither value is provided, the references cannot be resolved (a `warning MSB3245` names the missing assembly but not the missing path), and the real failure surfaces later as a `CS0246` at the compile step that uses those types. Neither mode silently falls back to one machine's private directory.

The build scripts also verify required files instead of only checking that a directory exists: the game directory must contain `Assembly-CSharp.dll` and `Waylith.dll`; Unity Editor/Data must contain `Managed/Newtonsoft.Json.dll` and the UnityEngine assemblies; the Unity project must contain the UGUI/TMP assemblies. A wrong path is reported at the entry point with the missing file named, instead of failing later at compile time. `ResourcePacker` and `PublishingTests` under `Tools/` read the same `UNITY_EDITOR_DIR` through `Tools/Directory.Build.props`, and `-p:UnityEditorDir=` still overrides it.

Deploy and upgrade backups default to `.rimunity-backups` under the game installation directory (outside `Mods`, so the mod scanner does not see them); override with `-BackupRoot` or the `RIMUNITY_BACKUP_DIR` environment variable. The upgrade tool stages into the system temp area, and both the backup and stage paths are printed at the end of the run.

The runtime outputs `RimUnity.Core.dll` and `RimUnity.dll` to `1.7/Assemblies`. The offline tool goes to `Tools/bin/ShaderTool` and requires the target game's installation path. Game DLLs are not included in release packages.

`Compile-Editor.ps1` is an optional package source compilation entry point. It needs a Unity Editor/Data directory and a UnityProject with UGUI and TMP imported. It does not launch Unity or replace the editor's package import checks.

## 2. Author and organize assets

Use native components in your Unity project. Keep Effects, UI, Models and Materials in distinct directories, and create standalone export entries for assets you want to load directly. Unity resolves nested prefabs and variants before the final hierarchy is exported.

Assign a dedicated layer to objects that should contribute to Bloom, including their children. Coordinate layer numbers between content mods. A light, post-processing component or Shader Graph in the source project does not imply equivalent runtime support. Adapt and recompile custom scripts for the game.

The destination is your content mod's root directory, with a valid `About/About.xml` and packageId. Keep your content out of the RimUnity framework directory.

## 3. Export a single prefab

Open `Tools → RimUnity → 导出明文预制体` (Export plain-text prefab):

1. Select a prefab asset in the project, not a scene instance.
2. Enter a description and logical path, such as `Effects/Portal`.
3. Select the destination content mod directory.
4. For custom shaders, set the RimWorld installation and `RimUnity.ShaderTool.exe` paths.
5. Export and check the Unity Console for errors and warnings about omitted functionality.

Local tool paths are stored in EditorPrefs; they are not absolute paths that the content mod must distribute. Output includes UTF-8 JSON, GLSL, images, fonts and other dependency files.

## 4. Batch and standalone asset export

An editor script can export several entries in one transaction:

```csharp
using RimUnity.Editor;
using UnityEditor;
using UnityEngine;

//Run inside an editor method; modDirectory is the content mod's root.
PrefabExporter.Export(modDirectory,
    new ExportRequest {
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Effects/Portal.prefab"),
        path = "Effects/Portal", description = "Portal"
    },
    new ExportRequest {
        asset = AssetDatabase.LoadAssetAtPath<Material>("Assets/UI/Flow.mat"),
        ui = true, path = "Materials/UIFlow", description = "Animated UI material"
    });
```

`ui = true` selects the GUI vertex layout for standalone UI materials and shaders. World materials use the default `false`. A `PrefabExporter.Export` transaction replaces the catalog only after every entry succeeds. Failure preserves the previous successful catalog. Entries not included in this export remain in the catalog.

Open `Tools → RimUnity → 一键导出项目粒子` (Batch export project particles) to find particle prefabs by folder. This window commits prefabs individually, so a batch can partially succeed. For automation:

```powershell
& 'C:/Unity/Editor/Unity.exe' -batchmode -quit `
  -projectPath 'C:/Projects/MyEffects' `
  -executeMethod RimUnity.Editor.ParticleBatchExporter.Batch `
  -rimUnityMod 'C:/Mods/MyContentMod' `
  -rimUnityFolder 'Assets/Effects' `
  -rimUnityReport 'C:/BuildReports/particles.json' `
  -rimUnityShaderTool 'C:/Projects/RimUnity/Tools/bin/ShaderTool/RimUnity.ShaderTool.exe' `
  -rimUnityGame 'C:/Games/RimWorld' `
  -logFile 'C:/BuildReports/unity-export.log'
```

This command launches Unity. Do not run it against a project already open in another editor instance. Create the report directory first. The batch may catch per-item errors and still exit normally, so an exit code alone does not establish success. Check `exported`, `degraded`, `error` and `warnings` in the report.

Keep a graphics device available when exporting textures; do not add `-nographics`. Non-readable or compressed textures need GPU copying and readback. Windows automation can use `-force-d3d11`. When the graphics device is Null, the exporter rejects these textures and preserves the previous successful output.

Use `BundleAssetExporter.Export(modDirectory, reportFile, bundleNames)` to export assets with AssetBundle labels as `Bundles/...` entries. The game loads the exported plain-text assets, not binary AssetBundles.

## 5. Integrate with the game

Follow [the API quick start](ApiQuickStart.md) to declare dependencies, reference assemblies and compile your mod. Load an asset through its owning ModContentPack and logical path when a map or interface needs it, instantiate its template, and let the owner manage stopping and disposal.

Exit the game before replacing game-side resources. Copy your mod DLLs, the catalog and every referenced file, then restart. Runtime hot reload is not available. See [asset loading](ResourceLoading.md) for troubleshooting.

## 6. Package for distribution

After building the runtime and ShaderTool, package the framework with:

```powershell
./Tools/Pack.ps1 -Output 'C:/Builds/RimUnity' -TempRoot 'C:/BuildTemp/RimUnity'
```

This produces a framework Mod ZIP and a UPM TGZ, including MIT and third-party license files. The framework package excludes content assets, Unity projects, caches, game assemblies and test assets. The TGZ includes the project's tool under `Tools~/ShaderTool`; it still requires a local game installation path.

Package your content mod separately: its About, LoadFolders, mod DLLs, Defs and every dependency under `1.7/Data/RimUnity`. Do not copy only the latest generation, since other catalog entries may still reference earlier files.

This repository uses MIT. Artwork, fonts and third-party plugins in content mods retain their own licenses.
