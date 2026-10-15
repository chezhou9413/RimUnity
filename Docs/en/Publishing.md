# Plain-text asset build manager

[简体中文](../zh-CN/Publishing.md) | [English](Publishing.md)

Unity menu: `RimUnity / 明文资源构建管理器` (Plain-text asset build manager). When installed as a local UPM package, the window uses the package source directly. There is no need to copy scripts into Assets.

Configuration is stored in the authoring project's `ProjectSettings/RimUnity.json`. UI flags are stored in `ProjectSettings/RimUnityWindow.json`. No configuration file is required on first launch. In `构建设置` (Build settings), set the content repository, ShaderTool, game directory and destination mod roots for AssetBundle labels, then save. Keep authoring-project configuration out of the RimUnity framework repository.

Entries come from configured AssetBundle labels and manually added assets. Texture, material, shader and animation dependencies are collected automatically. Output goes to `1.7/Data/RimUnity` beneath the target mod root. Reports are stored in the authoring project's `Library/RimUnity/Reports`.

## Build settings and export logs

Build settings groups output mappings, export options and authoring tools into separate sections. The content scrolls while the save action remains visible at the bottom. Save your edits to write them to the project's configuration.

Export logs lists one timestamped entry per publication attempt on the left, with full/incremental mode and success/failure/cancellation status. Select an entry to view its summary, inputs and dependencies, published resources and physical files, shared-file owners, absolute output paths and execution details. Incremental output includes retained entries. Failed or cancelled attempts do not show the old catalog as successful new output.

The latest 50 records are saved in the project's `Library/RimUnity/Reports/export-history.json` and restored when reopening the window. Deleting Library removes this history. Output snapshots remain unchanged by later publications. Bottom actions copy or save the selected complete record; saving reveals its file. Legacy line logs are not converted to history entries.

## Flat inventory and drag-and-drop ownership

The `资源清点` (Inventory) tab lists AB packages on the left and a flat table of roots and dependencies on the right. Rows include a small asset preview, type, AB owner, output mod, source-file size and status. Shared source paths appear once and remain visible in referencing packages. Unlabeled automatic dependencies are distinguished from unassigned resources. Source-file size is not exported size or GPU memory usage.

Filter by package, type, UI status (all/UI/non-UI), shared usage, issues or text; click headers to sort. Use checkboxes, Ctrl/Command selection or Shift ranges. Double-click a row to locate the asset. Drag selected rows onto an AB package to migrate ownership, or use the selection toolbar. Assets dragged from Project are also accepted. Package resources are read-only. An existing AB without an output mapping gets a blank mapping when assigned; configure its output before publishing.

Each row has a dedicated UI checkbox, including automatic dependencies. The selection toolbar can update multiple rows together. Changes are saved immediately to `ProjectSettings/RimUnityWindow.json` and survive reopening the window. Marked materials and shaders use UI export mode even when collected as dependencies. Flags on other types provide classification and do not change existing automatic Canvas detection in prefabs. Marking a dependency does not register it as an independent export entry. The UI filter combines with the other filters. Marking a shader as UI also makes every referencing material use UI export mode. If a shader is shared between world and UI content, mark only the UI materials.

`新建 AB 包` creates an empty package mapping. `导出选中` incrementally publishes the selected resources as explicit entry points, preserving other published entries. The main build button still processes all configured entries regardless of filters. A shared dependency needs one unambiguous target before exporting it independently. Publication validation and rollback remain in effect.

A full rebuild contains the currently configured entries. It replaces existing data after every target completes export and structural validation. Use incremental mode to retain other entries. Search and issue filters only affect the list display; remove unwanted entries from AssetBundle labels or manual entries to exclude them. Failed checks, failed exports and cancellation during preparation do not replace published data. A failure during publication restores the previous version. Structural validation does not establish visual correctness in the game.

Entries targeting the same mod are exported in one batch, reusing textures, materials, meshes and shader variants by asset ID. Before publication, the manager keeps only catalog entries and their dependencies, removes unreferenced historical files and stores identical payload files once under `blobs/`. Incremental mode also compacts the directory while retaining public entries that were not re-exported.

Catalogs and small configuration files remain plain text. Prefab, Mesh, AnimationClip and RuntimeAnimatorController JSON larger than 4 KiB is stored as `.json.gz` when compression saves space. The game decompresses it as a stream; standard Gzip tools can restore readable JSON. Whitespace and null fields are omitted while explicit zero values, false values, particle parameters and precision are retained. Deduplication does not merge distinct asset IDs or reduce texture resolution. Single-prefab and per-item particle exports retain plain JSON and historical directories; use the [publish-copy API](ResourceLoading.md#distributing-content-assets) before distribution.

When publishing several mods together, the manager shares identical files with required dependencies declared under `modDependencies` or `modDependenciesByVersion/v1.7` in `About/About.xml`. Both mods must participate in the same publication. Unrelated mods remain self-contained. The catalog's `sharedFiles` maps relative file names to owner package IDs. Required owners must be installed and enabled; missing files produce explicit errors. Release the base mod and extensions as matching versions, and rebuild dependent extensions whenever rebuilding the base mod.

Compressed publication uses catalog `formatVersion: 2` and requires RimUnity 0.1.7 or newer. Older runtimes cannot load it. PrefabLoader, AssetLoader and logical script paths are unchanged.

Version 0.1.8 additionally compresses individual `.rutex` GPU texture payloads as `.rutex.gz` when worthwhile. These files require the matching 0.1.8 runtime; old textures remain supported. This changes disk storage and initial reads without reducing resolution or altering GPU blocks. Existing exports can be compacted offline without re-exporting in Unity.

Missing scripts are recorded as warnings in the prefab and reported when the game loads it. Other exportable components remain usable. Shader Graph and script assets requiring separate compilation are identified in the list; remove those entries to export the remaining content.

## Renderer export settings

The output configuration page provides independent options to disable Cast Shadows, Receive Shadows, Light Probes and Reflection Probes. All options are off by default and are saved in `ProjectSettings/RimUnity.json`. Enabled options affect only the export copy and log the asset, hierarchy, component and previous value. Source prefab and model assets remain unchanged. Disabling reflection probes does not alter material cubemaps. With an option off, strict validation still applies.

For scripts, use `PrefabExporter.Export(modDirectory, new ExportOptions { disableCastShadows = true }, requests)`. The original overload retains strict behavior without global switches.
