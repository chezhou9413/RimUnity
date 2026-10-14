# Plain-text asset build manager

[简体中文](../Publishing.md) | [English](Publishing.md)

Unity menu: `RimUnity / 明文资源构建管理器` (Plain-text asset build manager). When installed as a local UPM package, the window uses the package source directly. There is no need to copy scripts into Assets.

Configuration is stored in the authoring project's `ProjectSettings/RimUnity.json`. UI flags are stored in `ProjectSettings/RimUnityWindow.json`. No configuration file is required on first launch. In `输出与工具配置` (Output and tool configuration), set the content repository, ShaderTool, game directory and destination mod roots for AssetBundle labels, then save. Keep authoring-project configuration out of the RimUnity framework repository.

Entries come from configured AssetBundle labels and manually added assets. Texture, material, shader and animation dependencies are collected automatically. Output goes to `1.7/Data/RimUnity` beneath the target mod root. Reports are stored in the authoring project's `Library/RimUnity/Reports`.

A full rebuild contains the currently configured entries. It replaces existing data after every target completes export and structural validation. Use incremental mode to retain other entries. Search and issue filters only affect the list display; remove unwanted entries from AssetBundle labels or manual entries to exclude them. Failed checks, failed exports and cancellation during preparation do not replace published data. A failure during publication restores the previous version. Structural validation does not establish visual correctness in the game.

Entries targeting the same mod are exported in one batch, reusing textures, materials, meshes and shader variants by asset ID. Before publication, the manager keeps only catalog entries and their dependencies, removes unreferenced historical files and stores identical payload files once under `blobs/`. Incremental mode also compacts the directory while retaining public entries that were not re-exported.

JSON remains plain text. Whitespace and null fields are omitted while explicit zero values, false values, particle parameters and asset precision are retained. File deduplication does not merge the object identities of different asset IDs or reduce texture resolution. Single-prefab and per-item particle exports retain historical directories; use the [publish-copy API](ResourceLoading.md#distributing-content-assets) to compact those results for distribution.

Missing scripts are recorded as warnings in the prefab and reported when the game loads it. Other exportable components remain usable. Shader Graph and script assets requiring separate compilation are identified in the list; remove those entries to export the remaining content.

## Renderer export settings

The output configuration page provides independent options to disable Cast Shadows, Receive Shadows, Light Probes and Reflection Probes. All options are off by default and are saved in `ProjectSettings/RimUnity.json`. Enabled options affect only the export copy and log the asset, hierarchy, component and previous value. Source prefab and model assets remain unchanged. Disabling reflection probes does not alter material cubemaps. With an option off, strict validation still applies.

For scripts, use `PrefabExporter.Export(modDirectory, new ExportOptions { disableCastShadows = true }, requests)`. The original overload retains strict behavior without global switches.
