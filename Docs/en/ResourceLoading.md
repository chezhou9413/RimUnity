# Loading assets and directory layout

[简体中文](../zh-CN/ResourceLoading.md) | [English](ResourceLoading.md)

## Asset ownership and logical paths

Assets belong to the content mod that uses them. Example layout:

```text
MyContentMod/
├─ About/About.xml
├─ LoadFolders.xml
└─ 1.7/
   ├─ Assemblies/MyContentMod.dll
   └─ Data/RimUnity/
      ├─ catalog.json
      ├─ exports/<generation>/...
      └─ blobs/<content-hash>.<extension>
```

`LoadFolders.xml` can use `<v1.7><li>1.7</li></v1.7>` to load this version's assemblies and Defs. RimUnity's ModContentPack entry point reads `mod.RootDir/1.7/Data/RimUnity`; it does not automatically resolve alternative resource directories.

The catalog records the format version, owner, stable asset IDs, names, descriptions, logical paths, relative file paths, types and dependencies. Load an asset by its logical path, such as `Effects/BasicBurst`, rather than a path such as `exports/<generation>/xxx.json`, which can change between exports. The loader does not look for similarly named resources in other mods to fill missing dependencies.

## Public entry points

| API | Result or purpose |
|---|---|
| `PrefabLoader.Load(mod, path)` | Shared GameObject template from the specified content mod |
| `PrefabLoader.LoadDirectory(dataDirectory, path)` | Template from an explicit Data/RimUnity directory |
| `AssetLoader.Load<T>(mod, path)` | Exported object, such as a template, material, texture or shader |
| `AssetLoader.LoadDirectory<T>(dataDirectory, path)` | Typed object from an explicit directory |
| `AssetLoader.LoadBundleAsset<T>(mod, bundle, assetPath)` | Plain-text resource addressed by its retained bundle name and Unity project path |

Generic types must derive from `RimUnity.Object` and match the exported resource's actual type.

```csharp
using RimUnity.Assets;
using RimUnity;

var template = PrefabLoader.Load(contentMod, "Effects/BasicBurst");
var instance = RimUnity.Object.Instantiate(template);
var material = AssetLoader.Load<Material>(contentMod, "Materials/Glow");
var texture = AssetLoader.Load<Texture2D>(contentMod, "Textures/Noise");
var shader = AssetLoader.Load<Shader>(contentMod, "Shaders/Dissolve");
```

These paths are examples and must exist in your catalog. Automatically collected dependencies normally use internal `__assets/` paths. Export materials, textures or shaders as standalone entries with explicit logical paths if scripts need to load them directly by a stable name.

## Loading by bundle name

If you use `BundleAssetExporter` to export assets carrying Unity AssetBundle labels:

```csharp
var template = AssetLoader.LoadBundleAsset<GameObject>(
    contentMod, "myeffects", "Assets/Effects/Portal.prefab");
```

The resulting logical path is `Bundles/myeffects/Assets/Effects/Portal.prefab`. Bundle names group plain-text resources; no binary AssetBundle is read. This API cannot load an unexported AssetBundle path. The batch particle window uses a different convention: `Assets/Effects/Portal.prefab` becomes `Particles/Effects/Portal`. Always check the final catalog.

## Caching, sharing and disposal

- Catalogs and resources are cached by directory and asset ID for the lifetime of the process. Several prefabs referencing the same material, texture or mesh load it only once. Logical paths and bundle names use lookup indexes; JSON is decoded directly from file streams.
- `blobs/` shares identical files on disk. Different asset IDs remain distinct objects, including textures with identical pixels but different color-space or sampling settings. Resource caches stay separate across mods; matching names or images do not merge objects.
- Prefab templates remain inactive. Instances retain the exported hierarchy, fields, references and active states.
- Do not modify or destroy shared materials, textures or templates returned by the loader. Prefer a renderer's MaterialPropertyBlock for per-instance parameters, or create and dispose of your own Material copy.
- Destroy a GameObject when it is no longer needed. Do not assume every one-shot particle effect automatically destroys its root.
- Restart the game after changing exported files. There is no automatic catalog refresh, template hot reload or public resource unloading API.

## Distributing content assets

RimUnity 0.1.8 stores GPU texture payloads as `.rutex.gz` when the original is at least 4 KiB and lossless Gzip saves at least 5% and 256 bytes. Otherwise `.rutex` is retained. The exported GPU blocks, dimensions, opacity and mip chain are unchanged. The runtime streams directly into final mip buffers and retains existing resource and host disk caches, including recovery when cached data is missing. No GPU compression is performed during game loading.

Both the exporter and `ExportStorage.CopyForPublish` apply this policy; compaction can also upgrade existing exports. Update RimUnity.Core and RimUnity together to 0.1.8 before using compressed textures. Older runtimes cannot read them; the new runtime still reads old `.rutex` files. Disk size decreases, GPU memory does not. Initial reads and recovery without cached data add CPU decompression work.

Include `catalog.json` and all referenced files, including `blobs/`. The build manager compacts its output automatically. For direct `PrefabExporter.Export` calls or per-item particle exports, create a compact copy from your own Editor script without converting the Unity assets again:

```csharp
RimUnity.Editor.ExportStorage.CopyForPublish(
    "C:/Mods/MyContentMod/1.7/Data/RimUnity",
    "C:/Builds/MyContentMod/1.7/Data/RimUnity");
```

Both arguments are asset directories, not mod roots. The destination must be absent or empty. Neither directory may equal or contain the other. The source is unchanged. Missing dependencies or invalid paths produce errors; do not distribute a failed destination. Distribute the complete successful output as your content mod's `1.7/Data/RimUnity` directory.

Compaction keeps public entries and their dependencies, writes compact JSON and shares identical texture, font, GLSL and other payload files by SHA-256. Asset IDs, logical paths and explicit parameters are preserved. Keeping only the newest generation can break entries that were not re-exported.

Large asset JSON is stored as Gzip without changing loading APIs. If the input already shares files across mods, pass a third argument to `CopyForPublish`: `owner => ownerDirectories[owner]`. Each dictionary value is that package ID's `1.7/Data/RimUnity` directory. This materializes external files into a self-contained copy. To share files across several prepared mods, call `ExportSharing.Share(prepared)` with a dictionary mapping real mod roots to prepared asset directories. The build manager handles these steps automatically.

To optimize existing exports offline, run from the repository:

```powershell
dotnet run --project Tools/ResourcePacker/ResourcePacker.csproj -c Release -p:UnityEditorDir="C:/Unity/2022.3/Editor/Data" -- "C:/Builds/Optimized" "C:/Mods/BaseMod" "C:/Mods/AddonMod"
```

Requires .NET 10 SDK and the Newtonsoft.Json assembly supplied with your Unity Editor; neither Unity nor the game is launched. The output directory must not exist. Output is grouped by package ID and accompanied by a ZIP. It contains assets only, not complete mods: replace each mod's `1.7/Data/RimUnity` with its matching output for distribution. Source directories remain unchanged. Include all owners of shared input files in the arguments.

`FAILED.txt`, batch reports, `.export.lock` and temporary files are not runtime resources. Let the exporter maintain stable IDs, relative paths and dependencies instead of editing them by hand.

`Tools/Pack.ps1` packages the framework only; it does not collect content mod resources. Content authors are responsible for permission to redistribute images, fonts and models. RimUnity's MIT license does not change their licenses.

## Common loading problems

| Error | Check first |
|---|---|
| Resource not found | Correct contentMod, existing logical path, no accidental use of a source file path |
| Zero or multiple matching types | Loaded mod DLL, matching fully qualified name, duplicate definitions; register an alias before loading if needed |
| Missing dependency or file | Omitted generations or dependencies; catalog and files from different exports |
| Resource type mismatch | Whether `T` in `Load<T>` matches the exported record |
| Shader compilation or layout error | Tool and game versions, conversion diagnostics, complete GLSL and descriptor files |
| Loaded but invisible | Instance active state, position/scale, material alpha, layer/render channel, pause and emission conditions |
